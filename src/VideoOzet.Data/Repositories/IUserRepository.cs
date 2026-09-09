using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Repositories;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<bool> IsEmailUniqueAsync(string email);
}
