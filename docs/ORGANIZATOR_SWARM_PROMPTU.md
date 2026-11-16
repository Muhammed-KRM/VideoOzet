/goal
/teamwork

Lütfen hemen `swarm` (`native_swarm`) becerisini (`SKILL.md`) yükle ve bu görevi **Swarm Koordinatörü (Orchestrator)** olarak, tüm kabul kriterleri %100 doğrulanıp derleme ve testler sıfır hatayla geçene kadar **kesintisiz** yürüt.

---

# 🎯 1. GOAL (ANA HEDEF VE VİZYON)

`d:\Hoca\VideoOzet` çalışma alanındaki **VideoÖzet AI** projesini; mevcut sığ, kısıtları yok sayan, model seçimini kaybeden ve kaynakların çoğunu atlayan yapısından çıkarıp, **onlarca video ve PDF kaynağını (%100 kaynak kapsamıyla) tarayan, dış kaynakları kopyalamadan argümanlarını sentezleyen, kullanıcının özgün tezini (`[YAZAR-TEZİ]`) anlatının merkezine koyan, sahne-sahne (iterative) derin stüdyo çekim planı + tam uzunlukta prompter konuşma metni üreten** eksiksiz bir **Akıllı Video Serisi & Senaryo Stüdyosuna** dönüştüreceğiz.

**Zorunlu Referans Dokümanlar (Her ajan kendi görevine başlamadan önce mutlaka okuyacak):**
1. `d:\Hoca\VideoOzet\docs\DOK1_HEDEF_VE_GEREKSINIMLER.md` — İş paketleri (`İP-1`..`İP-8`), kırmızı çizgiler ve kabul kriterleri (`KK-1`..`KK-7`).
2. `d:\Hoca\VideoOzet\docs\DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md` — Mevcut kodun satır satır bug ve darboğaz analizi.
3. `d:\Hoca\VideoOzet\docs\DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md` — Uygulanacak Co-STORM, AgentWrite/LongWriter, Kota-Kısıtlı RRF+MMR ve VeriScore matematiksel/mimari modelleri.
4. `d:\Hoca\VideoOzet\docs\DOK4_ADIM_ADIM_GELISTIRME_PLANI.md` — 6 fazlı teknik uygulama planı, şema değişiklikleri ve fonksiyon imzaları.

---

# 🤖 2. SWARM MİMARİSİ, AJAN KADROSU VE MODEL ATAMALARI (CARBON / ARGON / HYDROGEN)

Sen **Koordinatör (`coordinator`)** rolündesin: **Kendin doğrudan kod yazma veya uzun terminal işleri çalıştırma!** İlk turda `swarm{action:"title", title:"VideoOzet Akilli Seri ve Senaryo Motoru Donusumu"}` ile başlığı ata ve aşağıdaki ajanları **belirtilen tam model ID'leriyle (`model` parametresini sabitleyerek)** kur.

> **Model Verimlilik Stratejisi:**
> - **`gemini-4-carbon-high` (Gemini 4 Carbon - High):** Çok katmanlı algoritmik tasarım, hiyerarşik RAG matematiği, çok aşamalı prompt mühendisliği, iteratif senaryo motoru ve derin kod/kabul kriteri denetimi (Verifier) için.
> - **`gemini-4-argon-high` (Gemini 4 Argon - High):** EF Core veri şeması/migration, REST API controller uçları, ses/doküman işleme boru hattı ve kapsamlı Angular Standalone UI bileşenleri için.
> - **`gemini-4-hydrogen-medium` (Gemini 4 Hydrogen - Medium):** Düşük maliyetli, yüksek hızlı derleme/test koşumu (`dotnet build`, `dotnet test`, `npm run build`) ve swarm kilitlenme takibi (`monitor`) için.

### Oluşturulacak Swarm Ajanları ve Kesin Dosya Sahiplik Sınırları:

1. **`monitor`** — Model: `gemini-4-hydrogen-medium`
   - **Rol:** Stall Monitor. `swarm_monitor` becerisini yükler, yalnızca kilitlenme (stall) olursa müdahale eder.
   - *(Not: Eğer `swarm_narrator` becerisi sistemde mevcutsa ayrıca `narrator` ajanını da kur.)*

2. **`schema-settings`** — Model: `gemini-4-argon-high` (Dalga 1 — Temel Sözleşmeler, Şema & Model Ayarları)
   - **Tek Sahibi Olduğu Dosyalar:**
     - `src/VideoOzet.Business/Interfaces/IAiProviderSettingsService.cs`
     - `src/VideoOzet.Business/Services/AiProviderSettingsService.cs`
     - `src/VideoOzet.Business/Interfaces/ISynthesisProvider.cs`
     - `src/VideoOzet.Business/Interfaces/ISourceTopicMapper.cs`
     - `src/VideoOzet.Business/Services/ISourceContextBuilder.cs`
     - `src/VideoOzet.Business/Services/IQualityCheckService.cs`
     - `src/VideoOzet.Business/DTOs/SeriesDtos.cs`
     - `src/VideoOzet.Data/Entities/SeriBolum.cs`
     - `src/VideoOzet.Data/Entities/SeriPlani.cs`
     - `src/VideoOzet.Data/Entities/DokumanMetin.cs`
     - `src/VideoOzet.Data/Configurations/SeriBolumConfiguration.cs`
     - `src/VideoOzet.Data/Configurations/SeriPlaniConfiguration.cs`
     - `src/VideoOzet.Data/Configurations/DokumanMetinConfiguration.cs`
     - `src/VideoOzet.Data/Migrations/*` (Yeni EF Core migration)
     - `src/VideoOzet.Business/Services/EgitimManager.cs`
     - `src/VideoOzet.API/Controllers/VideolarController.cs`

