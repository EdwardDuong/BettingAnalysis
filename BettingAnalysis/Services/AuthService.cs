using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BettingAnalysis.Data;
using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BettingAnalysis.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository  _users;
    private readonly IConfiguration   _config;
    private readonly BettingDbContext  _db;

    public AuthService(IUserRepository users, IConfiguration config, BettingDbContext db)
    {
        _users  = users;
        _config = config;
        _db     = db;
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        var user = await _users.GetByUsernameAsync(username);

        if (user is null || !user.IsActive)
            return new AuthResult(false, Error: "Invalid credentials");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return new AuthResult(false, Error: "Invalid credentials");

        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);

        var refreshToken = await CreateRefreshTokenAsync(user.Id);
        return new AuthResult(true, Token: GenerateToken(user), RefreshToken: refreshToken, User: user);
    }

    public async Task<AuthResult> RegisterAsync(
        string username, string email, string password,
        decimal initialBankroll = 10_000m)
    {
        if (await _users.GetByUsernameAsync(username) is not null)
            return new AuthResult(false, Error: "Username already taken");

        if (await _users.GetByEmailAsync(email) is not null)
            return new AuthResult(false, Error: "Email already registered");

        var user = new User
        {
            Username        = username,
            Email           = email,
            PasswordHash    = BCrypt.Net.BCrypt.HashPassword(password),
            Role            = "User",
            InitialBankroll = initialBankroll,
            CurrentBankroll = initialBankroll,
            CreatedAt       = DateTime.UtcNow,
            IsActive        = true,
        };

        await _users.AddAsync(user);

        var refreshToken = await CreateRefreshTokenAsync(user.Id);
        return new AuthResult(true, Token: GenerateToken(user), RefreshToken: refreshToken, User: user);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken)
    {
        var stored = await _db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (stored is null || stored.IsRevoked || stored.ExpiresAt <= DateTime.UtcNow)
            return new AuthResult(false, Error: "Invalid or expired refresh token");

        if (stored.User is null || !stored.User.IsActive)
            return new AuthResult(false, Error: "User not found or inactive");

        // Rotate: revoke old, issue new
        stored.IsRevoked = true;
        var newRefresh = await CreateRefreshTokenAsync(stored.UserId);
        await _db.SaveChangesAsync();

        return new AuthResult(true, Token: GenerateToken(stored.User), RefreshToken: newRefresh, User: stored.User);
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (stored is not null && !stored.IsRevoked)
        {
            stored.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<AuthResult> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return new AuthResult(false, Error: "User not found");

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return new AuthResult(false, Error: "Current password is incorrect");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _users.UpdateAsync(user);

        return new AuthResult(true);
    }

    private async Task<string> CreateRefreshTokenAsync(int userId)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var token      = Convert.ToBase64String(tokenBytes);
        var expiryDays = int.Parse(_config["Jwt:RefreshExpiryDays"] ?? "30");

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId    = userId,
            Token     = token,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false,
        });
        await _db.SaveChangesAsync();
        return token;
    }

    public string GenerateToken(User user)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry      = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "1440");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role,               user.Role),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:             _config["Jwt:Issuer"],
            audience:           _config["Jwt:Audience"],
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(expiry),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
