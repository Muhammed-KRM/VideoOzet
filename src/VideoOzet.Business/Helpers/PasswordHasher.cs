namespace VideoOzet.Business.Helpers;

public static class PasswordHasher
{
    // BCrypt work factor: 12 (döküman Faz 7 - Güvenlik)
    private const int WorkFactor = 12;

    public static string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public static bool Verify(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
