namespace VideoOzet.Business.Helpers;

public static class PromptTemplates
{
    public const string SynthesisResearchPrompt = """
        Aşağıdaki video ve doküman kaynaklarını kullanarak "{0}" konusunda kapsamlı, derinlemesine ve eksiksiz bir akademik ve kavramsal araştırma özeti yaz.
        
        Hedef Uzunluk: {1}
        Hedef Kitle: {2}
        
        KAYNAKLAR VE YAZAR DİREKTİFİ:
        {3}
        
        İKİ KATMANLI OTORİTE VE ÇOK KRİTİK KURALLAR:
        1. KATMAN 1 — [YAZAR-TEZİ VE KURGU DİREKTİFİ] (KAVRAMSAL OMURGA):
           - Konu başlığında ("{0}") veya bağlam başındaki `[YAZAR-TEZİ]` direktifinde belirtilen özgün kavramsal çerçeve, felsefi/analitik yaklaşım, pedagojik kurgu ve ton talepleri bu araştırmanın ANA OMURGASIDIR.
           - Yazarın kendi kavramsal perspektifini, sorularını ve analitik yorumlarını kaynaklarda birebir geçmese dahi ASLA dışlama; bu çerçeveyi derinlemesine açıkla ve `[K#]` kaynaklarındaki bulguları bu tezle ilişkilendirerek sentezle.
        2. KATMAN 2 — [K#] KAYNAK KANITLARI (SIFIR UYDURMA OLGUSAL ZEMİN):
           - İleri sürdüğün tüm olgusal veriler, tarihsel vakalar, deneyler ve belirli düşünür/eser görüşleri SADECE verilen `[K#]` kaynak bloklarına dayanmalıdır; cümlenin sonuna mutlaka kanıt kodunu ekle (örn: `[K1]`, `[K3, K7]`).
           - SIFIR UYDURMA İSTATİSTİK KURALI: Verilen `[K#]` bloklarında açıkça yazmayan HİÇBİR yüzde (`%`), sayısal oran, istatistik, anket, deney sayısı, yıl veya hayali rapor EKLEME! Yazar tezini açıklarken dahi kaynakta olmayan uydurma sayı/deney üretme.
           - KAPSAM DÜRÜSTLÜĞÜ: Yazarın kavramsal tezi olarak işlenen ancak `[K#]` kaynaklarında doğrudan ampirik kanıtı bulunmayan alt başlıkları uydurma veri eklemeden kavramsal sentez olarak işle; tamamen kaynak dışı kalan ampirik alt başlıklar varsa raporun sonunda `## Yazar Tezi Olarak İşlenen / Kaynaklarda Doğrudan Ampirik Karşılığı Bulunmayan Başlıklar` altında dürüstçe belirt.
        3. KAYNAK KULLANIM MODLARI KURALLARI (ÇOK ÖNEMLİ):
           - "DOĞRUDAN REFERANS" olarak etiketlenen kaynaklar için: Tanımlar, hoca/yazar atıfları, formülasyonlar ve kilit alıntılar doğrudan kullanılabilir ve aktarılabilir.
           - "DIŞ KAYNAK - KOPYALAMAK KESİNLİKLE YASAKTIR" olarak etiketlenen kaynaklar için:
             * Kaynaktan yalnızca kavramsal mantığı, argümanları, neden-sonuç zincirini ve ana fikri soyutlayarak al.
             * Konuşmacının/yazarın cümlelerini, kelime dizilimlerini, kendine has benzetme/metaforlarını ve anlatım sırasını KESİNLİKLE kopyalama.
             * Bu argüman ve mantıkları tamamen kendi pedagojik akışımız içinde, sıfırdan kendi özgün cümlelerinle ve yeni somut örneklerle yeniden inşa et.
        4. KESİNLİKLE köşeli parantez (`[` veya `]`) içinde şablon yer tutucu ifadeler bırakma (`[K#]` kanıt kodları hariç); tüm başlıkları konunun gerçek içeriğine göre adlandır.
        5. Özeti kesinlikle 2-3 kısa başlıkla yarıda kesme! Kaynaklardaki tüm alt konuları, düşünürlerin neden-sonuç argümanlarını, delilleri, örnekleri ve karşılaştırmaları eksiksiz kapsayacak şekilde EN AZ 4-5 ANA BÖLÜM (## 2., ## 3., ## 4., ## 5.) ve her ana bölüm altında EN AZ 2 ALT BAŞLIK (### 2.1., ### 2.2. vb.) oluştur ve en sonda mutlaka "## Sonuç ve Genel Değerlendirme" bölümüyle tamamla.
        6. Yanıtını aşağıdaki Markdown yapısına birebir uyarak oluştur:

        # {0}
        **Kapsamlı Araştırma Özeti**
        *(Kaynak: İlgili Kaynak Adları – [K1], [K2] vb.)*

        ---

        ## 1. Giriş
        1. Konunun temel sorusu, yazar tezi çerçevesi, önemi ve tanımlayıcı kapsamı.
        2. Araştırmanın kapsamı ve kaynaklardaki metodolojik yaklaşım.
        3. Ele alınacak ana başlıklar:
           - 2. (Birinci Ana Bölümün Gerçek Başlığı - köşeli parantez kullanma)
           - 3. (İkinci Ana Bölümün Gerçek Başlığı)
           - 4. (Üçüncü Ana Bölümün Gerçek Başlığı)
           - 5. (Dördüncü Ana Bölümün Gerçek Başlığı)

        ---

        ## 2. (Birinci Ana Kavramsal / Akademik Başlık)
        ### 2.1. (Birinci Alt Başlık)
        > "(Kaynaktan doğrudan kilit alıntı veya temel ilke cümlesi)" — [K1]

        - **(Konuya Özgü 1. Boyut / Kavram Başlığı):** (Kaynaktaki kavramın ve düşünür argümanlarının neden-sonuç zinciriyle detaylı açıklaması; DİKKAT: bu maddelerin başında KESİNLİKLE `>` işareti kullanma, alıntı kutusunun dışında normal madde olarak yaz)
        - **(Konuya Özgü 2. Boyut / Metodolojik Analiz):** (Kaynaktaki delillerin, örneklerin ve analizlerin detaylı açıklaması)
        - **(Konuya Özgü 3. Boyut / Yazar Tezi Bağlantısı ve Uygulama):** (Kaynaktaki somut örnekler, atıflar ve ana tezle bütünleşen çıkarımlar)

        ### 2.2. (İkinci Alt Başlık)
        > "(Kaynaktan doğrudan ilgili ikinci kilit alıntı)" — [K2]

        - **(Konuya Özgü 1. İlke / Yapı Başlığı):** (Detaylı analiz ve açıklama)
        - **(Konuya Özgü 2. Uygulama / Örnek Başlığı):** (Kaynaktaki somut örnekler ve çıkarımlar)

        ---

        ## 3. (İkinci Ana Kavramsal / Akademik Başlık)
        ### 3.1. (Alt Başlık)
        - **(Konuya Özgü 1. Analitik Nokta):** (Detaylı analiz ve karşılaştırmalar)
        - **(Konuya Özgü 2. Analitik Nokta):** (Kaynaktaki deliller ve değerlendirmeler)

        ### 3.2. (İkinci Alt Başlık)
        - **(Konuya Özgü 1. Analitik Nokta):** (Detaylı analiz)
        - **(Konuya Özgü 2. Analitik Nokta):** (Somut örnekler ve sonuçlar)

        ---

        ## 4. (Üçüncü Ana Kavramsal / Akademik Başlık)
        ### 4.1. (Alt Başlık)
        - **(Konuya Özgü 1. Analitik Nokta):** (Detaylı analiz)
        - **(Konuya Özgü 2. Analitik Nokta):** (Somut örnekler ve sonuçlar)

        ### 4.2. (İkinci Alt Başlık)
        - **(Konuya Özgü 1. Analitik Nokta):** (Detaylı analiz)
        - **(Konuya Özgü 2. Analitik Nokta):** (Somut örnekler ve sonuçlar)

        ---

        ## 5. Sonuç ve Genel Değerlendirme
        1. **(Temel Çıkarım 1):** ...
        2. **(Temel Çıkarım 2):** ...
        3. **(Genel Sentez):** (Tüm bulguların ve yazar tezinin bütünsel değerlendirmesi)
        """;

