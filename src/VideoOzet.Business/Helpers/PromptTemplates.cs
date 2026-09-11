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
}