3. **`ingestion-stt`** — Model: `gemini-4-argon-high` (Dalga 2 — Kaynak Damıtma, STT Zaman Damgası & Canlı Mod Senkronizasyonu)
   - **Tek Sahibi Olduğu Dosyalar:**
     - `src/VideoOzet.Worker/Consumers/ExtractDokumanTextConsumer.cs`
     - `src/VideoOzet.Worker/Consumers/SummarizeVideoConsumer.cs`
     - `src/VideoOzet.Business/Infrastructure/AI/GeminiSummarizer.cs`
     - `src/VideoOzet.Business/Infrastructure/AI/GroqWhisperSttProvider.cs`
     - `src/VideoOzet.Business/Infrastructure/AI/CompositeSttProvider.cs`
     - `src/VideoOzet.Business/Infrastructure/AI/ContextualTextChunker.cs`
     - `src/VideoOzet.Business/Services/VideoManager.cs`
     - `src/VideoOzet.Business/Services/DokumanService.cs`

4. **`rag-core`** — Model: `gemini-4-carbon-high` (Dalga 2 — Tam Kaynak Haritası, Kota-Kısıtlı RRF+MMR & Dinamik Çapraz Dil Arama)
   - **Tek Sahibi Olduğu Dosyalar:**
     - `src/VideoOzet.Business/Services/SourceTopicMapper.cs`
     - `src/VideoOzet.Business/Services/SourceContextBuilder.cs`
     - `src/VideoOzet.Business/Services/QueryDecomposer.cs`

5. **`synthesis-engine`** — Model: `gemini-4-carbon-high` (Dalga 2 — Co-STORM Planlama, AgentWrite Sahne-Sahne Senaryo, `[YAZAR-TEZİ]` & VeriScore QC)
   - **Tek Sahibi Olduğu Dosyalar:**
     - `src/VideoOzet.Business/Helpers/PromptTemplates.cs`
     - `src/VideoOzet.Business/Helpers/LlmJson.cs`
     - `src/VideoOzet.Business/Infrastructure/AI/ClaudeSynthesisProvider.cs`
     - `src/VideoOzet.Business/Services/QualityCheckService.cs`
     - `src/VideoOzet.Worker/Consumers/TopicAnalysisConsumer.cs`
     - `src/VideoOzet.Worker/Consumers/SeriesPlanConsumer.cs`
     - `src/VideoOzet.Worker/Consumers/SeriesVideoGenerationConsumer.cs`
     - `src/VideoOzet.Worker/Consumers/GenerateContentConsumer.cs`
     - `src/VideoOzet.Worker/Consumers/QualityCheckConsumer.cs`