    public const string SynthesisVideoPlanGroundedPrompt = """
        Aşağıdaki DOĞRULANMIŞ ARAŞTIRMA ÖZETİ ve [K#] KAYNAK BAĞLAMINI temel alarak "{0}" konusunda stüdyoda ve kurguda doğrudan kullanılacak profesyonel, baştan sona eksiksiz bir Çekim, Kurgu ve Prompter Video Planı oluştur.
        Hedef Süre / Uzunluk: {1}

        İKİ KATMANLI OTORİTE VE BAĞLILIK KURALLARI (İHLAL EDİLEMEZ):
        1. [YAZAR-TEZİ] OMURGASI: Konu ve yazar direktifindeki kavramsal yaklaşımı, felsefi çerçeveyi ve pedagojik kurguyu senaryonun merkezinde tut; bu kavramsal yorumları stüdyo dilinde zenginleştirerek anlat.
        2. [K#] OLGUSAL DOĞRULUK: Prompter metinlerinde öne sürülen tüm olgular, vakalar, düşünürlere ait görüşler ve sayısal ifadeler AŞAĞIDAKİ DOĞRULANMIŞ ARAŞTIRMA ÖZETİ ve [K#] KAYNAKLARI ile %100 uyumlu olmalıdır.
        3. Araştırma özetinde veya kaynaklarda geçmeyen HİÇBİR yüzde ("%"), oran, istatistik, anket veya hayali deney ekleme!
        4. [Mod: DOĞRUDAN REFERANS] olarak işaretli kaynaklardaki kavramları, örnekleri ve alıntıları prompter metnine doğrudan ve doğal üslupla işleyebilirsin; [Mod: DIŞ KAYNAK] parçalarından sadece kavramsal argüman ve neden-sonuç mantığını alıp tamamen özgün cümlelerle yeniden kurgula.
        5. Düşünürlerin veya kuramların yalnızca adını anıp 1-2 cümleyle geçiştirme; her sahnede en az 3-5 dolu paragraf prompter metni yazarak argümanları neden-sonuç zinciri ve örneklerle sindire sindire anlat.

        DOĞRULANMIŞ ARAŞTIRMA ÖZETİ:
        {3}

        REFERANS KAYNAK BAĞLAMI ([K#]):
        {2}

        ÖNEMLİ FORMAT VE BÜTÜNLÜK KURALLARI:
        1. Kesinlikle düşünme süreci ("<think>", "Thinking Process:" vb.) veya başlangıç sohbeti YAZMA.
        2. Dokümanın EN BAŞINA doğrudan "# {0} - Video Çekim ve Kurgu Planı" ana başlığı ile başla.
        3. Her sahnede mutlaka `## Sahne X: Başlık`, `**Zaman Aralığı:**`, `### 1. [Görsel & Edit Yönergesi]` (Kamera, B-Roll, Metin Grafiği, Müzik/Ses) ve `### 2. [Prompter Metni (Aynen Okunacak)]` (`> "..."` blokları halinde en az 3-5 dolu paragraf) yer alsın; açılıştan Kapanış / Özet sahnesine kadar kesintisiz tamamla.
        """;

    public const string SynthesisVideoPlanPrompt = """
        Aşağıdaki video ve doküman kaynaklarını kullanarak "{0}" konusunda stüdyoda ve kurguda doğrudan kullanılacak profesyonel, baştan sona eksiksiz bir Çekim, Kurgu ve Prompter Video Planı oluştur.
        Hedef Süre / Uzunluk: {1}
        
        KAYNAKLAR:
        {2}

        ÇOK KRİTİK VE KESİN KURALLAR:
        1. İKİ KATMANLI OTORİTE: Konudaki `[YAZAR-TEZİ]` kavramsal çerçevesini ana anlatı omurgası yap; ancak olgusal iddialarda, düşünür görüşlerinde, deneylerde ve sayılarda SADECE verilen `[K#]` kaynaklarındaki bilgileri kullan. Kaynaklarda yer almayan yüzde (`%`), istatistik veya sahte deney uydurma.
        2. KAYNAK KULLANIM MODLARI KURALLARI (ÇOK ÖNEMLİ):
           - "DOĞRUDAN REFERANS" olarak etiketlenen kaynaklar için: Tanımlar, hoca/yazar atıfları, formülasyonlar ve kilit örnekler doğrudan ve detaylıca aktarılabilir.
           - "DIŞ KAYNAK - KOPYALAMAK KESİNLİKLE YASAKTIR" olarak etiketlenen kaynaklar için:
             * Kaynaktan yalnızca kavramsal mantığı, argümanları, neden-sonuç zincirini ve ana fikri soyutlayarak al.
             * Konuşmacının/yazarın cümlelerini, kelime dizilimlerini, kendine has benzetme/metaforlarını ve sahne/anlatım sırasını KESİNLİKLE kopyalama.
             * Bu argüman ve mantıkları tamamen kendi pedagojik akışımız içinde, prompter metninde sıfırdan kendi özgün cümlelerinle ve yeni somut örneklerle yeniden inşa et.
        3. Her sahne için [Prompter Metni (Aynen Okunacak)] kısmı KESİNLİKLE 2-3 cümlelik kısa bir özet OLMAYACAK; sunucunun kameraya karşı kelimesi kelimesine okuyacağı EN AZ 3-5 DOLU PARAGRAF (dakika başı ~140 kelime konuşma temposuna uygun) akıcı stüdyo konuşma metni olacaktır. Her paragrafın arasına mutlaka boş bir `>` satırı koy ki paragraflar birbirine yapışmasın.
        4. Video planını KESİNLİKLE yarıda bırakma! Konunun tüm alt başlıklarını kapsayacak şekilde EN AZ 6 SAHNE (Sahne 1: Giriş, Sahne 2-5+: Gelişme Sahneleri ve son sahne olarak Kapanış / Sonuç Sahnesi) oluştur.
        5. Yanıtını KESİNLİKLE aşağıdaki Markdown şablonuna birebir uyarak (JSON sarmalaması olmadan) döndür:

        # {0}
        **Video Tipi:** Uzun Formatlı Stüdyo Analizi / Ders
        **Hedef Süre:** {1}
        **Konu:** (Videonun ana fikri ve kapsamı)

        ---

        ## Sahne 1: Giriş – (Sahne Alt Başlığı)
        **Zaman Aralığı:** 00:00 - 02:30

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
        > "(3. Paragraf: Örnek/vaka açılımı ve bir sonraki sahneye akıcı geçiş...)"

        ---

        ## Sahne 2: (Birinci Gelişme Sahnesi Başlığı)
        **Zaman Aralığı:** 02:30 - 05:30

        ### 1. [Görsel & Edit Yönergesi]
        - **Kamera Açısı & Kadraj:** ...
        - **Görsel, B-Roll ve Kurgu:** ...
        - **Metin Grafiği (On-Screen Text):** ...
        - **Müzik & Ses Efekti:** ...

        ### 2. [Prompter Metni (Aynen Okunacak)]
        > "(1. Paragraf: Bu sahnenin ana kavramının ve düşünür argümanının neden-sonuç zinciriyle detaylı anlatımı...)"
        >
        > "(2. Paragraf: Kaynaktaki somut örneklerin, delillerin ve karşılaştırmaların açıklanması...)"
        >
        > "(3. Paragraf: Yazar teziyle bağlantı ve sonraki sahneye geçiş...)"

        ---
        (Konunun tüm alt başlıklarını kapsayacak şekilde Sahne 3, Sahne 4, Sahne 5... ve en sonda Kapanış/Sonuç sahnesi aynı şablonla eksiksiz devam etmelidir.)
        """;

