namespace VideoOzet.Data.Enums;

public enum MessageStatus
{
    Sent = 1,        // Gönderildi
    Locked = 2,      // Kilitli (Okumak için jeton harcanması gerekiyor)
    Unlocked = 3,    // Jeton harcanarak kilidi açıldı
    Replied = 4      // Cevaplandı (Sohbet artık tamamen ücretsiz aşamada)
}
