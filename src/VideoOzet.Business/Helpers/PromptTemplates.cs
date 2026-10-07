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
        1. HEDEF ODAKLI DÜZENLEME: Yalnızca ve sadece kullanıcının revize talimatında açıkça belirttiği kısımları (ekleme, çıkarma, ayrıntılandırma, kısaltma, örnek verme, ton vb.) güncelle.
        2. DOKÜMAN TÜRÜNE VE AMACINA KESİNLİKLE SADIK KAL:
           - Eğer revize edilen doküman "Video Planı" ise: Sahne bazlı [Görsel & Edit Yönergesi] (kamera, B-roll, ekran grafikleri, ses) ve [Prompter Metni (Aynen Okunacak)] (sunucunun kelimesi kelimesine okuyacağı tam konuşma metni; ASLA özet veya taslak değil!) yapısını koru ve talimata göre geliştir.
           - Eğer revize edilen doküman "Araştırma Özeti" ise: Bu doküman bir video planı veya prompter konuşma metni DEĞİLDİR; konunun derinlemesine akademik ve kavramsal araştırma özetidir. Sakın araştırma özetini prompter veya sahne planına dönüştürme!
        3. DEĞİNİLMEYEN KISIMLARA KESİNLİKLE DOKUNMA: Kullanıcının değiştirilmesini istemediği hiçbir bölümün başlıklarını, maddelerini, tonunu veya metnini BOZMA, SİLME veya KEYFİ OLARAK DEĞİŞTİRME. O kısımları aynen koru.
        4. AKICILIK VE TUTARLILIK: Yapılan ekleme/değişiklikler dokümanın genel akışına, diline ve Markdown formatına kusursuz şekilde uyum sağlamalıdır.
        5. EKSİKSİZ ÇIKTI: Yanıtında sadece değişen parçayı değil; değişmeyen kısımları aynen muhafaza ederek revize edilmiş TÜM DOKÜMANI baştan sona eksiksiz Markdown formatında döndür.
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

    public const string TopicAnalysisPrompt = """
        Aşağıdaki kaynak verilerini kullanarak "{0}" konusu için bir 'Konu Analizi' yap.
        Amacımız: Verilen kaynakları en verimli şekilde kullanarak hedef kitlenin ilgisini çekecek, kaynaklardaki asıl kavram ve teorileri doğru aktaran dinamik bir içerik tasarlamak.

        KAYNAKLAR:
        {1}

        ÇOK KRİTİK KURALLAR:
        1. Ana fikir, beklenen konular ve konu haritası SADECE verilen kaynaklardaki gerçek içerik, terim, yazar/hoca anlatımları ve metinlere dayanmalıdır.
        2. Kaynakta bulunmayan harici/genel teorileri veya rastgele konuları uydurma ya da ekleme.
        3. Kaynakta olmayan ama konunun gerektirebileceği şeyleri "KaynaktaOlmayanlar" listesinde açıkça belirt.

        Lütfen SADECE geçerli bir JSON objesi döndür:
        {{
            "AnaFikir": "Tüm kaynaklardan çıkarılan genel ana fikir",
            "BeklenenKonular": [ "Konu 1", "Konu 2" ],
            "KonuHaritasi": [ {{ "Konu": "Konu 1", "Aciklama": "Detay" }} ],
            "KaynaktaOlmayanlar": [ "Şu konular beklenebilir ancak kaynaklarda yok" ],
            "OnerilenVideoSayisi": 3,
            "OneriSuresiDk": 10,
            "OneriGerekcesi": "Neden bu şekilde bölündüğü ve süresi..."
        }}
        """;

    public const string SeriesPlanPrompt = """
        Aşağıdaki konu analizi ve kaynakları göz önünde bulundurarak "{0}" konusu için detaylı bir çoklu video serisi planı oluştur.
        Hedef Kitle: {1}
        
        KONU ANALİZİ:
        {2}
        
        KAYNAKLAR:
        {3}
        
        KULLANICI KISITLARI (Varsa):
        {4}

        ÇOK KRİTİK KURALLAR:
        1. Seri haritasındaki bölümlerin başlıkları, ana fikirleri ve konuları KESİNLİKLE verilen kaynaklardaki gerçek içeriklere, kavramlara ve alt başlıklara dayanmalıdır.
        2. Kaynaklarda yer almayan harici konuları uydurma.
        3. Eğer kullanıcı video sayısını belirtmişse veya bazı konuların dışarıda bırakılmasını istemişse KESİNLİKLE uymalısın.
        
        Lütfen SADECE geçerli bir JSON objesi döndür:
        {{
            "VideoSayisi": 3,
            "VarsayilanVideoSuresiDk": 10,
            "OneridenFarkli": false,
            "SeriHaritasi": [ 
                {{ "BolumNo": 1, "CalismaBasligi": "Bölüm 1", "HedefSureDk": 10, "AnaFikir": "...", "Konular": ["Konu 1", "Konu 2"] }}
            ],
            "DisaridaBirakilanlar": [ "Kaynak yetersizliği veya kısıtlamalar nedeniyle alınmayanlar" ]
        }}
        """;

    public const string SeriesVideoGenerationPrompt = """
        Çoklu video serisinin "{0}" başlıklı bölümü için detaylı araştırma özeti, video planı ve bir sonraki bölüme aktarılacak 'devir notu' oluştur.
        
        BÖLÜM KONULARI:
        {1}
        
        ÖNCEKİ VİDEODAN DEVİR NOTU (Eğer varsa dikkate al, bağlamı koparma):
        {2}
        
        HEDEF KİTLE:
        {3}
        
        KULLANICI TALİMATLARI / ÖZEL DİREKTİFLER:
        {5}

        KAYNAKLAR:
        {4}

        ÇOK KRİTİK VE KESİN KURALLAR:
        1. SADECE VE SADECE verilen KAYNAKLARDAKİ bilgileri, kavramları, argümanları ve analizleri kullan.
        2. Kaynaklarda yer almayan harici/genel teorileri veya konuları uydurma ya da ekleme.
        3. Kaynak metinlerde geçen özel kavramları, tanımları, yazar/hoca atıflarını ve örnekleri aynen ve detaylıca aktar.

        İÇERİK YAPISI VE FORMAT ZORUNLULUKLARI:
        
        A) "ArastirmaOzeti" (Markdown):
           - Bölüm konusunun arka planındaki akademik, felsefi, kavramsal ve analitik bilgileri eksiksiz içeren kaynak tabanlı kapsamlı bir araştırma özetidir.
           - Kesinlikle kamera yönergesi, sahne planı veya prompter konuşma metni İÇERMEMELİ; konunun derinlemesine referans dokümanı olmalıdır.
        
        B) "VideoPlani" (Markdown) - EN KRİTİK BÖLÜM:
           Bu plan, sunucu ve video kurgucusunun doğrudan stüdyoda/montajda kullanacağı profesyonel bir çekim ve kurgu planıdır. Girişten sonuca kadar sahneler halinde (zaman aralıklarıyla) yapılandırılmalıdır.
           Her sahne/bölüm KESİNLİKLE şu İKİ AYRI BÖLÜMDEN oluşmalıdır:
           
           1. [Görsel & Edit Yönergesi] (Kurgu ve Görsel Yönetmeni İçin):
              - Zaman Aralığı: (Örn: 00:00 - 02:30)
              - Kamera Açısı & Kadraj: (Örn: Sunucu merkezde, göğüs planı / Medium Close-Up, loş derinlikli fon)
              - Görsel, B-Roll ve Kurgu: (Ekrana gelecek arşiv görüntüleri, fotoğraflar, simülasyonlar, animasyonlar, borsa/metropol görüntüleri, split-screen vb.)
              - Metin Grafiği (On-Screen Text): (Ekranda belirecek alt yazı / Lower Third, kavram başlıkları, infografik maddeleri veya alıntılar)
              - Müzik & Ses Efekti: (Müziğin tonu, tempo, geçiş ses efektleri (whoosh, daktilo, bas vuruş vb.))
           
           2. [Prompter Metni (Aynen Okunacak)] (Sunucu İçin):
              - Sunucunun kameraya karşı KELİMESİ KELİMESİNE / AYNEN OKUYACAĞI tam konuşma metni.
              - KESİNLİKLE özet, madde işaretli not, taslak veya "burada şundan bahsedilir" şeklinde özetleme OLMAYACAK!
              - Tıpkı profesyonel bir sunucunun prompter cihazından doğrudan okuduğu gibi akıcı, etkileyici, doğal konuşma diliyle ve eksiksiz tam metin olarak yazılmalıdır.

        C) "DevirNotu":
           - Bir sonraki bölüme aktarılması gereken kilit kavram veya geçiş notu.

        Lütfen SADECE geçerli bir JSON objesi döndür:
        {{
            "ArastirmaOzeti": "Detaylı akademik araştırma özeti markdown metni",
            "VideoPlani": "Detaylı görsel edit yönergeleri ve aynen okunacak tam prompter metinlerini içeren video planı markdown metni",
            "DevirNotu": "Varsa sonraki bölüme devir notu"
        }}
        """;

    public const string SourceTopicExtractionPrompt = """
        Aşağıda verilen kaynak içeriği ({0}, Parça {1}/{2}) dikkatlice incele ve içeriğinde anlatılan temel konuları çıkar.
        
        KAYNAK METNİ:
        {3}
        
        GÖREV:
        - İçerikte geçen tüm önemli başlıkları ve konuları tespit et.
        - Her konu için kısa bir açıklama ve tahmini anlatım süresi (dakika) belirle.
        - Yalnızca saf JSON formatında yanıt ver:
        {{
            "Konular": [
                {{
                    "Baslik": "Konu Başlığı",
                    "Aciklama": "Bu konunun içeriği ve nelerin anlatıldığı hakkında 1-2 cümle",
                    "TahminiSureDk": 3
                }}
            ]
        }}
        """;
}
