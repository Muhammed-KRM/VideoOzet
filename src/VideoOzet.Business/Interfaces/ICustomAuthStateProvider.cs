namespace VideoOzet.Business.Interfaces;

public interface ICustomAuthStateProvider
{
    Task LogInAsync(Guid id, string fullName, string email, string role, string token = "");
    Task LogOutAsync();
}
