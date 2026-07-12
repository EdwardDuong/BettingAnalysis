using BettingAnalysis.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BettingAnalysis.Data;

/// <summary>
/// Runs once at startup to ensure the default admin account exists with a valid password hash.
/// Handles both fresh databases and the migration from the old broken-hash seed.
/// </summary>
public static class DataSeeder
{
    private const string DefaultUsername = "admin";
    private const string DefaultEmail    = "admin@betting.local";
    private const string DefaultPassword = "Admin@123";

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BettingDbContext>();

        await db.Database.MigrateAsync();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == DefaultUsername);

        if (existing is null)
        {
            // If the table is empty, reset the identity counter so admin always gets Id = 1.
            // BankrollHealthCheck uses Id = 1 as its system-level proxy user (see Program.cs).
            var anyUser = await db.Users.AnyAsync();
            if (!anyUser)
                await db.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[Users]', RESEED, 0)");

            db.Users.Add(new User
            {
                Username        = DefaultUsername,
                Email           = DefaultEmail,
                PasswordHash    = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
                Role            = "Admin",
                InitialBankroll = 10_000m,
                CurrentBankroll = 10_000m,
                CreatedAt       = DateTime.UtcNow,
                IsActive        = true,
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Default admin account created (username: {Username})", DefaultUsername);
        }
        else if (!BCrypt.Net.BCrypt.Verify(DefaultPassword, existing.PasswordHash))
        {
            // Fix the old "CHANGE_ME" hash from the broken initial seed
            existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword);
            await db.SaveChangesAsync();
            logger.LogInformation("Default admin password hash updated");
        }
    }
}