6. **`series-api-rev`** — Model: `gemini-4-argon-high` (Dalga 2 — Gerçek Harita Düzenleme API'si, Dinamik Bölüm Revizyonu, Kanıt Çözümleme & `BaglamEskidi`)
   - **Tek Sahibi Olduğu Dosyalar:**
     - `src/VideoOzet.API/Controllers/SeriesController.cs`
     - `src/VideoOzet.API/Controllers/ContentRequestsController.cs`
     - `src/VideoOzet.Worker/Consumers/SeriesVideoRevisionConsumer.cs`

7. **`angular-ui`** — Model: `gemini-4-argon-high` (Dalga 2 — Model Seçim Modalı/Navbar, Zengin Düzenlenebilir Seri Haritası, Kapsam Paneli & Kanıtlar Sekmesi)
   - **Tek Sahibi Olduğu Dosyalar:**
     - `src/VideoOzet.UI/src/app/core/services/settings.service.ts`
     - `src/VideoOzet.UI/src/app/shared/components/settings-modal/settings-modal.component.ts`
     - `src/VideoOzet.UI/src/app/shared/components/navbar/navbar.component.ts`
     - `src/VideoOzet.UI/src/app/core/services/api.service.ts`
     - `src/VideoOzet.UI/src/app/core/utils/markdown-docx.ts`
     - `src/VideoOzet.UI/src/app/features/series-planner/*`
     - `src/VideoOzet.UI/src/app/features/series-map/*`
     - `src/VideoOzet.UI/src/app/features/episode-viewer/*`
     - `src/VideoOzet.UI/src/app/features/plan-viewer/*`

8. **`qa-verifier`** — Model: `gemini-4-carbon-high` (Dalga 3 — Bağımsız Denetçi ve xUnit Test Yazarı)
   - **Tek Sahibi Olduğu Dosyalar:** `tests/**` altındaki tüm test dosyaları.
   - **Görevi:** Diğer hiçbir ajanın kaynak dosyasını düzenlemez! Yazarların yaptığı tüm değişiklikleri `DOK1`–`DOK4` gereksinimlerine göre satır satır denetler, kapsamlı xUnit birim/entegrasyon testlerini yazar ve hata/eksik bulursa doğrudan ilgili `@yazar` ajanına düzeltme raporu gönderir.

9. **`build-runner`** — Model: `gemini-4-hydrogen-medium` (Dalga 1 Sonu & Dalga 3 — Derleme, Migration ve Test Koşucusu)
   - **Görevi:** Kaynak dosya düzenlemez; `dotnet build VideoOzet.slnx`, `dotnet ef migrations add` / `database update`, `dotnet test` ve `src/VideoOzet.UI` içinde `npm run build` komutlarını çalıştırıp derleme/test çıktılarını panoya raporlar.

---

# 🛠️ 3. DALGA DALGA VE DOSYA DOSYA AYRINTILI UYGULAMA TALİMATLARI

## DALGA 1 — Temel Veri Şeması, Arayüz Sözleşmeleri ve Model Ayarları (`schema-settings`)
> **ÖNEMLİ:** Dalga 2 ajanları ortak interface ve entity'leri kullanacağı için önce `schema-settings` işini bitirip `[done]` raporlamalı, ardından Dalga 2'deki 5 paralel ajan aynı anda başlatılmalıdır (`angular-ui` ise Dalga 1 ile paralel başlayabilir çünkü C# dosyalarına dokunmaz!).

### `schema-settings` (`gemini-4-argon-high`) Ayrıntılı Görevleri:
1. **`DOK2 §1` & `DOK4 Faz 1` — Model Seçimi Senkronizasyonu:**
   - `IAiProviderSettingsService.cs` içindeki `AiProviderSettingsDto` kaydına `public bool AllowFallbackToSecondaryProvider { get; set; } = false;` ekle. Ayrıca frontend'den eski veya yeni isimle gelebilecek `antigravitySummaryModel` / `antigravityProxySummaryModel`, `antigravityVideoPlanModel` / `antigravityProxyVideoPlanModel`, `antigravityQcModel` / `antigravityProxyQcModel` alanlarının ikisini de yakalayabilmesi için JSON uyumluluğunu sağla (`AiProviderSettingsService.UpdateSettingsAsync` ve `GetSettingsAsync` içinde her iki alanı da senkron tut).
2. **`DOK4 Faz 2` — Entity Genişletmeleri ve EF Core Migration:**
   - `SeriBolum.cs` içine:
     - `public string Kanca { get; set; } = string.Empty;`
     - `public string SahneIskeletiJson { get; set; } = "[]";`
     - `public string AtananKaynaklarJson { get; set; } = "[]";`
     ekle ve `SeriBolumConfiguration.cs` dosyasını güncelle.
   - `SeriPlani.cs` içine:
     - `public string OnayTalimati { get; set; } = string.Empty;`
     ekle ve `SeriPlaniConfiguration.cs` dosyasını güncelle.
   - `DokumanMetin.cs` içine:
     - `public string? LlmModel { get; set; }`
     ekle ve `DokumanMetinConfiguration.cs` dosyasını güncelle.
   - EF Core migration oluştur (`AddSeriesRichPlanningAndSourceAssignment`) ve SQLite/PostgreSQL uyumlu olduğundan emin ol.
3. **`DOK4 Faz 2–5` — Ortak DTO ve Servis Arayüz İmzaları:**
   - `SeriesDtos.cs` içine şu DTO ve kayıtları eksiksiz tanımla:
     - `CourseSourceCardDto` (`KaynakId`, `KaynakTuru` ("Video"|"Dokuman"), `Baslik`, `Dil`, `KullanimTuru`, `TahminiToplamSureDk`, `Konular` listesi, `Ozet Bilgi`)
     - `SeriesSceneOutlineItemDto` (`SahneNo`, `Baslik`, `TahminiSureDk`, `AciklamaVeArgumanlar`, `AtananKaynakKodlari`)
     - `UpdatePlanDto` ve `UpdateEpisodeItemDto` (`BolumNo`, `CalismaBasligi`, `Kanca`, `AnaFikir`, `HedefSureDk`, `Konular`, `SahneIskeleti`, `AtananKaynakIdleri`)
     - `SeriesEpisodeGenerationRequest` (Bölüm no, toplam bölüm, çalışma başlığı, kanca, bölüm ana fikri, `HedefSureDk`, `BolumKonulariJson`, `SahneIskeletiJson`, `YazarTeziVeGenelOdak`, `EkTonTalimati`, `OnayTalimati`, `OncekiBolumDevirDefteriJson`, `HedefKitle`, `KaynakBaglamiText`)
     - `EpisodeContinuityLedgerDto` (`BolumNo`, `CalismaBasligi`, `IslenenDusunurlerVeKavramlar`, `VerilenSomutOrnekler`, `AcikBirakilanSorular`, `SonCumleGecisNotu`)
   - `ISourceTopicMapper.cs` içine `Task<IReadOnlyList<CourseSourceCardDto>> GetStructuredSourceCardsAsync(Guid egitimId, CancellationToken ct = default);` ekle.
   - `ISourceContextBuilder.cs` içindeki `BuildContextAsync` metoduna opsiyonel `IReadOnlyCollection<Guid>? preferredSourceIds = null` ve `int? targetChunksOverride = null` parametrelerini (varsayılan değerlerle geriye uyumlu olarak!) ekle.
   - `ISynthesisProvider.cs` içine:
     - Gelişmiş `GenerateSeriesPlanAsync` overload'unu (`topicAndThesis`, `targetAudience`, `toneInstruction`, `konuAnaliziJson`, `structuredSourceCardsJson`, `formattedConstraintsText`, `previousPlanJson`, `ct`),
     - Sahne-sahne derin üretim yapan `Task<(string ArastirmaOzeti, string VideoPlani, string DevirDefteriJson)> GenerateSeriesEpisodeAsync(SeriesEpisodeGenerationRequest request, Func<string, int, Task>? onProgress = null, CancellationToken ct = default);` metodunu ekle (mevcut imzaları kırmadan tut).
   - `IQualityCheckService.cs` içindeki `EvaluateAsync` metoduna opsiyonel `string? authorThesisContext = null` parametresini ekle.
4. **`DOK2 §2` & `PROJE_GENEL_DENETIM` — Klonlama ve Kuyruk Sıfırlama Düzeltmeleri:**
   - `EgitimManager.CloneAsync` içinde eksik bırakılan `KullanimTuru`, `IndekslemeTuru`, `CevrilmisOzetMetni`, `DamitilmisOzetMetni`, `OrijinalDil`, `LlmModel`, `ChunkTipi`, `KaynakBaslik`, `BolumBasligi`, `KaynakDil` ve `KaynakKonuCikarimlari` kopyalamalarını tamamla. Klonlanan eğitimden bir dosya silindiğinde başka bir eğitimin hâlâ kullandığı MinIO nesnesinin silinmemesi için referans kontrolü kuralını koy (veya `DeleteAsync` sırasında yalnız başka eğitimde referansı olmayan dosyaları temizle).
   - `VideolarController.ResetQueue` içinde sabit `localhost:15673` yerine `IConfiguration` (`RabbitMQ:Host`, `RabbitMQ:ManagementPort` 15672/15673) kullan ve temizlenen kuyruk listesine `TopicAnalysis`, `TopicAnalysisCompleted`, `SeriesPlan`, `SeriesVideoGeneration`, `SeriesVideoGenerated`, `SeriesVideoRevision` (ve `_error` kuyrukları) dahil tüm MassTransit kuyruklarını ekle.

---

## DALGA 2 — 5 Paralel Uzman Ajan (Sıfır Dosya Çakışması!)

### 1. `ingestion-stt` (`gemini-4-argon-high`) Ayrıntılı Görevleri:
- **`ExtractDokumanTextConsumer.cs` (`DOK2 §2.1`):**
  - `BuildRepresentativeSample(extractedText, 22000)` ile dokümanın %80'ini çöpe atan yapıyı kaldır! Uzun dokümanları ~20.000 karakterlik anlamlı bölümlere ayırarak her bölümden ana argümanları/konuları damıtıp birleştir (`damitilmisOzet`), yabancı dildeyse terim korumalı Türkçeye çevir (`cevrilmisOzet`) ve `dokumanMetin.LlmModel = _geminiProvider.ActiveModelName` olarak gerçek modeli kaydet.
- **`DokumanService.cs` & `VideoManager.cs` (`DOK2 §2.4`):**
  - `DokumanService.GetDokumanIcerikDetayAsync` içindeki sahte `LlmModel = "gemini-1.5-flash"` satırını kaldırıp `dokuman.DokumanMetin?.LlmModel` döndür.
  - `VideoManager.UpdateKullanimTuruAsync` ve `DokumanService.UpdateKullanimTuruAsync` çağrıldığında yalnızca ana tabloyu değil, o kaynağa ait tüm `VideoChunkDocuments` satırlarının `KullanimTuru` sütununu da veritabanında güncelle!
- **`ContextualTextChunker.cs` (`DOK3 §2.3`):**
  - `BuildHeader` içinde chunk metnine sabit `| Mod: DOĞRUDAN REFERANS` gömmeyi bırak! (Kullanım modu sonradan değiştirilebildiği için `Mod:` bilgisi çalışma anında `SourceContextBuilder` tarafından dinamik basılacak; böylece üst satır ile alt satır arasındaki çelişki tamamen ortadan kalkacak.)
- **`GeminiSummarizer.cs` & `SummarizeVideoConsumer.cs` (`DOK2 §1.6, §2.3`):**
  - `GeminiSummarizer.ExecuteLlmPromptAsync` içinde `AllowFallbackToSecondaryProvider == false` iken seçili sağlayıcı hata verdiğinde sessizce Groq sıkıştırılmış moduna düşmeyi engelle; kendi sağlayıcısında 3 deneme (exponential backoff) yap.
  - Uzun transkriptlerde JSON çıktı token limitine (`finish_reason == "length"`) çarparsa devam ettirme (continuation) veya parçalı damıtma uygula; JSON ayrıştırma başarısız olsa bile metin dilini güvenilir şekilde tespit et ki İngilizce videoların Türkçe çevirisi asla atlanmasın.
- **`GroqWhisperSttProvider.cs` & `CompositeSttProvider.cs` (`DOK2 §2.3`):**
  - `GroqWhisperSttProvider` içinde `verbose_json` yanıtındaki `segments` dizisini (`start`, `end`, `text`) ayrıştırıp `public string? LastSegmentsJson { get; private set; }` özelliğine JSON olarak ata ve `CompositeSttProvider` içinde `_groqProvider.LastSegmentsJson` değerini `LastSegmentsJson`'a aktar!
  - Ses akışı >20 MB ise FFmpeg aracılığıyla 15 dakikalık parçalara bölüp sırayla Groq Whisper'dan geçir ve segment zaman damgalarını parça başlangıç saniyesi kadar kaydırarak (offset) birleştir.

### 2. `rag-core` (`gemini-4-carbon-high`) Ayrıntılı Görevleri:
- **`SourceTopicMapper.cs` (`DOK2 §2.2` & `DOK4 Faz 2`):**
  - Doküman konu çıkarımında kesik özete bakma hatasını düzelt: Doğrudan tam `HamMetin` (veya parçalı tam damıtılmış metin) üzerinden `MaxWordsPerChunk = 18000` pencereleriyle tüm dokümanı tara; hiçbir bölümü atlama. `CurrentPromptVersion = 2` yaparak eski eksik önbelleklerin yenilenmesini sağla.
  - `GetStructuredSourceCardsAsync(Guid egitimId, CancellationToken ct)` metodunu uygula: Eğitimdeki tamamlanmış tüm video ve dokümanları `CourseSourceCardDto` listesi olarak (her kaynağın ID'si, başlığı, türü, dili, güncel `KullanimTuru`, çıkarılan tüm alt konuları, düşünür/vaka özetleri ve tahmini anlatım süresi ile) döndür.
- **`QueryDecomposer.cs` (`DOK2 §2.5`):**
  - Yalnızca 36 kelimelik "özgür irade" sözlüğüne bağımlı kalma! Eğitimde yabancı dilde kaynak (`hasForeignSource == true`) varsa, sözlük eşleşmesine ek olarak `IGeminiProvider` (veya hızlı özet modeli) ile sorgudaki özel kavramların kaynak dillerdeki akademik/teknik karşılıklarını dinamik üreten (hata durumunda deterministik sözlüğe geri dönen, önbellekli) çapraz dil genişleticiyi ekle.
- **`SourceContextBuilder.cs` (`DOK2 §4.1` & `DOK3 §2.3`):**
  - Sabit `GlobalTargetChunks = 24` darboğazını kaldır!
  - `preferredSourceIds` (bölüme atanan kaynak ID'leri) verildiğinde **2 Aşamalı Kota-Kısıtlı RRF + MMR** uygula:
    1. Hedef parça sayısını `targetChunksOverride ?? Math.Clamp(Math.Max(32, (preferredSourceIds?.Count ?? distinctCourseSources) * 3), 30, 64)` olarak dinamik hesapla (`maxChars` varsayılan `120000`).
    2. **Taban Kota Garantisi (Floor Quota = 1):** `preferredSourceIds` içindeki **her kaynaktan** en yüksek skorlu en az 1 parçayı (eğer genel bütçeye sığıyorsa 2 parçayı) öncelikle seç (`Recall@AssignedSource = %100`).
    3. Kalan bütçeyi alt-sorgu Round-Robin + çeşitlilik cezalı MMR ($\lambda = 0.72$) ile doldur.
  - `PopulateFormattedResult` içinde eski indekslenmiş chunk metinlerinin başında kalmış olabilecek eski `| Mod: ...]` ibaresini temizle ve yalnızca güncel `ResolveMode(chunk, meta)` bilgisini üst başlığa bas.

### 3. `synthesis-engine` (`gemini-4-carbon-high`) Ayrıntılı Görevleri:
- **`ClaudeSynthesisProvider.cs` Sağlayıcı Kilidi (`DOK4 Faz 1`):**
  - `CallLlmRawAsync` içinde `settings.AllowFallbackToSecondaryProvider == false` iken seçili sağlayıcı (OpenRouter veya Yerel Proxy) başarısız olduğunda **asla sessizce Groq `CompactPromptForGroq` (5.800 kr) moduna düşme!** Kendi sağlayıcısında 3 deneme yap, yine başarısızsa hangi sağlayıcı/modelin neden hata verdiğini açıkça fırlat.
- **İki Katmanlı Otorite (`[YAZAR-TEZİ]` + `[K#]`) ve Prompt Yenilemeleri (`PromptTemplates.cs`, `DOK1 İP-3`, `DOK3 §2.1–2.4`):**
  - **`TopicAnalysisPrompt` & `TopicAnalysisConsumer.cs`:** Kullanıcının `request.Konu` (odak/kurgu metni) ve `request.EkTonTalimati` alanlarını **`[YAZAR-TEZİ VE KURGU DİREKTİFİ]`** olarak prompta enjekte et. Kullanıcının kendi kurgusunu/tezini "kaynakta yok" diye dışlamak yerine, tüm kaynak kartlarını (`GetStructuredSourceCardsAsync`) bu tezi besleyecek şekilde analiz et; kaynakta doğrudan karşılığı olmayıp yazar tezi olarak işlenecek başlıkları açıkça işaretle.
  - **`SeriesPlanPrompt` & `SeriesPlanConsumer.cs` (`DOK2 §3`, `DOK4 Faz 3`):**
    - Yanıltıcı `"CalismaBasligi": "Bölüm 1"` şablon örneğini kaldır; jenerik `"Bölüm 1"`, `"Video 1"` gibi başlıkları kesin olarak yasakla!
    - Her bölüm için: çarpıcı `CalismaBasligi`, `Kanca`, `HedefSureDk`, en az 3–4 cümlelik derin `AnaFikir`, açıklamalı `Konular`, 4–8 maddelik `SahneIskeleti` (`SahneNo`, `Baslik`, `TahminiSureDk`, `AciklamaVeArgumanlar`) ve eğitimdeki kaynak kartlarından eşleştirilen `AtananKaynakIdleri` üretilmesini zorunlu kıl.
    - **Fark-Bazlı (Diff-Aware) Taslak Güncelleme:** `SeriesPlanConsumer` içinde mevcut `seriPlani.SeriBolumler`'i silmeden önce JSON olarak serileştir (`previousPlanJson`) ve `KullaniciKisitlariJson` (`SeriesDraftDto` olarak parse edilmiş okunaklı metin halinde!) + `request.EkTonTalimati` ile birlikte `GenerateSeriesPlanAsync`'e gönder.
    - **Deterministik C# Kısıt Zorlayıcı (`EnforcePlanConstraintsAsync`):** Kullanıcı `SeriesDraftDto.VideoSayisi` (ör. `2`) vermişse ve LLM farklı sayıda bölüm döndürmüşse, hatayı belirterek 1 düzeltme çağrısı yap; yine uymazsa C# tarafında fazla bölümleri birleştirerek veya bölerek tam `VideoSayisi` sayısına sabitle! `VarsayilanSureDk` (ör. `20`) ve `VideoSureleri` sözlüğünü her bölümün `HedefSureDk` alanına kesin olarak uygula.
  - **AgentWrite Tipi Sahne-Sahne Derin Senaryo Motoru (`GenerateSeriesEpisodeAsync` & `SeriesVideoGenerationConsumer.cs`, `DOK3 §2.2`, `DOK4 Faz 4`):**
    - Sabit `"10-15 Dakika"` ezmesini ve sahte `DevirNotu = "... işlendi"` satırını tamamen kaldır!
    - `SeriesVideoGenerationConsumer` içinde bölümün `AtananKaynaklarJson` listesindeki kaynak ID'lerini parse edip `_sourceContextBuilder.BuildContextAsync(..., maxChars: 130000, preferredSourceIds: assignedIds)` ile tüm atanmış kaynaklardan derin bağlam çek. `request.Konu`, `request.EkTonTalimati`, `seriPlani.OnayTalimati` ve önceki bölümün `DevirNotuJson` defterini `SeriesEpisodeGenerationRequest` içine koy.
    - `GenerateSeriesEpisodeAsync` içinde:
      1. **Aşama 1:** `[K#]` kaynakları + `[YAZAR-TEZİ]` omurgasıyla kapsamlı `ArastirmaOzeti` ve `HedefSureDk` ile tam uyumlu sahne planını ($\sum \Delta t_i = \text{HedefSureDk}$, sahne sayısı $S = \max(5, \lceil \text{HedefSureDk}/3.0 \rceil)$) üret.
      2. **Aşama 2:** `HedefSureDk <= 8` ise tek geçişte, `> 8` ise **2'şer sahnelik gruplar halinde iteratif LLM çağrılarıyla** her sahneyi yaz! Her sahnede: `**Zaman Aralığı:**`, `### 1. [Görsel & Edit Yönergesi]` (Kamera, B-Roll, On-Screen Text, Ses) ve `### 2. [Prompter Metni (Aynen Okunacak)]` (dakika başı ~135–150 kelime hedefiyle her sahnede en az 3–5 dolu paragraf; düşünürlerin görüşlerini ismen geçiştirmeden nedenleri, örnekleri ve yazar teziyle bağlantısını kurarak anlatan gerçek stüdyo konuşma metni) üret. Her adımda `onProgress` callback'i ile SignalR ilerlemesi yayınla.
      3. **Aşama 3:** Üretilen bölümden gerçek `EpisodeContinuityLedgerDto` JSON devir defterini (`IslenenDusunurlerVeKavramlar`, `VerilenSomutOrnekler`, `AcikBirakilanSorular`, `SonCumleGecisNotu`) çıkarıp döndür.
    - `GenerateContentConsumer.cs` (Klasik Mod) içinde de `HedefUzunluk` değerine göre gerçek süre/kelime ölçeklemesini ve `UretimSuresiMs` ölçümünü (`Stopwatch`) aktif et.
- **VeriScore Tipi Çift Katmanlı Kalite Kontrol (`QualityCheckService.cs`, `QualityCheckConsumer.cs`, `DOK3 §2.4`, `DOK4 Faz 5`):**
  - `BatchQcPrompt` ve `ReQcVerificationPrompt` şablonlarını güncelle: İddiaları `"tip": "olgusal_kaynak_iddiasi"` veya `"tip": "yazar_tezi_yorumu"` olarak sınıflandır.
  - Kullanıcının `[YAZAR-TEZİ]` (`authorThesisContext`) kapsamındaki kavramsal yorumları, felsefi çerçevesi ve pedagojik sentezleri **asla `"desteklenmedi"` olarak işaretlenmez** ve Auto-Refine tarafından **kesinlikle silinmez**!
  - Yalnızca kaynakta bulunmayan uydurma sayısal oranlar (`%`), hayali deney/anketler ve yanlış düşünür atıfları `"desteklenmedi"` sayılır ve Auto-Refine yalnız o uydurma olgusal ifadeleri temizler.

### 4. `series-api-rev` (`gemini-4-argon-high`) Ayrıntılı Görevleri:
- **`SeriesController.cs` (`DOK2 §3`, `DOK4 Faz 3 & Faz 5`):**
  - `GetContentPlan` yanıtına her bölümün yeni `Kanca`, `SahneIskeletiJson`, `AtananKaynaklarJson` alanlarını, `OnayTalimati` alanını ve eğitime ait toplam kaynak özet listesini (`EgitimKaynaklari`: ID, başlık, tür, kullanım modu) ekle.
  - `UpdatePlanTexts` (`PUT api/content-plans/{id}/plans/{planNo}`) içindeki sahte `SeriHaritasiJson` kaydını gerçek `UpdatePlanDto` işlemcisine dönüştür: Gelen bölüm listesine göre `SeriBolumler` tablosundaki başlık, kanca, süre, ana fikir, konular, sahne iskeleti ve atanan kaynakları güncelle, yeni eklenen bölümleri ekle, silinenleri kaldır, `BolumNo` sıralamasını `1..N` yeniden numaralandır ve `plan.VideoSayisi` değerini güncelle!
  - `ApprovePlan` içinde `plan.KullaniciKisitlariJson += "\n" + talimat` diyerek JSON bozan satırı kaldır; onay direktifini yeni `plan.OnayTalimati` sütununa temizce kaydet.
  - `GetEpisodeDetails` içinde her revizyon için `KullanilanKaynaklar` GUID listesini çözümleyerek (`ContentRequestsController` içindeki mantıkla uyumlu şekilde) `KullanilanKaynakDetaylari` (`KanitKodu`, `KaynakTipi`, `KaynakBaslik`, `BolumBasligi`, `ZamanAraligi`, `KaynakDil`, `KullanimTuru`) dizisini döndür!
  - Bölüm revizyonları için `POST api/series-requests/{id:guid}/videos/{bolumNo:int}/revisions/{revizyonNo:int}/re-qc` uç noktasını ekle (`IQualityCheckService` ile gerçek yeniden denetim yapıp revizyon kaydını güncellesin).
- **`ContentRequestsController.cs`:**
  - `ContentRequestDetailsController` içindeki eski kopyala-yapıştır `ExecuteQualityCheckInternal` metodunu `IQualityCheckService` (`authorThesisContext: request.Konu`) kullanacak şekilde birleştir ve `LoadUsedSourceChunkDetailsAsync` içinde güncel `video/dokuman.KullanimTuru` değerini baz al.
- **`SeriesVideoRevisionConsumer.cs` (`DOK2 §4.4`, `DOK4 Faz 5`):**
  - Eski 24 chunk'a hapsolma hatasını düzelt: Revizyon geldiğinde her zaman `[msg.Talimat, bolum.CalismaBasligi, bolum.AnaFikir, ...konular]` sorgularıyla `_sourceContextBuilder.BuildContextAsync` çalıştır (`preferredSourceIds`: bölümün `AtananKaynaklarJson` listesi) ve önceki kullanılan chunk'larla birleştirerek zengin bağlam oluştur.
  - `ReviseContentAsync` çağrılarına `[YAZAR-TEZİ]: {request.Konu} {request.EkTonTalimati} {seriPlani.OnayTalimati}` bilgisini ekle.
  - Re-QC çağrısında unutulan `videoPlanText: yeniPlan` ve `authorThesisContext` parametrelerini eksiksiz geçir.
  - `catch` bloğundaki sahte `durum = "desteklendi"` JSON kaydını tamamen kaldır; gerçek hata açıklamasıyla `"belirsiz"` yaz.
  - Revizyon tamamlandığında yeni içeriğe göre `DevirNotuJson`'ı güncelle ve aynı plandaki `b.BolumNo > bolum.BolumNo` olan tamamlanmış bölümlerde `b.BaglamEskidi = true` ve `b.BaglamSorunlariJson` uyarısını kaydet!

### 5. `angular-ui` (`gemini-4-argon-high`) Ayrıntılı Görevleri:
- **`settings.service.ts`, `settings-modal.component.ts` & `navbar.component.ts` (`DOK2 §1`, `DOK4 Faz 1`):**
  - `AiProviderSettings` arayüzündeki Yerel Proxy görev modeli alanlarını backend ile tam uyumlu `antigravityProxySummaryModel`, `antigravityProxyVideoPlanModel`, `antigravityProxyQcModel` (ve geriye uyumluluk okuması) ile eşitle; `allowFallbackToSecondaryProvider: boolean` seçeneğini modala ("Ana model hata verirse yedek sağlayıcıya geçilmesine izin ver — kapalıyken seçtiğiniz model kilitlenir") olarak ekle.
  - `navbar.component.ts` içindeki rozette aktif **Plan/Senaryo Modelini** belirgin göster (farklı özet modeli seçiliyse yanına küçük etiketle belirt).
- **`series-planner.component.ts` & `.html` (`DOK4 Faz 6.1`):**
  - Sol panele **Kaynak Kapsam Özeti** (`Atanan / Toplam Kaynak` rozeti ve hiçbir bölüme atanmayan kaynaklar varsa uyarı listesi) ekle.
  - `Beklenen Konular` etiketlerini tıklanabilir yap: Tıkladıkça `Normal → Zorunlu Tut (Yeşil) → Hariç Tut (Kırmızı)` durumları arasında geçiş yapsın ve `draftOptions.zorunluKonular` / `draftOptions.haricKonular` olarak `generatePlanDraft` çağrısına gönderilsin.
  - API'den gelen ama gizlenen `kaynaktaOlmayanlar` (*"Sizin Teziniz Olarak İşlenecek / Kaynaklarda Doğrudan Geçmeyen Başlıklar"*) ve `disaridaBirakilanlar` listelerini sol panelde şık bilgi kutularıyla göster.
- **`series-map.component.ts` & `.html` (`DOK4 Faz 6.2`):**
  - Kartlarda jenerik görünüm yerine: **Çalışma Başlığı**, **Hedef Süre (`~X dk`)**, **Açılış Kancası (`Kanca`)**, **Ana Fikir**, **Sahne Kırılımı (`SahneIskeletiJson`)** ve **Atanan Kaynak Sayısı / İsimleri** gösterilsin.
  - Plan onaylanmadan önce (`!readonly`) kart üzerinde **"✏️ Düzenle"** modu, **"+ Yeni Bölüm Ekle"**, **"🗑️ Bölümü Sil"** ve sıra kaydırma butonları ekle; değişiklik yapıldığında `apiService.updatePlanTexts(contentRequestId, plan.planNo, payload)` ile veritabanına kaydet ve haritayı yenile!
  - Onaylanmış planda `b.baglamEskidi === true` ise kart üzerinde sarı **"⚠️ Önceki Bölüm Değişti (Bağlam Eskidi)"** rozeti ve hızlı yenileme butonu göster.
- **`episode-viewer.component.ts/.html`, `plan-viewer.component.ts/.html`, `api.service.ts` & `markdown-docx.ts` (`DOK4 Faz 6.3`):**
  - `EpisodeViewerComponent` içinde varsayılan `hedefAlan = 'Hepsi'` yap (kullanıcı isterse yine tek alanı seçebilsin).
  - `ApiService` içine `reRunEpisodeQc(id, bolumNo, revizyonNo)` metodunu ekle.
  - `PlanViewerComponent` içine `Kanıtlar ([K#])` sekmesini (`revision.kullanilanKaynakDetaylari` tablosu: Kanıt Kodu, Kaynak Başlığı, Tür, Konum/Zaman Aralığı, Dil, Kullanım Modu) ve QC sekmesine **"🔄 Kalite Kontrolü Yeniden Çalıştır"** butonunu ekle.
  - `markdown-docx.ts` içindeki `extractPrompterScenes` fonksiyonunu hem `## Sahne 1`, hem `### GİRİŞ`, hem de numaralı `### 1.1 ...` / `#### ...` sahne başlıklarını ve `**Prompter Metni:**` bloklarını hiç kaçırmadan ayrıştıracak şekilde güçlendir.

---

## DALGA 3 — Bağımsız Denetim, Birim Testler ve Canlı Derleme (`qa-verifier` & `build-runner`)

1. **`build-runner` (`gemini-4-hydrogen-medium`):**
   - `dotnet build VideoOzet.slnx` çalıştır; varsa derleme hatalarını dosya sahibine `@mention` ile bildir.
   - EF Core migration'ı kontrol et / veritabanına uygula.
   - `src/VideoOzet.UI` dizininde `npm run build` çalıştır; Angular template ve TypeScript derlemesinin sıfır hatayla geçtiğini doğrula.
2. **`qa-verifier` (`gemini-4-carbon-high`):**
   - `DOK1` içindeki `KK-1`..`KK-7` kabul kriterlerini ve `PROJE_GENEL_DENETIM_VE_MIMARI_ONERILER.md` içindeki 12 sahte/ölü özelliğin tamamının gerçekten bağlandığını kod üzerinden tek tek denetle.
   - `tests/` altında şu otomatik testleri yaz ve `build-runner` üzerinden `dotnet test` ile çalıştırt:
     1. `AiProviderSettingsTests`: `AntigravityProxyVideoPlanModel` / `antigravityVideoPlanModel` çift yönlü kayıt/okuma ve `AllowFallbackToSecondaryProvider == false` iken sessiz Groq fallback'in engellenmesi.
     2. `SourceContextQuotaTests`: 40+ kaynaklı eğitimde `preferredSourceIds` verildiğinde atanan her kaynaktan en az 1 parça seçilmesi (`Recall@AssignedSource = %100`) ve dinamik `Mod:` başlığında çelişki olmaması.
     3. `SeriesPlanConstraintTests`: `VideoSayisi = 2`, `VarsayilanSureDk = 20` ve önceki planla fark-bazlı revizyonun deterministik olarak uygulanması; `"Bölüm 1"` jenerik başlığının reddedilmesi.
     4. `VeriScoreQualityCheckTests`: `[YAZAR-TEZİ]` argümanlarının Auto-Refine tarafından silinmemesi, buna karşılık kaynakta geçmeyen uydurma `%37` istatistiğinin yakalanması.

---

# ⚠️ 4. UYULMASI ZORUNLU KURALLAR VE PRENSİPLER

1. **Sıfır Dosya Çakışması Prensibi:** İki ajan **asla** aynı dosyayı düzenleyemez. Bir ajan başka bir ajanın dosyasındaki bir metoda ihtiyaç duyarsa panodan (`@ajan-adı`) o değişikliği talep eder.
2. **Sahte / Yarım Kod Yasağı (`Zero Mock / Zero Dead Code`):** `"Basitlik adına mevcut tasarımı koruyoruz"`, `"Bölüm 1 işlendi"`, sabit `"10-15 Dakika"`, sabit `"gemini-1.5-flash"` veya hata bloğunda sahte `"desteklendi"` gibi hiçbir yer tutucu/sahte mantık bırakılmayacaktır.
3. **Şeffaf İlerleme ve `[done]` Protokolü:** Her ajan görevini bitirdiğinde değiştirdiği dosyaları ve doğrulama özetini `@coordinator [done] ...` formatıyla panoya yazar; ara mesajlarda gereksiz `@all` kullanılmaz.
4. **Tam Kapanış Şartı (`/goal`):**
   - `dotnet build VideoOzet.slnx` → **0 Error**,
   - `dotnet test` (yeni testler dahil) → **%100 Pass**,
   - `npm run build` (`VideoOzet.UI`) → **0 Error**
   olmadan görev tamamlanmış sayılmaz! Tüm kontroller geçtikten sonra koordinatör olarak yapılan tüm değişikliklerin özetini ve doğrulama sonuçlarını bana raporla.
