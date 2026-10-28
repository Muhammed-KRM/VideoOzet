namespace VideoOzet.Business.Helpers;

public static class PromptTemplates
{
    public const string SynthesisResearchPrompt = """
        Aşağıdaki video ve doküman kaynaklarını kullanarak "{0}" konusunda kapsamlı, derinlemesine ve eksiksiz bir akademik araştırma özeti yaz.
        
        Hedef Uzunluk: {1}
        Hedef Kitle: {2}
        
        KAYNAKLAR:
        {3}
        
        ÇOK KRİTİK KURALLAR VE BİREBİR MARKDOWN ŞABLONU:
        1. SADECE verilen kaynaklardaki bilgileri, kavramları, delilleri ve örnekleri kullan; kaynakta olmayan bilgi ekleme.
        2. KESİNLİKLE köşeli parantez (`[` veya `]`) içinde şablon yer tutucu ifadeler bırakma; tüm başlıkları ve alt madde etiketlerini konunun gerçek içeriğine göre adlandır.
        3. Özeti kesinlikle 2-3 kısa başlıkla yarıda kesme! Kaynaklardaki tüm alt konuları, delilleri, örnekleri ve karşılaştırmaları eksiksiz kapsayacak şekilde EN AZ 4-5 ANA BÖLÜM (## 2., ## 3., ## 4., ## 5.) ve her ana bölüm altında EN AZ 2 ALT BAŞLIK (### 2.1., ### 2.2. vb.) oluştur ve en sonda mutlaka "## Sonuç ve Genel Değerlendirme" bölümüyle tamamla.
        4. Yanıtını aşağıdaki Markdown yapısına birebir uyarak oluştur:

        # {0}
        **Kapsamlı Araştırma Özeti**
        *(Kaynak: İlgili Kaynak Adları – KAYNAK #1, #2 vb.)*

        ---

        ## 1. Giriş
        1. Konunun temel sorusu, önemi ve tanımlayıcı çerçevesi.
        2. Araştırmanın kapsamı ve kaynaklardaki metodolojik yaklaşım.
        3. Ele alınacak ana başlıklar:
           - 2. (Birinci Ana Bölümün Gerçek Başlığı - köşeli parantez kullanma)
           - 3. (İkinci Ana Bölümün Gerçek Başlığı)
           - 4. (Üçüncü Ana Bölümün Gerçek Başlığı)
           - 5. (Dördüncü Ana Bölümün Gerçek Başlığı)

        ---

        ## 2. (Birinci Ana Kavramsal / Akademik Başlık)
        ### 2.1. (Birinci Alt Başlık)
        > "(Kaynaktan doğrudan kilit alıntı veya temel ilke cümlesi)" — KAYNAK #1

        - **(Konuya Özgü 1. Boyut / Kavram Başlığı):** (Kaynaktaki kavramın detaylı açıklaması; DİKKAT: bu maddelerin başında KESİNLİKLE `>` işareti kullanma, alıntı kutusunun dışında normal madde olarak yaz)
        - **(Konuya Özgü 2. Boyut / Metodolojik Analiz):** (Kaynaktaki delillerin, örneklerin ve analizlerin detaylı açıklaması)
        - **(Konuya Özgü 3. Boyut / Somut Karşılaştırma veya Uygulama):** (Kaynaktaki somut örnekler, atıflar ve çıkarımlar)

        ### 2.2. (İkinci Alt Başlık)
        > "(Kaynaktan doğrudan ilgili ikinci kilit alıntı)" — KAYNAK #1

        - **(Konuya Özgü 1. İlke / Yapı Başlığı):** (Detaylı analiz ve açıklama)
        - **(Konuya Özgü 2. Uygulama / Örnek Başlığı):** (Kaynaktaki somut örnekler ve çıkarımlar)

        ---

        ## 3. (İkinci Ana Kavramsal / Akademik Başlık)
        ### 3.1. (Alt Başlık)
        > "(Kaynaktan doğrudan ilgili kilit alıntı)" — KAYNAK #1

        - **(Konuya Özgü 1. Analitik Nokta):** (Detaylı analiz ve karşılaştırmalar)
        - **(Konuya Özgü 2. Analitik Nokta):** (Kaynaktaki deliller ve değerlendirmeler)

        ### 3.2. (İkinci Alt Başlık)
        > "(Kaynaktan doğrudan ilgili kilit alıntı)" — KAYNAK #1

        - **(Konuya Özgü 1. Analitik Nokta):** (Detaylı analiz)
        - **(Konuya Özgü 2. Analitik Nokta):** (Somut örnekler ve sonuçlar)

        ---

        ## 4. (Üçüncü Ana Kavramsal / Akademik Başlık)
        ### 4.1. (Alt Başlık)
        > "(Kaynaktan doğrudan ilgili kilit alıntı)" — KAYNAK #1

        - **(Konuya Özgü 1. Analitik Nokta):** (Detaylı analiz)
        - **(Konuya Özgü 2. Analitik Nokta):** (Somut örnekler ve sonuçlar)

        ### 4.2. (İkinci Alt Başlık)
        > "(Kaynaktan doğrudan ilgili kilit alıntı)" — KAYNAK #1

        - **(Konuya Özgü 1. Analitik Nokta):** (Detaylı analiz)
        - **(Konuya Özgü 2. Analitik Nokta):** (Somut örnekler ve sonuçlar)

        ---

        ## 5. Sonuç ve Genel Değerlendirme
        1. **(Temel Çıkarım 1):** ...
        2. **(Temel Çıkarım 2):** ...
        3. **(Genel Sentez):** (Tüm bulguların bütünsel değerlendirmesi)
        """;

