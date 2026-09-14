namespace VideoOzet.Business.Helpers;

public static class PromptTemplates
{
    public const string SynthesisResearchPrompt = """
        Aşağıdaki video kaynaklarını kullanarak "{0}" konusunda kapsamlı bir araştırma özeti yaz.
        
        Hedef Uzunluk: {1}
        Hedef Kitle: {2}
        
        KAYNAKLAR:
        {3}
        
        Kurallar:
        - SADECE verilen kaynaklardaki bilgileri kullan
        - Kaynakta olmayan bilgi ekleme
        - Kaynak referanslarını belirt
        - Akademik ve yapılandırılmış bir dil kullan
        - Markdown formatında yaz
        """;

    public const string SynthesisVideoPlanPrompt = """
        Aşağıdaki video kaynaklarını kullanarak "{0}" konusunda yapılandırılmış bir video içerik planı yaz. Videonun süresi ortalama {1} dakika olacak.
        
        KAYNAKLAR:
        {2}
        """;

    public const string ClaimExtractionPrompt = """
        Aşağıdaki metindeki en önemli olgusal iddiaları liste halinde çıkar. Her iddia bağımsız ve tek bir cümleden oluşmalıdır.
        
        METİN:
        {0}
        """;

    public const string QcVerificationPrompt = """
        Aşağıdaki iddiayı verilen kaynakla karşılaştır.
        
        İDDİA: {0}
        
        KAYNAK: {1}
        
        Bu iddia kaynak tarafından destekleniyor mu? Sadece şu 3 seçenekten birini JSON olarak döndür:
        {{ "durum": "desteklendi", "aciklama": "..." }}
        {{ "durum": "belirsiz", "aciklama": "..." }}
        {{ "durum": "desteklenmedi", "aciklama": "..." }}
        """;

    public const string BatchQcPrompt = """
        Aşağıdaki ARAŞTIRMA ÖZETİ metnini dikkatle analiz et. Bu metindeki en önemli ve belirleyici {0} adet olgusal iddiayı tespit et.
        Ardından tespit ettiğin her bir iddianın, verilen KAYNAKLAR tarafından desteklenip desteklenmediğini kaynak metinle karşılaştırarak değerlendir.
        
        ARAŞTIRMA ÖZETİ:
        {1}
        
        KAYNAKLAR:
        {2}
        
        Lütfen değerlendirme sonucunu SADECE geçerli bir JSON dizisi (array) formatında ver. Markdown kod bloğu tırnakları (```json gibi) veya fazladan açıklama yazmadan doğrudan saf JSON döndür.
        Format:
        [
          {{
            "iddia": "Metinden çıkarılan olgusal iddia cümlesi",
            "durum": "desteklendi",
            "aciklama": "İddianın kaynak tarafından nasıl ve nerede desteklendiğini veya desteklenmediğini belirten kısa ve net açıklama"
          }}
        ]
        
        Not: "durum" değeri SADECE "desteklendi", "desteklenmedi" veya "belirsiz" olabilir.
        """;

    public const string RevisionPrompt = """
        Sen uzman bir eğitim ve video içerik mimarısın.
        Aşağıda mevcut bir "{0}" dokümanı ve kullanıcının bu doküman üzerinde yapılmasını istediği özel revizyon talimatı verilmiştir.
        
        DOKÜMAN KONUSU:
        {3}
        
        MEVCUT DOKÜMAN:
        {1}
        
        KULLANICININ REVİZE TALİMATI:
        {2}
        
        KAYNAK BAĞLAMI (Referans gerekirse):
        {4}
        
        ÇOK KRİTİK VE KESİN KURALLAR:
        1. HEDEF ODAKLI DÜZENLEME: Yalnızca ve sadece kullanıcının revize talimatında açıkça belirttiği kısımları (ekleme, çıkarma, ayrıntılandırma, kısaltma, örnek verme vb.) güncelle.
        2. DEĞİNİLMEYEN KISIMLARA KESİNLİKLE DOKUNMA: Kullanıcının değiştirilmesini istemediği hiçbir bölümün başlıklarını, maddelerini, tonunu veya metnini BOZMA, SİLME veya KEYFİ OLARAK DEĞİŞTİRME. O kısımları aynen koru.
        3. AKICILIK VE TUTARLILIK: Yapılan ekleme/değişiklikler dokümanın genel akışına, diline ve Markdown formatına kusursuz şekilde uyum sağlamalıdır.
        4. EKSİKSİZ ÇIKTI: Yanıtında sadece değişen parçayı değil; değişmeyen kısımları aynen muhafaza ederek revize edilmiş TÜM DOKÜMANI baştan sona eksiksiz Markdown formatında döndür.
        """;

    public const string ReQcVerificationPrompt = """
        Sen uzman bir akademik ve eğitim içerik Kalite Kontrol (QC) denetçisisin.
        Aşağıdaki GÜNCEL İÇERİK metnini, verilen REFERANS KAYNAKLARI ve varsa BİR ÖNCEKİ KALİTE KONTROL RAPORUNU incele.

        GÜNCEL İÇERİK:
        {1}

        REFERANS KAYNAKLAR:
        {2}

        ÖNCEKİ KALİTE KONTROL RAPORU (Düzeltilen veya uyarı alan kısımları takip etmek için):
        {3}

        GÖREVİN VE ANALİZ ADIMLARI:
        1. ÖNCEKİ HATA / UYARILARIN KONTROLÜ: Eğer önceki raporda "desteklenmedi" veya "belirsiz" olarak işaretlenmiş iddialar varsa, kullanıcının güncel içerikte bu hataları düzeltip düzeltmediğini, kaynaklarla uyumlu hale getirip getirmediğini öncelikle incele. Düzeltilmişse açıklamanda bunu "Önceki hata düzeltildi ve kaynakla doğrulandı" şeklinde açıkça belirt.
        2. GÜNCEL İÇERİK İDDİALARI: Güncel içerikteki en kritik toplam {0} adet belirleyici olgusal iddiayı (önceki düzeltilenler dahil) tespit et ve referans kaynaklarla karşılaştır.
        3. NET SINIFLANDIRMA: Her bir iddiayı kesinlikle şu 3 durumdan birine ata: "desteklendi", "belirsiz", "desteklenmedi".
        4. AÇIKLAMA: Kaynağa dayanan, yapıcı ve net bir gerekçe yaz.

        Lütfen değerlendirme sonucunu SADECE geçerli bir JSON dizisi (array) formatında ver. Markdown kod bloğu tırnakları (```json gibi) veya fazladan açıklama yazmadan doğrudan saf JSON döndür:
        [
          {{
            "iddia": "Metinden çıkarılan olgusal iddia cümlesi",
            "durum": "desteklendi",
            "aciklama": "İddianın kaynakla uyumuna ve varsa önceki hatanın düzeltilme durumuna dair net açıklama"
          }}
        ]
        """;
}
