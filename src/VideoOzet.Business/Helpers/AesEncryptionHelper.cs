using System.Security.Cryptography;
using System.Text;

namespace VideoOzet.Business.Helpers;

/// <summary>
/// AES-256-CBC ile TCKN ve IBAN gibi hassas verileri şifreler/çözer.
/// Anahtar uygulama ayarlarından (appsettings) gelmelidir.
/// </summary>
public static class AesEncryptionHelper
{
    public static string Encrypt(string plainText, string key)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // IV + CipherText birleştirilerek Base64 olarak saklanır
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string cipherText, string key)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));

        var iv = new byte[aes.BlockSize / 8];
        var cipher = new byte[fullCipher.Length - iv.Length];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