    public const string ClaimExtractionPrompt = """
        Aşağıdaki metindeki en önemli doğrulanabilir olgusal iddiaları liste halinde çıkar. Her iddia bağımsız ve tek bir cümleden oluşmalıdır.
        
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
        Sen VeriScore metodolojisini uygulayan iki katmanlı bir akademik ve stüdyo içerik Kalite Kontrol (QC) denetçisisin.
        Aşağıdaki DENETLENECEK İÇERİK (varsa [YAZAR-TEZİ] çerçevesi, ARAŞTIRMA ÖZETİ ve VİDEO PLANI PROMPTER METNİ) metnini dikkatle incele ve en belirleyici {0} adet iddiayı değerlendir.

        VERİSCORE İKİ KATMANLI İDDİA SINIFLANDIRMA KURALI (ÇOK KRİTİK — İHLAL EDİLEMEZ):
        Her ifadeyi doğrulamadan önce mutlaka şu 2 türden birine ayır (`"iddiaTuru"`):
        1. `"olgusal_kaynak_iddiasi"`:
           - Yüzdeler (`%`), sayısal oranlar, istatistikler, yıllar, deney/anket sonuçları, tarihsel vakalar ve belirli bir düşünür/yazar/eser adına yapılan somut atıflar (örn: "Spinoza şunu savunur", "Libet deneyinde...", "Kullanıcıların %70'i...").
           - Bu iddialar MUTLAKA verilen `[K#]` KAYNAKLAR içinde doğrulanmalıdır! Kaynakta açıkça yazmayan bir yüzde (`%`), sayısal oran, hayali deney/anket veya yanlış düşünür atfı KESİNLİKLE `"desteklenmedi"` olarak işaretlenmelidir!
        2. `"yazar_tezi_yorumu"`:
           - Kullanıcının `[YAZAR-TEZİ]` katmanından gelen özgün kavramsal çerçeve, felsefi/analitik yorumlar, pedagojik benzetmeler, değer yargıları, geçiş soruları ve sentez çıkarımları.
           - Bu ifadeler `[K#]` kaynak aramasından MUAFTIR! İçinde kaynak dışı uydurma istatistik (`%`), hayali deney veya sahte düşünür atfı barındırmadığı sürece YAZAR TEZİ YORUMLARI ASLA `"desteklenmedi"` SAYILMAZ; `"durum": "desteklendi"` ve `"kanitKodu": "[YAZAR-TEZİ]"` olarak işaretlenir!

        DENETLENECEK İÇERİK:
        {1}
        
        KAYNAKLAR ([K#]):
        {2}
        
        Lütfen değerlendirme sonucunu SADECE geçerli bir JSON dizisi (array) formatında ver. Markdown kod bloğu tırnakları (```json gibi) veya fazladan açıklama yazmadan doğrudan saf JSON döndür:
        [
          {{
            "iddia": "Metinden çıkarılan cümle veya iddia",
            "iddiaTuru": "olgusal_kaynak_iddiasi",
            "durum": "desteklendi",
            "kanitKodu": "[K1] veya [YAZAR-TEZİ] veya -",
            "kaynakAlani": "ArastirmaOzeti veya VideoPlani",
            "aciklama": "İddianın [K#] kaynaklarıyla veya [YAZAR-TEZİ] çerçevesiyle uyumuna dair net gerekçe"
          }}
        ]
        
        Not:
        - `"iddiaTuru"` değeri SADECE `"olgusal_kaynak_iddiasi"` veya `"yazar_tezi_yorumu"` olabilir.
        - `"durum"` değeri SADECE `"desteklendi"`, `"desteklenmedi"` veya `"belirsiz"` olabilir.
        """;

    public const string RevisionPrompt = """
        Sen uzman bir eğitim ve video içerik mimarısın.
        Aşağıda mevcut bir "{0}" dokümanı ve bu doküman üzerinde yapılmasını istenen revizyon talimatı verilmiştir.
        
        DOKÜMAN KONUSU VE [YAZAR-TEZİ] ÇERÇEVESİ:
        {3}
        
        MEVCUT DOKÜMAN:
        {1}
        
        KULLANICININ / SİSTEMİN REVİZE TALİMATI:
        {2}
        
        KAYNAK BAĞLAMI ([K#] Referansları):
        {4}
        
        ÇOK KRİTİK VE KESİN KURALLAR:
        1. HEDEF ODAKLI DÜZENLEME: Yalnızca revize talimatında açıkça belirtilen kısımları güncelle.
        2. [YAZAR-TEZİ] KORUMA KURALI: Dokümandaki `[YAZAR-TEZİ]` kaynaklı kavramsal çerçeveyi, felsefi/analitik yorumları ve pedagojik sentezleri (kullanıcı açıkça silinmesini istemedikçe) KESİNLİKLE SİLME! Eğer bu bir Auto-Refine (kanıt öz-düzeltmesi) çağrısıysa yalnızca kaynakta olmayan uydurma sayıları (`%`), hayali deney/anketleri ve yanlış düşünür atıflarını temizle; yazarın kavramsal yorumlarını ve sahne derinliğini aynen koru.
        3. DOKÜMAN TÜRÜNE VE AMACINA KESİNLİKLE SADIK KAL:
           - Eğer revize edilen doküman "Video Planı" ise: Yalnızca sahne bazlı `### 1. [Görsel & Edit Yönergesi]` ve `### 2. [Prompter Metni (Aynen Okunacak)]` yapısını koru ve geliştir. Bu dokümanın içine KESİNLİKLE "Araştırma Özeti" başlığı veya bölümü ekleme!
           - Eğer revize edilen doküman "Araştırma Özeti" ise: Yalnızca akademik ve kavramsal araştırma özeti yapısını koru. Bu dokümanın içine KESİNLİKLE "Video Planı", "Sahne 1" veya prompter konuşma metni ekleme!
        4. DEĞİNİLMEYEN KISIMLARA KESİNLİKLE DOKUNMA: Değiştirilmesi istenmeyen hiçbir bölümün başlıklarını, maddelerini, sahnelerini, kelime hacmini veya metnini BOZMA, KISALTMA veya SİLME.
        5. EKSİKSİZ ÇIKTI: Sadece değişen parçayı değil; değişmeyen kısımları aynen muhafaza ederek revize edilmiş TÜM DOKÜMANI baştan sona eksiksiz Markdown formatında döndür.
        """;

