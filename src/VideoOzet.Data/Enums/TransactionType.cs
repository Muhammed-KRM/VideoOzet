namespace VideoOzet.Data.Enums;

public enum TransactionType
{
    Purchase = 1,    // Satın Alma (Para harcama)
    Spend = 2,       // Jeton Harcama (Mesaj açma veya teklif atma)
    Bonus = 3,       // Hediye Jeton (Kayıt, referans vb.)
    Refund = 4       // İade
}
