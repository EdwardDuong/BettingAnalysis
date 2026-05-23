using BettingAnalysis.Data.Entities;

namespace BettingAnalysis.Interfaces;

/// <summary>
/// Repository interface for User entity operations.
/// Supports authentication and user management.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id);
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<IEnumerable<User>> GetAllAsync();
    Task AddAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(int id);
}