    public const string ReQcVerificationPrompt = """
        Sen VeriScore metodolojisini uygulayan uzman bir akademik ve stüdyo içerik Kalite Kontrol (QC) denetçisisin.
        Aşağıdaki GÜNCEL İÇERİK metnini, verilen `[K#]` REFERANS KAYNAKLARI ve varsa BİR ÖNCEKİ KALİTE KONTROL RAPORUNU incele.

        GÜNCEL İÇERİK (ve varsa [YAZAR-TEZİ] çerçevesi):
        {1}

        REFERANS KAYNAKLAR ([K#]):
        {2}

        ÖNCEKİ KALİTE KONTROL RAPORU:
        {3}

        VERİSCORE İKİ KATMANLI DENETİM ADIMLARI:
        1. ÖNCEKİ HATALARIN KONTROLÜ: Önceki raporda `"desteklenmedi"` olarak işaretlenmiş `olgusal_kaynak_iddiasi` (uydurma oran/yüzde, hayali deney, yanlış düşünür atfı) varsa güncel metinde temizlenip temizlenmediğini kontrol et.
        2. İDDİA SINIFLANDIRMASI (`"iddiaTuru"`):
           - `"olgusal_kaynak_iddiasi"`: Yüzdeler (`%`), sayılar, deney/anket sonuçları ve belirli düşünür/eser atıfları -> Mutlaka `[K#]` kaynaklarında bulunmalıdır; yoksa `"desteklenmedi"` işaretle.
           - `"yazar_tezi_yorumu"`: `[YAZAR-TEZİ]` kapsamındaki kavramsal yorumlar, felsefi çerçeve, pedagojik benzetmeler ve sentezler -> Kaynak aramasından MUAFTIR; uydurma istatistik/sahte deney içermediği sürece ASLA `"desteklenmedi"` sayılmaz, `"desteklendi"` (`"kanitKodu": "[YAZAR-TEZİ]"`) olarak işaretlenir!
        3. GÜNCEL İÇERİK İDDİALARI: En belirleyici toplam {0} adet iddiayı incele ve net gerekçeyle raporla.

        Lütfen değerlendirme sonucunu SADECE geçerli bir JSON dizisi (array) formatında ver. Markdown kod bloğu tırnakları (```json gibi) veya fazladan açıklama yazmadan doğrudan saf JSON döndür:
        [
          {{
            "iddia": "Metinden çıkarılan cümle veya iddia",
            "iddiaTuru": "olgusal_kaynak_iddiasi",
            "durum": "desteklendi",
            "kanitKodu": "[K1] veya [YAZAR-TEZİ] veya -",
            "kaynakAlani": "ArastirmaOzeti veya VideoPlani",
            "aciklama": "İddianın [K#] kaynağıyla veya [YAZAR-TEZİ] çerçevesiyle uyumuna ve varsa önceki hatanın düzeltilme durumuna dair net açıklama"
          }}
        ]
        """;

    public const string TopicAnalysisPrompt = """
        Sen Co-STORM ve NotebookLM mimarisiyle çalışan kıdemli bir eğitim serisi baş editörüsün.
        Aşağıda verilen TÜM EĞİTİM KAYNAK KARTLARINI / METİNLERİNİ ve kullanıcının `[YAZAR-TEZİ VE KURGU DİREKTİFİ]` bilgisini birlikte analiz ederek kapsamlı bir 'Konu ve Kaynak Kapsam Analizi' oluştur.

        [YAZAR-TEZİ VE KURGU DİREKTİFİ]:
        {0}

        EĞİTİMDEKİ TÜM KAYNAKLAR VE KONU HARİTASI:
        {1}

        İKİ KATMANLI OTORİTE VE ÇOK KRİTİK KURALLAR:
        1. YAZAR TEZİNİ ASLA ÇÖPE ATMA: Kullanıcının `[YAZAR-TEZİ VE KURGU DİREKTİFİ]` içinde belirttiği özgün tez, felsefi/kavramsal çerçeve, anlatım kurgusu ve perspektif bu serinin MERKEZİ OMURGASIDIR. Kaynaklarda birebir geçmese dahi yazarın bu kurgusunu ASLA reddetme veya ana fikirden dışlama!
        2. %100 KAYNAK GÖRÜNÜRLÜĞÜ: Verilen kaynak kartlarındaki tüm düşünürleri, kuramları, vakaları ve argümanları dikkatle tara; kaynakları yazarın tezini besleyecek, karşılaştıracak ve derinleştirecek şekilde haritala. Kaynakta olmayan sahte düşünür veya uydurma istatistik ekleme.
        3. `KaynaktaOlmayanlar` ALANININ DOĞRU KULLANIMI: Kullanıcının `[YAZAR-TEZİ]` içinde işlenmesini istediği ancak eğitim kaynaklarında doğrudan ampirik/metinsel karşılığı bulunmayan özgün başlıkları çöpe atmak yerine `KaynaktaOlmayanlar` dizisinde `"Yazar Tezi Olarak İşlenecek: <Başlık ve Yorum Çerçevesi>"` formatıyla açıkça işaretle ve koru! Böylece seri planlayıcı bu başlıkları yazar tezi katmanı olarak bölümlere dahil edecektir.

        Lütfen SADECE geçerli bir JSON objesi döndür (```json sarmalaması olmadan saf JSON):
        {{
            "AnaFikir": "Yazarın özgün tezini ve tüm kaynak havuzundaki temel argümanları birleştiren 3-5 cümlelik derinlikli üst anlatı omurgası",
            "BeklenenKonular": [ "Kaynaklardan ve yazar tezinden çıkarılan somut konu/düşünür başlığı 1", "Somut konu/düşünür başlığı 2" ],
            "KonuHaritasi": [ {{ "Konu": "Somut Konu / Düşünür Başlığı", "Aciklama": "Hangi kaynaklarda hangi argümanlarla işlendiği ve yazar teziyle bağlantısı" }} ],
            "KaynaktaOlmayanlar": [ "Yazar Tezi Olarak İşlenecek: (Yazarın istediği ancak kaynaklarda doğrudan geçmeyen kavramsal başlıklar)" ],
            "OnerilenVideoSayisi": 3,
            "OneriSuresiDk": 15,
            "OneriGerekcesi": "Kaynak hacmi, düşünür çeşitliliği ve yazar kurgusuna göre neden bu bölüm sayısı ve sürenin önerildiği"
        }}
        """;

