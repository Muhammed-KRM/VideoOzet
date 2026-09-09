using Microsoft.EntityFrameworkCore;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Repositories;

public class UserRepository : GenericRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _dbSet.FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<bool> IsEmailUniqueAsync(string email)
    {
        return !await _dbSet.AnyAsync(u => u.Email == email);
    }
}