    public const string SynthesisVideoPlanPrompt = """
        Aşağıdaki video ve doküman kaynaklarını kullanarak "{0}" konusunda stüdyoda ve kurguda doğrudan kullanılacak profesyonel, baştan sona eksiksiz bir Çekim, Kurgu ve Prompter Video Planı oluştur.
        Hedef Süre / Uzunluk: {1}
        
        KAYNAKLAR:
        {2}

        ÇOK KRİTİK VE KESİN KURALLAR:
        1. SADECE VE SADECE verilen KAYNAKLARDAKİ bilgileri, kavramları, argümanları ve analizleri kullan. Kaynaklarda yer almayan harici konuları uydurma.
        2. Kaynak metinlerde geçen özel kavramları, tanımları, yazar/hoca atıflarını ve örnekleri aynen ve detaylıca aktar.
        3. Her sahne için [Prompter Metni (Aynen Okunacak)] kısmı KESİNLİKLE 2-3 cümlelik kısa bir özet OLMAYACAK; sunucunun kameraya karşı kelimesi kelimesine okuyacağı EN AZ 3 DOLU PARAGRAF (180-250 kelime) uzunluğunda akıcı stüdyo konuşma metni olacaktır. Her paragrafın arasına mutlaka boş bir `>` satırı koy ki paragraflar birbirine yapışmasın.
        4. Video planını KESİNLİKLE yarıda bırakma! Konunun tüm alt başlıklarını kapsayacak şekilde EN AZ 6 SAHNE (Sahne 1: Giriş, Sahne 2-5+: Gelişme Sahneleri ve son sahne olarak Kapanış / Sonuç Sahnesi) oluştur.
        5. Yanıtını KESİNLİKLE aşağıdaki Markdown şablonuna birebir uyarak (JSON sarmalaması olmadan) döndür:

        # {0}
        **Video Tipi:** Uzun Formatlı Stüdyo Analizi / Ders
        **Hedef Süre:** {1}
        **Konu:** (Videonun ana fikri ve kapsamı)

        ---

        ## Sahne 1: Giriş – (Sahne Alt Başlığı)
        **Zaman Aralığı:** 00:00 - 02:00

        ### 1. [Görsel & Edit Yönergesi]
        - **Kamera Açısı & Kadraj:** (Sunucu kadrajı, ışık ve atmosfer)
        - **Görsel, B-Roll ve Kurgu:** (Ekrana gelecek arşiv görüntüleri, simülasyonlar, geçişler)
        - **Metin Grafiği (On-Screen Text):** (Ekranda belirecek başlık, alt yazı / Lower Third ve kavramlar)
        - **Müzik & Ses Efekti:** (Müziğin tonu, tempo ve geçiş ses efektleri)

        ### 2. [Prompter Metni (Aynen Okunacak)]
        > "(1. Paragraf: İzleyiciyi yakalayan güçlü giriş ve temel sorunun ortaya konulması...)"
        >
        > "(2. Paragraf: Kaynaktaki temel kavramların ve çerçevelerin detaylı anlatımı...)"
        >
        > "(3. Paragraf: Bir sonraki sahneye akıcı geçiş...)"

        ---

        ## Sahne 2: (Birinci Gelişme Sahnesi Başlığı)
        **Zaman Aralığı:** 02:00 - 04:30

        ### 1. [Görsel & Edit Yönergesi]
        - **Kamera Açısı & Kadraj:** ...
        - **Görsel, B-Roll ve Kurgu:** ...
        - **Metin Grafiği (On-Screen Text):** ...
        - **Müzik & Ses Efekti:** ...

        ### 2. [Prompter Metni (Aynen Okunacak)]
        > "(1. Paragraf: Bu sahnenin ana kavramının ve kaynaktaki ilkelerin detaylı anlatımı...)"
        >
        > "(2. Paragraf: Kaynaktaki somut örneklerin, delillerin ve karşılaştırmaların açıklanması...)"
        >
        > "(3. Paragraf: Sahne çıkarımı ve sonraki sahneye geçiş...)"

        ---
        (Konunun tüm alt başlıklarını kapsayacak şekilde Sahne 3, Sahne 4, Sahne 5... ve en sonda Kapanış/Sonuç sahnesi aynı şablonla eksiksiz devam etmelidir.)
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
           - Eğer revize edilen doküman "Video Planı" ise: Yalnızca sahne bazlı [Görsel & Edit Yönergesi] ve [Prompter Metni (Aynen Okunacak)] yapısını koru ve geliştir. Kullanıcı talimatında araştırma özetinden bahsedilse bile bu dokümanın içine KESİNLİKLE "Araştırma Özeti" başlığı veya bölümü ekleme!
           - Eğer revize edilen doküman "Araştırma Özeti" ise: Bu doküman yalnızca akademik ve kavramsal araştırma özetidir. Kullanıcı talimatında video planından veya sahnelerden bahsedilse bile bu dokümanın içine KESİNLİKLE "Video Planı", "Sahne 1" veya prompter konuşma metni ekleme!
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
              - KESİNLİKLE 2-3 cümlelik kısa bir geçiş, özet, madde işaretli not veya taslak OLMAYACAK!
              - Her sahne için en az 2-3 dolu paragraf uzunluğunda; kaynaktaki kavramları, delilleri, örnekleri ve hoca/yazar anlatımlarını izleyiciye detaylıca anlatan gerçek uzunlukta bir stüdyo konuşma metni yazılmalıdır.
              - Giriş, gelişme ve kapanış sahneleri baştan sona eksiksiz tamamlanmalıdır.

        C) "DevirNotu":
           - Bir sonraki bölüme aktarılması gereken kilit kavram veya geçiş notu.

        Lütfen yanıtını JSON içine sıkıştırmadan, doğrudan aşağıdaki 3 bölüm başlığı (ayraç) ile ayırarak ve her bölümün altındaki Markdown şablonuna BİREBİR uyarak döndür:

        ===ARASTIRMA_OZETI===
        # {0}
        **Kapsamlı Araştırma Özeti**
        *(Kaynak: İlgili Kaynak Adları – KAYNAK #1, #2 vb.)*

        ---

        ## 1. Giriş
        (Bölümün temel sorusu, kapsamı ve ele alınan ana başlıkların numaralı listesi)

        ---

        ## 2. [Birinci Ana Kavramsal / Akademik Başlık]
        ### 2.1. [Alt Başlık]
        > "[Kaynaktan doğrudan kilit alıntı veya temel ilke cümlesi]" — KAYNAK #1

        (Kaynaktaki kavramların, delillerin ve analizlerin detaylı, yapılandırılmış açıklaması)

        ### 2.2. [İkinci Alt Başlık]
        (Detaylı analiz ve örnekler)

        ---

        ## 3. [İkinci Ana Kavramsal / Akademik Başlık]
        ### 3.1. [Alt Başlık]
        (Detaylı analiz, karşılaştırmalar ve örnekler)

        ---

        ## Sonuç ve Genel Değerlendirme
        (Bölümdeki tüm bulguların bütünsel değerlendirmesi)

        ===VIDEO_PLANI===
        # {0}
        **Video Tipi:** Uzun Formatlı Stüdyo Analizi / Ders
        **Hedef Süre:** ~10-15 Dakika
        **Konu:** (Bölümün ana fikri ve kapsamı)

        ---

        ## Sahne 1: Giriş – [Sahne Alt Başlığı]
        **Zaman Aralığı:** 00:00 - 02:00

        ### 1. [Görsel & Edit Yönergesi]
        - **Kamera Açısı & Kadraj:** (Sunucu kadrajı, ışık ve atmosfer)
        - **Görsel, B-Roll ve Kurgu:** (Ekrana gelecek arşiv görüntüleri, simülasyonlar, geçişler)
        - **Metin Grafiği (On-Screen Text):** (Ekranda belirecek başlık, alt yazı / Lower Third ve kavramlar)
        - **Müzik & Ses Efekti:** (Müziğin tonu, tempo ve geçiş ses efektleri)

        ### 2. [Prompter Metni (Aynen Okunacak)]
        > "(1. Paragraf: İzleyiciyi yakalayan güçlü giriş ve temel sorunun ortaya konulması...)"
        >
        > "(2. Paragraf: Kaynaktaki temel kavramların ve çerçevelerin detaylı anlatımı...)"
        >
        > "(3. Paragraf: Bir sonraki sahneye akıcı geçiş...)"

        ---

        ## Sahne 2: [Gelişme Sahnesi Başlığı]
        **Zaman Aralığı:** 02:00 - 05:00

        ### 1. [Görsel & Edit Yönergesi]
        - **Kamera Açısı & Kadraj:** ...
        - **Görsel, B-Roll ve Kurgu:** ...
        - **Metin Grafiği (On-Screen Text):** ...
        - **Müzik & Ses Efekti:** ...

        ### 2. [Prompter Metni (Aynen Okunacak)]
        > "(En az 2-3 dolu paragraf uzunluğunda, kaynaktaki örnekleri, delilleri ve analizleri sindire sindire anlatan tam stüdyo konuşma metni...)"

        ---
        (Bölümün tüm alt başlıklarını kapsayacak şekilde diğer gelişme sahneleri ve Kapanış/Sonuç sahnesi aynı şablonla eksiksiz devam etmelidir.)

        ===DEVIR_NOTU===
        (Varsa sonraki bölüme devir notu buraya)
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