    public const string SeriesPlanPrompt = """
        Sen Co-STORM mimarisiyle çalışan uzman bir Çoklu Video Serisi Mimarısın.
        Aşağıdaki `[YAZAR-TEZİ]`, Konu Analizi, Eğitim Kaynak Kartları ve Kullanıcı Kısıtlarını dikkate alarak zengin, derinlikli ve doğrudan üretime hazır bir çoklu video serisi planı oluştur.

        [YAZAR-TEZİ VE SERİ KONUSU]:
        {0}

        Hedef Kitle: {1}
        
        KONU ANALİZİ:
        {2}
        
        EĞİTİM KAYNAKLARI (KaynakId, Başlık, Konular ve İçerik Özeti):
        {3}
        
        KULLANICI KISITLARI VE REVİZYON DİREKTİFLERİ:
        {4}

        ÇOK KRİTİK VE İHLAL EDİLEMEZ PLANLAMA KURALLARI:
        1. JENERİK BAŞLIK YASAĞI (KESİN KURAL!): `"CalismaBasligi"` alanında `"Bölüm 1"`, `"Bölüm 2"`, `"Video 1"`, `"Ders 1"`, `"Giriş Bölümü"` gibi jenerik ve sığ isimler kullanmak KESİNLİKLE YASAKTIR! Her bölümün içeriğindeki çatışmayı, düşünürleri ve ana tezi yansıtan çarpıcı, özgün bir editoryal başlığı olmalıdır (Örn: `"Özgür İrade Yanılsaması mı? Spinoza, Gazzâlî ve Modern Nörobiyolojinin Hesaplaşması"`).
        2. KULLANICI KISITLARINA %100 UYUM: Kullanıcı `VideoSayisi`, `VarsayilanSureDk`, `VideoSureleri`, `ZorunluKonular`, `HaricKonular` veya özel revizyon talimatı belirtmişse tek bir sapma olmadan %100 UYACAKSIN!
        3. İKİ KATMANLI OTORİTE (`[YAZAR-TEZİ]` + `[K#]`): Kullanıcının tezini ve kavramsal kurgusunu bölümlerin ana omurgasına yerleştir; eğitimdeki kaynak kartlarını (`KaynakId` UUID değerleriyle) ilgili bölümlerin `"AtananKaynakIdleri"` listesine eksiksiz dağıt ki hiçbir alakalı kaynak dışarıda kalmasın.
        4. ZENGİN BÖLÜM İÇERİĞİ ZORUNLULUĞU:
           - `"Kanca"`: İlk 30 saniyede izleyiciyi ekrana kilitleyecek çarpıcı açılış sorusu, paradoks veya vaka vaadi (1-2 güçlü cümle).
           - `"AnaFikir"`: En az 3-4 cümlelik derinlikli bölüm tezi (hangi temel soruyu çözdüğü, hangi düşünürleri karşılaştırdığı ve yazar tezini nasıl ilerlettiği).
           - `"Konular"`: Tek kelimelik etiketler DEĞİL; açıklayıcı maddeler (Örn: `"Robert Sapolsky'nin biyolojik belirlenimcilik argümanı ve prefrontal korteks bulguları"`).
           - `"SahneIskeleti"`: Bölümün `HedefSureDk` süresini dolduran **4 ila 8 adet alt sahne** (`SahneNo`, `Baslik`, `TahminiSureDk`, `AciklamaVeArgumanlar`, `AtananKaynakKodlari`). Alt sahnelerin `TahminiSureDk` toplamı bölümün `HedefSureDk` değerine eşit olmalıdır!
           - `"AtananKaynakIdleri"`: Kaynak listesinde verilen geçerli `KaynakId` (GUID) değerlerinden bu bölümde kullanılacak tüm kaynakların ID dizisi.

        Lütfen SADECE geçerli bir JSON objesi döndür (```json sarmalaması olmadan saf JSON):
        {{
            "VideoSayisi": 3,
            "VarsayilanVideoSuresiDk": 15,
            "OneridenFarkli": false,
            "SeriHaritasi": [
                {{
                    "BolumNo": 1,
                    "CalismaBasligi": "İçeriği Yansıtan Çarpıcı ve Özgün Bölüm Başlığı (Asla Bölüm 1 Yazma!)",
                    "Kanca": "İzleyiciyi ilk 30 saniyede yakalayan paradoks veya çarpıcı açılış sorusu",
                    "HedefSureDk": 15,
                    "AnaFikir": "Bu bölümün temel tezini, tartışacağı kuramcıları, neden-sonuç omurgasını ve yazar teziyle bağını anlatan en az 3-4 cümlelik kapsamlı açıklama.",
                    "Konular": [
                        "1. Alt Konu ve İşlenecek Düşünür/Argüman Açıklaması",
                        "2. Alt Konu ve Somut Vaka/Karşılaştırma Açıklaması",
                        "3. Alt Konu ve Yazar Tezi Sentezi"
                    ],
                    "SahneIskeleti": [
                        {{
                            "SahneNo": 1,
                            "Baslik": "Açılış Kancası ve Temel Paradoksun Kurulması",
                            "TahminiSureDk": 3,
                            "AciklamaVeArgumanlar": "Bu sahnede işlenecek açılış vakası, temel soru ve izleyiciye verilecek vaat.",
                            "AtananKaynakKodlari": ["Kaynak Başlığı 1"]
                        }},
                        {{
                            "SahneNo": 2,
                            "Baslik": "Birinci Kuramsal Cephe ve Derin Analiz",
                            "TahminiSureDk": 4,
                            "AciklamaVeArgumanlar": "İlgili düşünürlerin argümanları, neden-sonuç analizi ve örnekler.",
                            "AtananKaynakKodlari": ["Kaynak Başlığı 1", "Kaynak Başlığı 2"]
                        }},
                        {{
                            "SahneNo": 3,
                            "Baslik": "Karşıt Argümanlar ve Ampirik Bulgular",
                            "TahminiSureDk": 4,
                            "AciklamaVeArgumanlar": "Deneysel/metinsel kanıtların karşılaştırılması.",
                            "AtananKaynakKodlari": ["Kaynak Başlığı 2"]
                        }},
                        {{
                            "SahneNo": 4,
                            "Baslik": "Yazar Tezi Sentezi ve Sonraki Bölüme Köprü",
                            "TahminiSureDk": 4,
                            "AciklamaVeArgumanlar": "Yazarın özgün çerçevesiyle sentez ve kapanış sorusu.",
                            "AtananKaynakKodlari": ["[YAZAR-TEZİ]"]
                        }}
                    ],
                    "AtananKaynakIdleri": []
                }}
            ],
            "DisaridaBirakilanlar": [ "Kullanıcı kısıtı veya kapsam dışı kalması nedeniyle hiçbir bölüme atanmayan kaynak/konuların gerekçeli listesi" ]
        }}
        """;

