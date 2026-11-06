namespace VideoOzet.Data.Enums;

public enum KaynakKullanimTuru
{
    DogrudanReferans = 0,       // Kendi kaynağımız: Cümleler, tanımlar ve örnekler doğrudan kullanılabilir/alıntılanabilir
    SadeceArgumanVeMantik = 1   // Dış kanal/kaynak: Kopyalanamaz; sadece ana fikir, mantıksal örgü ve argümanlar alınıp özgünleştirilir
}
