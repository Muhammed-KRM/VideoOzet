namespace VideoOzet.Business.Interfaces;

public interface ISmsService
{
    Task SendAsync(string phoneNumber, string message);
}