    public const string SeriesPlanRichPrompt = """
        Sen Co-STORM mimarisiyle çalışan kıdemli bir Çoklu Video Serisi Mimarısın.
        Görevin; `[YAZAR-TEZİ VE KURGU DİREKTİFİ]`, Konu Analizi, Eğitimdeki TÜM Kaynak Kartları, Kullanıcı Kısıtları ve (varsa) Önceki Taslak Planı üzerinde fark-bazlı (Diff-Aware) çalışarak eksiksiz bir seri haritası üretmektir.

        [YAZAR-TEZİ VE SERİ ODAĞI]:
        {0}

        HEDEF KİTLE: {1}
        EK TON VE ÜSLUP DİREKTİFİ: {2}

        KONU ANALİZİ ÖZETİ:
        {3}

        EĞİTİMDEKİ TÜM YAPILANDIRILMIŞ KAYNAK KARTLARI (KaynakId GUID'lerini AtananKaynakIdleri için kullan!):
        {4}

        KULLANICI KISITLARI VE REVİZYON TALİMATI (MUTLAK ZORUNLU!):
        {5}

        MEVCUT / ÖNCEKİ TASLAK PLAN (Eğer doluysa, kullanıcının revizyon talimatını bu taslak üzerinde fark-bazlı uygula; değiştirilmesi istenmeyen başarılı kurguları koru!):
        {6}

        ÇOK KRİTİK VE İHLAL EDİLEMEZ KURALLAR:
        1. JENERİK BAŞLIK KESİNLİKLE YASAKTIR: `"CalismaBasligi"` alanına `"Bölüm 1"`, `"Bölüm 2"`, `"Video 1"`, `"Part 1"` gibi jenerik başlıklar YAZAMAZSIN! Her bölümün çarpıcı, içerik odaklı, özgün bir çalışma başlığı olmalıdır.
        2. KISIT ZORUNLULUĞU: Kullanıcı kısıtlarında belirtilen bölüm sayısına (`VideoSayisi`), bölüm sürelerine (`VarsayilanSureDk` / `VideoSureleri`), zorunlu konulara (`ZorunluKonular`) ve hariç tutulan konulara (`HaricKonular`) %100 UYACAKSIN.
        3. FARK-BAZLI (DIFF-AWARE) GÜNCELLEME: Önceki taslak plan verilmişse ve kullanıcı örneğin `"1. videoyu daha ayrıntılı yap, 2 video olsun"` demişse, önceki taslağın 1. videosunu görerek derinleştir ve toplam yapıyı tam istenen kısıta uyarla.
        4. %100 KAYNAK EŞLEŞTİRME: Eğitimdeki alakalı her kaynağın `KaynakId` (GUID) değerini en az bir bölümün `"AtananKaynakIdleri"` dizisine ata. Yalnızca gerçekten kapsam dışı/hariç bırakılan kaynakları gerekçesiyle `"DisaridaBirakilanlar"` dizisine yaz.
        5. ZENGİN ALANLAR: Her bölümde çarpıcı `"Kanca"`, doğru `"HedefSureDk"`, en az 3-4 cümlelik derin `"AnaFikir"`, açıklamalı `"Konular"` maddeleri, toplam süresi `HedefSureDk`'ya eşit **4 ila 8 maddelik `"SahneIskeleti"`** ve geçerli GUID'lerden oluşan `"AtananKaynakIdleri"` bulunmalıdır.

        Lütfen SADECE geçerli bir JSON objesi döndür (```json kod bloğu veya açıklama ekleme):
        {{
            "VideoSayisi": 2,
            "VarsayilanVideoSuresiDk": 20,
            "OneridenFarkli": true,
            "SeriHaritasi": [
                {{
                    "BolumNo": 1,
                    "CalismaBasligi": "Çarpıcı ve İçerik Odaklı Özgün Bölüm Başlığı",
                    "Kanca": "İlk 30 saniyede izleyiciyi yakalayan çarpıcı soru veya vaka",
                    "HedefSureDk": 20,
                    "AnaFikir": "En az 3-4 cümlelik kapsamlı bölüm tezi, tartışılan düşünürler, neden-sonuç zinciri ve yazar teziyle bağlantısı.",
                    "Konular": [
                        "Açıklamalı alt konu ve işlenecek düşünür/argüman 1",
                        "Açıklamalı alt konu ve somut vaka/örnek 2"
                    ],
                    "SahneIskeleti": [
                        {{
                            "SahneNo": 1,
                            "Baslik": "Sahne Başlığı",
                            "TahminiSureDk": 4,
                            "AciklamaVeArgumanlar": "Bu sahnede işlenecek argümanlar, düşünürler ve yazar tezi vurgusu",
                            "AtananKaynakKodlari": ["Kaynak Başlığı"]
                        }}
                    ],
                    "AtananKaynakIdleri": ["00000000-0000-0000-0000-000000000000"]
                }}
            ],
            "DisaridaBirakilanlar": []
        }}
        """;

    public const string EpisodeStage1ResearchAndOutlinePrompt = """
        Sen AgentWrite mimarisiyle çalışan kıdemli bir akademik araştırmacı ve belgesel/stüdyo senaryo baş mimarısın.
        Çoklu video serisinin **{0}. Bölümü (Toplam {1} Bölüm): "{2}"** için **Aşama 1: Derin Araştırma Özeti ve Zaman/Kelime Bütçeli Sahne Planı** üreteceksin.

        BÖLÜM KİMLİĞİ VE HEDEF BÜTÇE:
        - Bölüm Başlığı: {2}
        - Açılış Kancası (Hook): {3}
        - Bölüm Ana Tezi: {4}
        - Kesin Hedef Süre: **{5} Dakika**
        - Hedef Sahne Sayısı: **{6} Sahne** (`sum(TahminiSureDk) == {5}` dakika olmak ZORUNDADIR!)
        - Toplam Prompter Kelime Bütçesi: **~{7} kelime** (dakika başı ~140-150 kelime stüdyo konuşma temposu)
        - Hedef Kitle: {8}

        BÖLÜM ALT KONULARI VE PLANLANAN SAHNE İSKELETİ:
        - Alt Konular: {9}
        - Planlanan Sahne İskeleti: {10}

        [YAZAR-TEZİ VE KURGU DİREKTİFİ] (KATMAN 1 — OTORİTER KAVRAMSAL OMURGA):
        {11}

        [ÖNCEKİ BÖLÜM DEVİR DEFTERİ] (Tekrara düşme, açık bırakılan soruları devral ve organik köprü kur!):
        {12}

        [K#] REFERANS KAYNAK BAĞLAMI (KATMAN 2 — OLGUSAL VE METİNSEL KANIT ZEMİNİ):
        {13}

        ÇOK KRİTİK KURALLAR:
        1. İKİ KATMANLI OTORİTE: `[YAZAR-TEZİ]` içinde belirtilen özgün yaklaşımı, kavramsal çerçeveyi, üslubu ve onay talimatlarını hem Araştırma Özetinin hem Sahne Planının merkezine yerleştir. Ancak tüm olgusal kanıtlar, deneyler, yüzdeler (`%`) ve düşünür/eser görüşleri SADECE `[K#]` kaynaklarına dayanmalıdır (satır içi `[K1]`, `[K4]` atıflarıyla). Kaynakta olmayan HİÇBİR sayı/oran/deney uydurma!
        2. KAYNAK KULLANIM MODU: `[Mod: DOĞRUDAN REFERANS]` kaynaklardan doğrudan kavram/alıntı aktarabilirsin. `[Mod: DIŞ KAYNAK - KOPYALAMAK KESİNLİKLE YASAKTIR]` kaynaklardan yalnızca soyut argüman ve neden-sonuç mantığını al; cümle yapısını ve özgün metaforlarını asla kopyalama.
        3. DERİN ARAŞTIRMA ÖZETİ: Bölümdeki her düşünürün ve kavramın argümanını ismen geçiştirmeden; hangi soruya cevap verdiğini, neden-sonuç zincirini, somut vaka/örneklerini ve yazar teziyle ilişkisini en az 4-5 ana Markdown başlığı (`## 1. Giriş`, `## 2..5` analiz bölümleri ve `## Sonuç ve Genel Değerlendirme`) altında kapsamlıca yaz. Asla kamera/sahne yönergesi karıştırma.
        4. TAM SÜRE UYUMLU SAHNE PLANI JSON: Tam **{6} adet** sahne planla (`SahneNo: 1..{6}`). Sahnelerin `TahminiSureDk` tam sayılarının toplamı KESİNLİKLE **{5}** olmalıdır! Her sahnede hangi düşünürlerin, hangi `[K#]` kanıtlarının ve hangi `[YAZAR-TEZİ]` bağlantısının işleneceğini detaylıca yaz.

        Yanıtını KESİNLİKLE aşağıdaki iki ayraçla (`===ARASTIRMA_OZETI===` ve `===SAHNE_PLANI_JSON===`) ayırarak döndür:

        ===ARASTIRMA_OZETI===
        # {2}
        **Kapsamlı Bölüm Araştırma Özeti (Bölüm {0}/{1})**
        ... (Detaylı Markdown araştırma özeti buraya) ...

        ===SAHNE_PLANI_JSON===
        [
          {{
            "SahneNo": 1,
            "Baslik": "Sahne Başlığı",
            "TahminiSureDk": 3,
            "IslenecekArgumanVeDusunurler": "Bu sahnede derinlemesine açıklanacak düşünürler, kavramlar, neden-sonuç zinciri ve somut örnekler",
            "YazarTeziBaglantisi": "Bu sahnenin [YAZAR-TEZİ] ile nasıl bütünleştiği",
            "AtananKanitKodlari": ["[K1]", "[K3]"]
          }}
        ]
        """;

