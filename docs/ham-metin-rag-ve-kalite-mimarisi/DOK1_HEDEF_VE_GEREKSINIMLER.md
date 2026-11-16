# DOK1: Hedef ve Gereksinim Dokümanı (Biz Ne Yapmak İstiyoruz?)

> **4'lü Mimari Dokümantasyon Seti — Navigasyon:**
> - **DOK1 (Bu Dosya):** [DOK1_HEDEF_VE_GEREKSINIMLER.md](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK1_HEDEF_VE_GEREKSINIMLER.md) — *Hedef, Kapsam, İş Paketleri ve Kabul Kriterleri*
> - **DOK2:** [DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — *Mevcut Kod Debug ve Akış Raporu (Şu Anki Sistem Nasıl Çalışıyor?)*
> - **DOK3:** [DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — *Endüstri, Benzer Projeler ve Literatür Araştırması*
> - **DOK4:** [DOK4_ADIM_ADIM_GELISTIRME_PLANI.md](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — *Adım Adım Geliştirme Planı (Nasıl Geliştireceğiz?)*

---

## 1. Vizyon ve Temel Amaç

VideoÖzet AI uygulamasında uzun PDF dokümanları (akademik makaleler, kitap bölümleri, hukuk/felsefe metinleri) ve saatlik video ders kayıtlarından (STT transkriptleri) yüksek doğrulukta, bol ayrıntılı, kaynaklara sadık **Araştırma Özeti** ve **Çekim/Prompter Video Planı** üretmek istiyoruz.

Mevcut mimarideki *"uzun ham metni (~30.000–150.000 karakter) önce ~2.000 karakterlik damıtılmış özete sıkıştırıp sadece o özeti vektör veritabanına indeksleme"* yaklaşımı şu 3 temel soruna yol açmaktadır ([DOK2](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) içindeki canlı veritabanı ölçümüyle doğrulanmıştır):

1. **%92–%94 Bilgi Kaybı:** Her doküman ve videonun yalnızca %6–%8'i vektör indeksine girmekte; yazar isimleri, vaka analizleri, dakika bazlı anlatımlar, deneyler, kavram ayrımları ve somut veriler arama aşamasında tamamen görünmez olmaktadır.
2. **Kaynak Tekelleşmesi ve Halüsinasyon:** Özetlemesi hata veren birkaç PDF ham metniyle indekslenip vektör uzayını ele geçirmekte; diğer kaynaklardan parça gelmediği için LLM eksikleri dış dünyadan uydurduğu tanımlarla ve istatistiklerle (ör. kaynakta olmayan yüzde değerleri) doldurmaktadır.
3. **Kırık Kalite Kontrol (QC) ve Kopuk UI:** QC mekanizması üretimde seçilen parçalar yerine başka parçaları veya kırpılmış bağlamı görmekte, yalnızca araştırma özetini denetleyip prompter metnini denetlememekte ve hata/boş bağlam durumlarında sahte `%100` güven skoru üretmektedir.

**Temel Hedefimiz:** Kullanıcının kaygısı olan *"tüm ham metni modele gönderirsek LLM bağlamı kaldırmaz ve aşırı sürer"* problemini **Sıfır Kayıplı Bağlamsal Ham Metin İndeksleme + Çoklu Alt-Sorgulu Hibrit RAG + Sağlayıcıya Duyarlı Parçalı Sentez + Kanıt-Bağlı QC** mimarisiyle çözmektir. Böylece:
- Hiçbir modelin bağlam penceresine 1,5 milyon karakter ham metin tek seferde yığılmaz (hızlı ve ekonomik kalır),
- Veritabanında **%100 ham metin** ve **zaman damgalı video segmentleri** saklanır (%0 veri kaybı),
- Üretim sırasında yalnız sorulan konuların **en ilgili, çeşitli ve bağlamı korunmuş ham metin parçaları (`~450–600` kelimelik pencereler)** seçilir.

---

## 2. Kapsam ve İş Paketleri (`İP-1` – `İP-6`)

### İP-1: Sıfır Kayıplı Ham Metin (`HamMetin`) ve Zaman Damgalı Transkript Bağlamsal İndeksleme + Gürültü Eleme
1. **Ham Metin Öncelikli İndeksleme:**
   - Dokümanlarda özet (`DamitilmisOzetMetni` / `CevrilmisOzetMetni`) değil, doğrudan `DokumanMetin.HamMetin` parçalara bölünerek indekslenecektir.
   - Videolarda yalnız `VideoSummary` değil, `VideoTranscript.HamMetin` (ve Whisper segment zaman damgaları) parçalara bölünerek indekslenecektir. `VideoSummary` yalnızca hızlı konu yönlendirmesi/kimlik kartı amacıyla isteğe bağlı destek parçası (`ChunkTipi = OzetKart`) olarak tutulacaktır.
2. **Zaman Damgalı Video Segmentasyonu:**
   - [local_whisper_service.py](file:///d:/Hoca/VideoOzet/scripts/local_whisper_service.py) tarafından halihazırda üretilen `segments` (`start`, `end`, `text`) verisi STT sağlayıcısı katmanında yakalanıp `VideoTranscript.ZamanDamgalari` alanına JSON olarak kaydedilecektir.
   - Video parçalama aşamasında bu segmentler `~450–550` kelimelik pencerelerde birleştirilip her parçanın `StartTimeMs` ve `EndTimeMs` değerleri gerçek konuşma milisaniyesi olarak kaydedilecektir.
   - Henüz zaman damgası bulunmayan (önceden STT edilmiş) videolar için indeksleme sırasında `Video.SureSaniye` ve kelime konumu üzerinden oransal zaman aralığı tahmini üretilecektir; böylece yeniden STT çalıştırmaya gerek kalmayacaktır.
3. **Bağlamsal ve Cümle-Duyarlı Parçalayıcı (`ContextualTextChunker`):**
   - Kelime ortasından/cümle ortasından körlemesine kesen 500 kelimelik yapı yerine **paragraf ve cümle sınırlarına duyarlı**, **`%15` örtüşmeli (`overlap`)** (`~450–555` kelime pencere, `~65–80` kelime örtüşme) parçalayıcı kullanılacaktır.
   - Doküman içi aktif başlık izlenerek (`BolumBasligi`) parça meta verisine işlenecektir.
   - Her parçanın vektörleştirilen ve bağlama sunulan metni standart bağlamsal üstbilgi ile zenginleştirilecektir:
     `[Kaynak: <Ad> | Tür: <PDF/Video> | Bölüm/Zaman: <Başlık veya mm:ss-mm:ss> | Dil: <TR/EN> | Kullanım: <Doğrudan Referans / Dış Kaynak>]`
4. **Akademik Kaynakça (Bibliography/References) Gürültü Filtrelemesi:**
   - Makale/kitap sonlarındaki `KAYNAKÇA`, `REFERENCES`, `BIBLIOGRAPHY`, `WORKS CITED` bölümleri algılanıp `ChunkTipi = Kaynakca` olarak işaretlenecek ve standart RAG bağlam aramalarından dışlanacaktır (yazar adı listelerinin gerçek içerik parçalarını bastırmasını önlemek için).
5. **Sahte Fallback Vektör Yasağı:**
   - [OllamaEmbeddingProvider.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.Business/Infrastructure/AI/OllamaEmbeddingProvider.cs) içindeki `GetHashCode()` tabanlı sahte `CreateLocalFallbackEmbedding` davranışı (canlı mod için) tamamen kaldırılacaktır. Ollama erişilemediğinde veya zaman aşımına uğradığında sessizce bozuk vektör üretmek yerine anlamlı istisna fırlatılarak MassTransit yeniden deneme (`retry`) mekanizmasının devreye girmesi sağlanacaktır.
6. **Tek Tıkla "Ham Metinle Yeniden İndeksle":**
   - Mevcut eğitimlerde (ör. `46` dosyalı `"Özgür irade ve algoritmalar"` eğitimi) dosyaları sıfırdan yüklemeye veya saatlik videoları yeniden STT'ye sokmaya gerek kalmadan, DB'de halihazırda bekleyen `DokumanMetin.HamMetin` ve `VideoTranscript.HamMetin` verilerini dakikalar içinde yeni bağlamsal şemayla yeniden indeksleyen toplu işlem ve UI butonu eklenecektir.

---

### İP-2: Hafif "Kaynak Kimlik Kartı" ve Kırpmasız Haritalama (Özet ile Ham Metin Rol Ayrımı)
1. **Kırpmasız Özetleme / Haritalama:**
   - Doküman özetlemedeki `20.000` karakter sınırı ve Groq özetlemedeki `9.500` karakter baş-son kırpması kaldırılacaktır. Uzun doküman ve videolar gerekirse parça-birleştir (`Map-Reduce`) yöntemiyle tam kapsanacaktır.
2. **Özetlerin Doğru Rolü:**
   - Üretilen damıtılmış özetler ve Türkçe çeviriler RAG ham metin indeksinin yerine geçmeyecek; yalnız UI'daki *"Metin & Özet"* önizlemesi ve Seri Planlayıcı modundaki üst seviye müfredat/konu haritalaması için **"Kaynak Kimlik Kartı"** olarak kullanılacaktır.
3. **Hata Şeffaflığı:**
   - Özet üretimi başarısız olduğunda istisna yutulup işlem sessizce `NULL` özetle geçmeyecek; kullanıcıya uyarı olarak yansıtılacak ancak ham metin indekslemesi (`İP-1`) özet başarısına bağlı kılınmadan bağımsız tamamlanacaktır.

---

### İP-3: Çoklu Alt-Sorgu + Hibrit Arama (`pgvector` + FTS/Trigram) + Çapraz Dil + Kaynak Çeşitliliği Kotası (MMR)
1. **Çoklu Alt-Sorgu Ayrıştırma (`QueryDecomposer`):**
   - Kullanıcının `"Konu veya Detaylı İçerik Planı"` alanına yazdığı çok bölümlü taslaklar (ör. 9 bölümlü akademik/popüler ödev planı) tek bir büyük sorgu olarak embedding'e sokulmayacak; her bölüm/madde bağımsız bir **alt-sorguya (`Sub-Query`)** ayrıştırılacaktır.
2. **Hibrit Arama ve Reciprocal Rank Fusion (RRF):**
   - Her alt-sorgu için yalnız `pgvector` anlamsal kosinüs benzerliği (`bge-m3`) değil, eşzamanlı olarak PostgreSQL sözcüksel/anahtar kelime araması (`tsvector` GIN + özel isimler ve Türkçe ekli kelimeler için `pg_trgm`) çalıştırılacaktır.
   - İki sıralama listesi **Reciprocal Rank Fusion (RRF)** formülüyle birleştirilerek hem kavramsal anlam hem de kesin terim/yazar isimleri (*Gazzâlî, Mâtürîdî, İbn Rüşd, Sapolsky, Karen Yeung, hypernudge, Akbulut*) yakalanacaktır.
3. **Çapraz Dil Terim Genişletmesi (`Cross-Lingual Query Expansion`):**
   - Eğitimde İngilizce/Almanca vb. yabancı dil kaynaklar varsa, alt-sorgudaki kilit kavramsal terimler (ör. *özgür irade -> free will, determinizm -> determinism, uyumluluk -> compatibilism, öneri algoritmaları -> recommender systems, dikkat ekonomisi -> attention economy*) için çift dilli anahtar terim eşleşmesi sağlanacaktır.
4. **Kaynak Çeşitliliği Kotası (Per-Source Cap + MMR):**
   - Maximal Marginal Relevance (MMR) ile hem sorgu alakalılığı hem parçalar arası yenilik/farklılık dengelenecektir.
   - Eğitimdeki kaynak sayısına duyarlı **Kaynak Başına Maksimum Parça Tavanı** (ör. 10+ kaynaklı eğitimlerde bir PDF/videodan en fazla `2–3` parça) uygulanarak tek bir kaynağın tüm Top-K sonuçlarını domine etmesi imkânsız hale getirilecektir.
   - Her alt-başlık/alt-sorgu için kaç kanıt parçasının bulunduğu (**Alt Başlık Kanıt Kapsama Matrisi**) hesaplanıp zayıf kalan alt başlıklar sentez öncesinde tespit edilecektir.

---

### İP-4: Kanıt-Bağlı İki Aşamalı Sentez (`Araştırma Özeti` → `Video Planı`) ve Sağlayıcıya Duyarlı Pencereleme
1. **Bağlam Zinciri (`Araştırma Özeti` → `Video Planı`):**
   - Klasik modda `VideoPlani` ile `ArastirmaOzeti` birbirinden habersiz ayrı iki çağrıyla üretilmeyecektir. Önce seçilmiş `[K1..Kn]` kanıt parçalarına dayanarak satır içi atıflı (`[K1]`, `[K2]`) **`ArastirmaOzeti`** üretilecek; ardından **`VideoPlani`**, hem kanıtlı `ArastirmaOzeti` iskeletini hem de doğrudan ham alıntı/zaman damgası parçalarını girdi olarak alarak sahnelere dökülecektir.
2. **Kapalı Dünya, Sıfır Uydurma İstatistik ve Kapsam Dürüstlüğü Kuralları:**
   - Kaynakta geçmeyen hiçbir sayısal veri, yüzde (ör. *"Facebook kullanıcılarının %70'i"*), araştırma yılı veya rapor adı LLM'in genel kültüründen eklenemez.
   - Kullanıcının istediği bir alt başlık kaynaklarda hiç bulunmuyorsa (veya yalnız kısmen varsa), model o konuda teori/istatistik uydurmak yerine açıkça **"Kaynaklarda Yeterli Kanıt Bulunmayan / Eksik Alt Başlıklar"** bölümünde veya notunda dürüstçe beyan edecektir.
3. **Kaynak Kullanım Modları (`Doğrudan Referans` vs `Dış Kaynak`):**
   - `Doğrudan Referans` etiketli `[K#]` parçalarından doğrudan alıntılar, hoca/yazar atıfları (`Akbulut (2023)`, `Gazzâlî`) ve video zaman damgası referansları teşvik edilecektir.
   - `Dış Kaynak (Sadece Argüman)` etiketli `[K#]` parçalarından yalnızca mantıksal argüman ve neden-sonuç zinciri soyutlanacak; cümle dizilimi, özgün metaforlar ve konuşmacının/yazarın üslubu kesinlikle kopyalanmayacaktır.
4. **Dar Modellerde Kırpma Yerine Bölüm Bazlı Üretim (`Sectioned Synthesis`):**
   - Ana sağlayıcı (Gemini / OpenRouter / Claude vb.) geniş bağlamda tek veya 2 aşamada eksiksiz üretim yaparken, ücretsiz Groq (`8K TPM`) gibi dar token pencereli yedek sağlayıcılara düşüldüğünde metnin `%90`'ını kesip atan kırpma mekanizması yerine, alt-başlık/sahne gruplarına bölünmüş **Parçalı Üretim (Sectioned Generation)** uygulanacaktır.

---

### İP-5: Gerçek Kanıt-Bağlı Kalite Kontrol (QC), Sayısal Denetçi ve 1-Turlu Öz-Düzeltme (`Auto-Refine`)
1. **Seçilen Kanıtların Birebir QC'ye İletilmesi:**
   - [SourceContextBuilder.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.Business/Services/SourceContextBuilder.cs) içindeki `BuildFromChunkIdsAsync` hatası ve [ClaudeSynthesisProvider.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.Business/Infrastructure/AI/ClaudeSynthesisProvider.cs) içindeki `[KAYNAK #` vs `[KAYNAK:` etiket uyuşmazlığı kökten giderilecektir. QC servisi istisnasız **yalnız ve tüm `UsedChunkIds` (`[K1..Kn]`) parçalarını** tam metinleriyle görecektir.
2. **Yalnız Özet Değil `VideoPlani` (Prompter Metni) Denetimi:**
   - Stüdyoda sunucunun doğrudan okuyacağı `VideoPlani` prompter metninde uydurma bilgi çıkmasını engellemek için QC hem `ArastirmaOzeti` hem `VideoPlani` iddialarını kapsamına alacaktır.
3. **Deterministik Sayısal & Atıf Ön-Denetçisi (`DeterministicClaimScanner`):**
   - Üretilen metinlerde geçen tüm yüzdeler (`%\d+`, `\d+%`), tarihler ve kaynak dışı rapor isimleri regex ile çıkarılıp `contextData` içinde geçip geçmediği deterministik olarak kontrol edilecek; kaynakta hiç bulunmayan sayılar QC denetçisine öncelikli kırmızı bayrak olarak verilecektir.
4. **Sahte `%100` Güven Skoru İptali:**
   - `contextData` boş geldiğinde, LLM JSON parse hatası verdiğinde veya QC çağrısı hata aldığında otomatik `GuvenSkorYuzde = 100` yazan tüm sahte fallback blokları ([QualityCheckService.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.Business/Services/QualityCheckService.cs), [QualityCheckConsumer.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.Worker/Consumers/QualityCheckConsumer.cs), [ContentRequestsController.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.API/Controllers/ContentRequestsController.cs), [SeriesVideoRevisionConsumer.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.Worker/Consumers/SeriesVideoRevisionConsumer.cs)) kaldırılacaktır. Hata/boş bağlam durumunda `QcDurumu = Hata` / `Belirsiz` ve skor `0` (veya `null`) olarak dürüstçe raporlanacaktır.
5. **1-Turlu Otomatik Öz-Düzeltme (`Self-Correction / Auto-Refine`):**
   - İlk QC denetiminde `desteklenmedi > 0` tespit edilirse, kullanıcıya halüsinasyonlu çıktı teslim etmek yerine desteklenmeyen iddialar ve ilgili `[K1..Kn]` kanıt parçaları sentez modeline geri verilerek otomatik temizleme/revizyon turu (`Auto-Refine`, maks. `1` tur) uygulanacak, ardından nihai QC skoru hesaplanacaktır. Orijinal sürüm (`V1`) ve otomatik düzeltilmiş sürüm (`V2`) versiyon geçmişinde şeffaf biçimde saklanacaktır.

---

### İP-6: Kopmayan SignalR İletişimi, HTTP Polling Yedeği ve UI Kanıt İzlenebilirliği
1. **Dayanıklı SignalR İstemcisi:**
   - [signalr.service.ts](file:///d:/Hoca/VideoOzet/src/VideoOzet.UI/src/app/core/services/signalr.service.ts) sınırlı varsayılan yeniden deneme yerine uzun süreli kademeli retry dizisine (`[0, 2000, 5000, 10000, 15000, 30000, 30000, ...]`) ve başlangıç bağlantı hatasında otomatik yeniden başlatma mekanizmasına geçirilecektir.
2. **3 Saniyelik HTTP Polling Yedeği:**
   - [course-detail.component.ts](file:///d:/Hoca/VideoOzet/src/VideoOzet.UI/src/app/features/course-detail/course-detail.component.ts) içindeki `requestContent()` ve `planSeries()` metodları API'nin döndürdüğü kimliği (`contentRequestId` / analiz durumu) saklayacak; üretim aktifken her `3` saniyede bir durumu HTTP ile kontrol edecektir. Böylece arka planda `45` saniyede biten bir içerik WebSocket kopsa bile en geç `3` saniye içinde UI'a düşecek, 5 dakikalık sahte zaman aşımı hatası ortadan kalkacaktır.
3. **UI Yeniden İndeksleme ve Kanıt İzlenebilirliği:**
   - Eğitim detay sayfasında **"Ham Metinle Yeniden İndeksle"** butonu (ilerleme çubuğu ile) ve üretim sonucunun yanında kullanılan `[K1]..[Kn]` parçalarının dosya adı, sayfa/bölüm veya video zaman damgası listesi görüntülenecektir.

---

## 3. İş Kuralları ve Ölçülebilir Kabul Kriterleri

| Kriter Kodu | Alan | Mevcut Durum ([DOK2](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)) | Hedef Kabul Kriteri |
| :--- | :--- | :--- | :--- |
| **KK-1** | İndeksleme Kapsamı | PDF'te %6,1 (`2.076/34.207` kr), Videoda %7,9 (`2.158/27.271` kr) ve yalnız 1 chunk | **%100 `HamMetin` + Transkript** kapsama; cümle duyarlı %15 örtüşmeli çoklu chunk; videolarda gerçek `StartTimeMs`–`EndTimeMs` |
| **KK-2** | Kaynak Çeşitliliği | Tekil istekte 6/10 chunk tek PDF'ten (`Akbulut`); İslam/sinirbilim kaynakları `0` chunk | `10+` kaynaklı eğitimde tek bir kaynağın payı **<= %20–25** (kaynak başına tavan `2–3` chunk); çoklu alt-sorguda alt başlık kapsama raporlu |
| **KK-3** | Bağlam Sadakati & Sentez | 20K / 9.5K baş-son kırpma; `VideoPlani` özetten bağımsız; uydurma `%70/%80` istatistikleri | Kırpma **0 karakter** (dar modelde parçalı üretim); `VideoPlani` kanıtlı `ArastirmaOzeti` + `[K#]` parçalarına bağlı; kaynak dışı istatistik **0** |
| **KK-4** | Kalite Kontrol (QC) Doğruluğu | Yanlış chunk (`BuildFromChunkIdsAsync` bug'ı), `[KAYNAK #` hatası, sahte `%100` fallback, `%12,50` skor | QC **%100 aynı `UsedChunkIds`** ile çalışır; hem `ArastirmaOzeti` hem `VideoPlani` denetlenir; sahte `%100` fallback **%0**; 1-turlu öz-düzeltme sonrası gerçek güven skoru **>= %85** |
| **KK-5** | Sağlamlık & UI Deneyimi | SignalR kopunca 5 dk sonra sahte *"çok uzun sürdü"* hatası | API arka planda tamamladığında HTTP polling yedeği ile **<= 3 sn** içinde ekranda görüntüleme; tek tıkla yeniden indeksleme |
