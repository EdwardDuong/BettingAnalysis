using BettingAnalysis.Data;
using BettingAnalysis.Data.Repositories;
using BettingAnalysis.Interfaces;
using BettingAnalysis.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BettingAnalysis.Tests.Services;

/// <summary>
/// AuthService previously had zero test coverage. This covers the refresh-token
/// revocation added to ChangePasswordAsync — a security-relevant behavior change
/// (killing stale sessions after a password change) that shipped without a test.
/// </summary>
public class AuthServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly IAuthService _authService;

    public AuthServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"]               = "test-signing-key-at-least-32-bytes-long!!",
                ["Jwt:Issuer"]            = "TestIssuer",
                ["Jwt:Audience"]          = "TestAudience",
                ["Jwt:ExpiryMinutes"]     = "60",
                ["Jwt:RefreshExpiryDays"] = "30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var dbName = $"AuthTestDb_{Guid.NewGuid()}";
        services.AddDbContext<BettingDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthService, AuthService>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _scope.ServiceProvider.GetRequiredService<BettingDbContext>().Database.EnsureCreated();

        _authService = _scope.ServiceProvider.GetRequiredService<IAuthService>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task ChangePassword_RevokesExistingRefreshTokens()
    {
        var registered = await _authService.RegisterAsync("alice", "alice@test.local", "OldPassw0rd!");
        registered.Success.Should().BeTrue();
        var oldRefreshToken = registered.RefreshToken!;

        var changed = await _authService.ChangePasswordAsync(registered.User!.Id, "OldPassw0rd!", "NewPassw0rd!");
        changed.Success.Should().BeTrue();

        var refreshAttempt = await _authService.RefreshAsync(oldRefreshToken);

        refreshAttempt.Success.Should().BeFalse("a refresh token issued before a password change must be revoked");
        refreshAttempt.Error.Should().Be("Invalid or expired refresh token");
    }

    [Fact]
    public async Task ChangePassword_DoesNotAffectAnotherUsersRefreshToken()
    {
        var alice = await _authService.RegisterAsync("alice2", "alice2@test.local", "OldPassw0rd!");
        var bob   = await _authService.RegisterAsync("bob2", "bob2@test.local", "BobPassw0rd!");

        await _authService.ChangePasswordAsync(alice.User!.Id, "OldPassw0rd!", "NewPassw0rd!");

        var bobRefresh = await _authService.RefreshAsync(bob.RefreshToken!);

        bobRefresh.Success.Should().BeTrue("changing Alice's password must not revoke Bob's sessions");
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrentPassword_DoesNotRevokeExistingTokens()
    {
        var registered = await _authService.RegisterAsync("carol", "carol@test.local", "CorrectPassw0rd!");
        var refreshToken = registered.RefreshToken!;

        var result = await _authService.ChangePasswordAsync(registered.User!.Id, "WrongPassword", "NewPassw0rd!");

        result.Success.Should().BeFalse();
        var refreshAttempt = await _authService.RefreshAsync(refreshToken);
        refreshAttempt.Success.Should().BeTrue("a failed password-change attempt must not revoke valid sessions");
    }
}
