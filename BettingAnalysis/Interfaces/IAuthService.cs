using BettingAnalysis.Data.Entities;

namespace BettingAnalysis.Interfaces;

public record AuthResult(
    bool Success,
    string? Token        = null,
    string? RefreshToken = null,
    User?   User         = null,
    string? Error        = null);

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password);
    Task<AuthResult> RegisterAsync(string username, string email, string password, decimal initialBankroll = 10_000m);
    Task<AuthResult> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
    Task<AuthResult> RefreshAsync(string refreshToken);
    Task            RevokeAsync(string refreshToken);
    string GenerateToken(User user);
}