    public const string EpisodeStage2SceneBatchPrompt = """
        Sen AgentWrite mimarisiyle çalışan usta bir stüdyo belgesel/ders metin yazarı ve yönetmenisin.
        Çoklu video serisinin **{0}. Bölümü ("{1}", Toplam Süre: {2} dk, Hedef Kitle: {3})** için sahne-sahne derin senaryo yazımının **{4}** grubunu yazıyorsun.

        [YAZAR-TEZİ VE KURGU DİREKTİFİ] (MERKEZİ ANLATI OMURGASI):
        {5}

        [ÖNCEKİ BÖLÜM DEVİR DEFTERİ] (Tekrardan kaçın, sürekliliği koru):
        {6}

        BÖLÜMÜN TÜM SAHNE HARİTASI (Genel akışta nerede olduğunu bilmek için — YALNIZCA aşağıda istenen sahneleri yaz!):
        {7}

        DOĞRULANMIŞ ARAŞTIRMA ÖZETİ (Olgusal ve kavramsal referans zemini):
        {8}

        BİR ÖNCEKİ SAHNENİN SON BAĞLAMI (Pürüzsüz ve organik geçiş için — asla tekrar "Merhaba/Hoş geldiniz" deme, bu son cümlenin bıraktığı yerden doğal bir düşünce köprüsüyle devam et!):
        {9}

        ŞİMDİ YAZMAN GEREKEN HEDEF SAHNELER VE KELİME BÜTÇESİ (YALNIZCA BU SAHNELERİ YAZ!):
        {10}

        [K#] KAYNAK KANITLARI:
        {11}

        İHLAL EDİLEMEZ YAZIM VE DERİNLİK KURALLARI:
        1. GERÇEK STÜDYO HACİM ZORUNLULUĞU: Bu çağrıda yazacağın sahnelerin `[Prompter Metni (Aynen Okunacak)]` bölümleri toplamda **EN AZ {12} KELİME** (dakika başı ~140-150 kelime konuşma temposu) ve **HER SAHNEDE EN AZ 3–5 DOLU, UZUN PARAGRAF** olmalıdır!
        2. İSMEN GEÇİŞTİRME YASAĞI: Düşünürlerin, kuramcıların veya kavramların yalnızca adını anıp 2 cümleyle geçmek KESİNLİKLE YASAKTIR! Her düşünürün/yaklaşımın:
           - Hangi temel soruya veya krize yanıt aradığını,
           - Argümanının **neden-sonuç mantığını** adım adım nasıl kurduğunu,
           - Somut bir vaka, düşünce deneyi veya günlük hayat örneğiyle ne anlama geldiğini,
           - Kullanıcının **`[YAZAR-TEZİ]`** çerçevesiyle nasıl bütünleştiğini veya nerede ayrıştığını
           kameraya konuşan usta bir anlatıcının doğal, akıcı ve entelektüel Türkçesiyle sindire sindire anlat!
        3. SIFIR UYDURMA İSTATİSTİK & DIŞ KAYNAK KURALI: `[K#]` kaynaklarında açıkça geçmeyen HİÇBİR yüzde (`%`), sayısal oran, anket veya hayali deney uydurma. `[Mod: DIŞ KAYNAK]` olan kaynakların cümle dizilimini veya özgün metaforlarını kopyalamadan argümanlarını kendi özgün cümlelerinle kur. Prompter metninin içine `[K1]` gibi köşeli parantez kodları YAZMA (sunucu okuyacağı için doğal konuşma metni olmalı).
        4. SAHNE GEÇİŞLERİ:
           - Yalnızca `Sahne 1` ise açılış kancasıyla güçlü bir giriş yap.
           - Ara sahnelerde (`SahneNo > 1`) KESİNLİKLE yeniden selamlama veya sıfırdan giriş yapma; önceki sahnenin son düşüncesinden organik geçişle başla.
           - Yalnızca bölümün EN SON sahnesiyse tüm bölümü sentezleyen güçlü bir kapanış ve varsa sonraki bölüme merak uyandıran köprü kur.
        5. BİREBİR MARKDOWN FORMATI (Her sahne için bu şablonu eksiksiz uygula, başka üst başlık veya açıklama yazma!):

        ## Sahne X: (Sahnenin Gerçek Başlığı)
        **Zaman Aralığı:** MM:SS - MM:SS

        ### 1. [Görsel & Edit Yönergesi]
        - **Kamera Açısı & Kadraj:** (Sunucu kadrajı, lens/ışık/atmosfer ve vurgu anındaki kamera hareketi)
        - **Görsel, B-Roll ve Kurgu:** (Anlatılan argümanla eşzamanlı ekrana gelecek somut arşiv görüntüleri, diyagramlar, sinematik B-Roll akışı)
        - **Metin Grafiği (On-Screen Text):** (Ekranda belirecek Lower Third, düşünür/kavram kartı, kilit soru veya şema)
        - **Müzik & Ses Efekti:** (Sahnenin duygusal/entelektüel temposuna uygun müzik ve geçiş ses tasarımı)

        ### 2. [Prompter Metni (Aynen Okunacak)]
        > "(1. Dolu Paragraf: Sahneye organik geçiş, temel sorunun/iddianın kurulması — en az 4-6 cümle...)"
        >
        > "(2. Dolu Paragraf: Düşünürlerin/kaynakların argümanlarının neden-sonuç zinciriyle derinlemesine açılımı — en az 5-7 cümle...)"
        >
        > "(3. Dolu Paragraf: Somut vaka, tarihsel/güncel örnek veya karşılaştırmalı analiz — en az 5-7 cümle...)"
        >
        > "(4. Dolu Paragraf: [YAZAR-TEZİ] perspektifiyle kavramsal sentez, derin yorum ve bir sonraki sahneye merak uyandıran geçiş — en az 4-6 cümle...)"
        """;

