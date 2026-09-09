namespace VideoOzet.Data.Enums;

public enum ListingStatus
{
    Pending = 1,     // Onay Bekliyor
    Active = 2,      // Yayında
    Suspended = 3,   // Askıya Alındı (Admin tarafından)
    Closed = 4       // Kapatıldı (Kullanıcı tarafından)
}
