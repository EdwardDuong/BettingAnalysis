using BettingAnalysis.Data.Entities;
using BettingAnalysis.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BettingAnalysis.Data.Repositories;

/// <summary>
/// EF Core implementation of IUserRepository.
/// Handles user authentication and account management operations.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly BettingDbContext _context;

    public UserRepository(BettingDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        return await _context.Users
            .Include(u => u.Bets)
            .Include(u => u.BankrollSnapshots)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        return await _context.Users
            .Where(u => u.IsActive)
            .ToListAsync();
    }

    public async Task AddAsync(User user)
    {
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            user.IsActive = false; // Soft delete
            await _context.SaveChangesAsync();
        }
    }
}