    public const string EpisodeStage3ContinuityLedgerPrompt = """
        Çoklu video serisinin az önce üretilen **{0}. Bölümü ("{1}")** metinlerini inceleyerek, bir sonraki bölümün (`Bölüm {2}`) tekrara düşmeden organik şekilde devam edebilmesi için yapılandırılmış JSON **Bölüm Devir Defteri (`EpisodeContinuityLedgerDto`)** çıkar.

        BÖLÜM ARAŞTIRMA ÖZETİ:
        {3}

        BÖLÜM VİDEO PLANI VE KAPANIŞ SAHNESİ:
        {4}

        Lütfen SADECE aşağıdaki alanlara sahip geçerli ve saf bir JSON objesi döndür (```json sarmalaması olmadan):
        {{
            "BolumNo": {0},
            "CalismaBasligi": "{1}",
            "IslenenDusunurlerVeKavramlar": [
                "Bu bölümde detaylıca açıklanmış düşünür ve kavram 1 (sonraki bölümde sıfırdan tanımlanmaması için)",
                "İşlenen düşünür ve kavram 2"
            ],
            "VerilenSomutOrnekler": [
                "Bu bölümde kullanılmış somut vaka, deney veya benzetme 1 (sonraki bölümde aynı örneğin tekrarlanmaması için)"
            ],
            "AcikBirakilanSorular": [
                "Bu bölümde bilerek açık bırakılan veya sonraki bölüme devredilen felsefi/analitik soru 1"
            ],
            "SonCumleGecisNotu": "Bölümün kapanış sahnesindeki son geçiş fikri ve bir sonraki bölümün nereden devralması gerektiği (2-3 cümle)"
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
        
        [YAZAR-TEZİ VE ÖZEL DİREKTİFLER]:
        {5}

        KAYNAKLAR ([K#]):
        {4}

        ÇOK KRİTİK VE KESİN KURALLAR:
        1. İKİ KATMANLI OTORİTE: `[YAZAR-TEZİ VE ÖZEL DİREKTİFLER]` katmanındaki kavramsal çerçeveyi ana omurga olarak işle; olgusal iddialarda, deneylerde ve sayılarda ise SADECE verilen `[K#]` kaynaklarını kullan, asla sahte oran/deney uydurma.
        2. KAYNAK KULLANIM MODLARI KURALLARI:
           - "DOĞRUDAN REFERANS" kaynaklardan doğrudan kavram ve alıntı aktarılabilir.
           - "DIŞ KAYNAK - KOPYALAMAK KESİNLİKLE YASAKTIR" kaynaklardan yalnızca argüman mantığını al; cümle dizilimini ve metaforlarını kopyalama.

        Lütfen yanıtını doğrudan aşağıdaki 3 bölüm başlığı (ayraç) ile ayırarak döndür:

        ===ARASTIRMA_OZETI===
        # {0}
        **Kapsamlı Araştırma Özeti**
        (Detaylı Markdown araştırma özeti)

        ===VIDEO_PLANI===
        # {0}
        **Video Tipi:** Uzun Formatlı Stüdyo Analizi / Ders
        (Sahne bazlı `### 1. [Görsel & Edit Yönergesi]` ve `### 2. [Prompter Metni (Aynen Okunacak)]` içeren tam çekim planı)

        ===DEVIR_NOTU===
        (Sonraki bölüme devir defteri JSON özeti)
        """;

    public const string SourceTopicExtractionPrompt = """
        Aşağıda verilen kaynak içeriği ({0}, Parça {1}/{2}) dikkatlice incele ve içeriğinde anlatılan temel konuları, düşünürleri, kavramları ve vakaları çıkar.
        
        KAYNAK METNİ:
        {3}
        
        GÖREV:
        - İçerikte geçen tüm önemli başlıkları, tartışılan düşünürleri/yazarları ve konuları tespit et.
        - Her konu için kısa bir açıklama ve tahmini anlatım süresi (dakika) belirle.
        - Yalnızca saf JSON formatında yanıt ver:
        {{
            "Konular": [
                {{
                    "Baslik": "Konu Başlığı",
                    "Aciklama": "Bu konunun içeriği, geçen düşünürler/argümanlar ve nelerin anlatıldığı hakkında 1-2 cümle",
                    "TahminiSureDk": 3
                }}
            ]
        }}
        """;

    public const string TermPreservingTranslationPrompt = """
        Sen akademik, teknik ve sektörel içeriklerde uzman bir çevirmen ve terminoloji editörüsün.
        Aşağıda "{0}" dilindeki bir kaynağın damıtılmış bilgi dökümü yer almaktadır.
        Görevin bu metni hiçbir bilgi, argüman veya detay kaybı olmadan akıcı, doğal ve anlaşılır bir TÜRKÇEYE çevirmektir.

        ÇOK KRİTİK KAVRAM VE TERİM KORUMA KURALLARI:
        1. O DİLDE KALMASI GEREKEN KAVRAMLAR: Uluslararası literatürde, felsefede, hukukta, yazılımda veya ilgili disiplinde orijinal haliyle yerleşmiş özel terimleri, kavramları, model/yöntem adlarını (Örn: "Prompt Engineering", "Zero-Shot", "Zeitgeist", "Dasein", "Common Law", "Rule of Law", "Fine-Tuning", "Backpropagation" vb.) zorlama ve yapay kelimelerle Türkçeleştirme!
        2. KULLANIM FORMATI: Bu tür özel kavramlar metinde ilk kez geçtiğinde Türkçe karşılığını/açıklamasını verip parantez içinde orijinal terimi koru veya doğrudan orijinal terimi kullanıp parantez içinde kısa Türkçe karşılığını belirt (Örn: "Zero-Shot (Sıfır Örnekli Öğrenme)" veya "Varoluşsal Bulunuş (Dasein)"). Sonraki geçişlerde kavramın en doğal kabul gören halini tutarlı şekilde kullan.
        3. AKICILIK VE BÜTÜNLÜK: Terimler dışındaki tüm cümle yapılarını, argüman akışlarını ve açıklamaları çeviri kokmayan, stüdyoda bir uzmanın anlatacağı doğallıkta kusursuz bir Türkçeye dönüştür.
        4. BİLGİ SADAKATİ: Metindeki hiçbir paragrafı kısaltma, özetin özetini çıkarma; tüm maddeleri ve örnekleri birebir koruyarak çevir.

        METİN ({0}):
        {1}

        Lütfen SADECE çevrilmiş Türkçe metni döndür. Başka hiçbir giriş, önsöz veya açıklama ekleme.
        """;
}
