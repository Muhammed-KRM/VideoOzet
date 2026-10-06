# VideoÖzet - Kalan Sorunlar, Teşhis ve Çözüm Yol Haritası

Bu döküman, VideoÖzet platformunda yapılan derinlemesine veritabanı, kod ve altyapı teşhisleri sonucunda tespit edilen eksiklikleri, kök nedenlerini ve adım adım çözüm planını içermektedir.

---

## 1. Yönetici Özeti ve Mevcut Durum Teşhisi

Bugün yapılan geliştirmelerle **Çoklu Video Serisi** akışında Kalite Kontrol (QC) motoru başarıyla ayağa kaldırılmış, doküman bağlamı (`SourceContextBuilder`) üzerinden %62.50 güven skoruyla çalışan gerçek iddia doğrulama sistemi doğrulanmıştır.

Bununla birlikte, platform genelindeki **Vektör İndeksleme** ve **Video İşleme (STT)** katmanlarında yapılan adli tıp sorgulamalarında 5 kritik kök sorun saptanmıştır:

| Bileşen | Durum / Teşhis | Kök Neden | Etki |
| :--- | :--- | :--- | :--- |
| **Video Transkriptleri (STT)** | **218 videonun 218'inde de sahte metin var.** | `OpenAIWhisperProvider.cs` içinde `OPENAI_API_KEY` olmayınca mock metin dönülmesi. | Videoların gerçek konuşma içeriği veritabanında hiç yok. |
| **Vektör Veritabanı (pgvector)** | **536 chunk'ın 536'sı da sıfır vektör `[0,0,0...]`.** | `OpenAIEmbeddingProvider.cs` içinde API key olmayınca 1536 adet sıfır dönülmesi. | pgvector kosinüs benzerliği `NaN` veriyor, aramalarda 0 satır dönüyor. |
| **Embedding Boyut Uyuşmazlığı** | **DB: `vector(1536)` vs Ollama `bge-m3`: 1024.** | Veritabanı şeması OpenAI 1536 boyutuna sabitlenmiş. | `bge-m3` vektörleri DB'ye yazılmaya çalışıldığında boyut hatası riski. |
| **Doküman Karakter Kodlaması** | **Bazı PDF dokümanlarında bozuk Türkçe karakterler.** | Eski PDF yazı tiplerinin ANSI/Unicode eşlemesi (`ø, Õ, ú, ÷`). | Vektör kalitesini ve metin akışını olumsuz etkiliyor. |
| **Eski Seri Bölümleri** | **Bölüm 1 ve Bölüm 2 (Rev 1) QC'siz (0).** | QC motoru eklenmeden önce üretilmiş olmaları. | Kullanıcı arayüzünde eski bölümlerde %0 görünmesi. |

---

## 2. Sorunların Ayrıntılı Kök Neden Analizi

### Sorun 1: Videolarda Gerçek Transkript Bulunmaması (STT Açığı)
* **Kaynak Kod:** `src/VideoOzet.Business/Infrastructure/AI/OpenAIWhisperProvider.cs` (Satır 38-42)
  ```csharp
  if (string.IsNullOrEmpty(_apiKey))
  {
      _logger.LogWarning("OpenAI ApiKey is not configured. Returning mock transcription for development/testing.");
      return "Bu eğitim videosunda temel kavramlar, metodoloji ve uygulama örnekleri ele alınmaktadır. İlgili konularda detaylı analizler yapılmış ve pratik bilgiler sunulmuştur.";
  }
  ```
* **Gerçekleşen Durum:** Sistemde OpenAI API anahtarı tanımlanmadığı için yüklenen her video doğrudan bu şablon metne dönüştürülmüş; ardından bu şablon Gemini'ye özetletilmiş ve veritabanına bu şekilde girmiştir.
* **Mevcut Kaynak:** Videoların orijinal dosyaları MinIO depolama alanında (`videolar` bucket) mevcuttur; yani fiziksel veri kaybı yoktur, sadece sesten metne döküm (transcription) çalıştırılmamıştır.

### Sorun 2: Vektör Boyut Uyuşmazlığı (`vector(1536)` vs `1024`)
* **Veritabanı Tanımı:** `VideoChunkDocumentConfiguration.cs` içinde:
  ```csharp
  builder.Property(x => x.Embedding)
         .HasColumnType("vector(1536)");
  ```
* **Ollama Durumu:** Docker konteynerine çektiğimiz `bge-m3` modeli **1024 boyutlu**, `nomic-embed-text` modeli ise **768 boyutlu** vektör üretmektedir.
* **Risk:** Eğer `VideoChunkDocument` tablosuna 1024 boyutlu vektör doğrudan yazılmaya çalışılırsa PostgreSQL `ERROR: different vector dimensions 1024 and 1536` hatası fırlatır.

### Sorun 3: 536 Adet Sıfır Vektörlü Chunk
* Veritabanında şu an bulunan 536 chunk'ın (291 doküman, 245 video özeti) tüm embedding değerleri `[0,0,0,0...]` şeklindedir.
* `SourceContextBuilder` küçük eğitimlerde metinleri doğrudan birleştirdiği için bu durum dokümanlarda tolere edilmiş; ancak büyük eğitimlerde RAG araması yapıldığında pgvector sonuç üretemez.

### Sorun 4: Doküman Metinlerindeki Karakter Bozulmaları
* `dokuman_metinleri` tablosundaki bazı akademik PDF'lerden çıkarılan metinlerde şu harf bozulmaları mevcuttur:
  - `ø` -> `i` / `ı` (örn: `ÜNøTE` -> `ÜNİTE`)
  - `Õ` -> `ı` (örn: `ortaya konmuú` -> `ortaya konmuş`)
  - `ú` -> `ş`
  - `÷` -> `ğ` (örn: `ne do÷ru` -> `ne doğru`)
* Bu bozulmalar LLM'in kavram algısını zayıflatmakta ve arama eşleşmelerini bozmaktadır.

---

## 3. Adım Adım Çözüm Yol Haritası

```
+-----------------------------------------------------------------------------------+
|                            ÇÖZÜM MİMARİSİ AKIŞI                                  |
+-----------------------------------------------------------------------------------+
|  [Adım 1: Gemini Audio STT]   --> Videoların MP3 sesleri Gemini 3.8 Flash'a        |
|                                   gönderilir, gerçek Türkçe transkriptler alınır.  |
|                                                                                   |
|  [Adım 2: pgvector Şeması]     --> DB kolonu vector(1024)'e güncellenir            |
|                                   (Ollama BGE-M3 ile tam uyumlu hale getirilir).   |
|                                                                                   |
|  [Adım 3: Text Normalizer]     --> PDF çıkarımına Türkçe harf onarım filtresi      |
|                                   eklenir (ø, Õ, ú, ÷ temizliği).                 |
|                                                                                   |
|  [Adım 4: Toplu Yenileme]      --> Mevcut 218 video ve doküman arka planda         |
|                                   gerçek ses & vektörlerle yeniden işlenir.       |
|                                                                                   |
|  [Adım 5: Eski Bölümler QC]    --> Bölüm 1 ve önceki revizyonlar için QC           |
|                                   çalıştırılıp arayüz %100 senkronize edilir.     |
+-----------------------------------------------------------------------------------+
```

---

### Adım 1: Gemini Audio STT Sağlayıcısının Yazılması (OpenAI Bağımlılığını Kaldırma)

Sistemde zaten geçerli bir `GEMINI_API_KEY` bulunmaktadır ve Gemini 1.5/2.0/3.8 Flash modelleri **doğrudan ses dosyalarını (mp3, wav) dinleyip metne dökebilmektedir**. Harici bir OpenAI aboneliğine gerek kalmaksızın gerçek transkripsiyon bu sağlayıcı ile çözülebilir.

#### Uygulama Detayları:
1. `src/VideoOzet.Business/Infrastructure/AI/GeminiAudioSttProvider.cs` sınıfı oluşturulur.
2. `ISttProvider` arayüzünü implemente eder.
3. Videodan FFmpeg ile çıkarılan `.mp3` dosyasını `antigravity-manager` proxy'si (`localhost:8045`) üzerinden veya doğrudan Google API üzerinden `audio/mp3` formatında Gemini'ye iletir.
4. Prompt olarak konuşmacının söylediklerini harfiyen transkribe etmesi talimatı verilir:
   ```text
   Sen profesyonel bir ses döküm (transkripsiyon) uzmanısın.
   Verilen Türkçe ses kaydındaki tüm konuşmaları hiçbir kelime atlamadan,
   zaman sırasına sadık kalarak harfiyen yazıya dök.
   ```
5. `ServiceRegistration.cs` içinde:
   - Eğer `OPENAI_API_KEY` varsa -> `OpenAIWhisperProvider`
   - Yoksa -> `GeminiAudioSttProvider`
   otomatik olarak seçilir.

---

### Adım 2: pgvector Şemasının `vector(1024)` / Dinamik Yapıya Güncellenmesi

Ollama üzerindeki `bge-m3` modeli 1024 boyutlu vektör üretmektedir. PostgreSQL'deki `vector(1536)` kısıtlaması güncellenmelidir.

#### Uygulama Detayları:
1. `VideoChunkDocumentConfiguration.cs` içinde kolon tipi `vector(1024)` olarak güncellenir:
   ```csharp
   builder.Property(x => x.Embedding)
          .HasColumnType("vector(1024)");
   ```
2. EF Core Migration oluşturulup veritabanına uygulanır:
   ```bash
   dotnet ef migrations add UpdateVectorDimensionTo1024 --project src/VideoOzet.Data --startup-project src/VideoOzet.API
   dotnet ef database update --project src/VideoOzet.Data --startup-project src/VideoOzet.API
   ```
3. PostgreSQL üzerinde ALTER komutu ile kolon dönüştürülür:
   ```sql
   ALTER TABLE video_chunk_documents ALTER COLUMN embedding TYPE vector(1024);
   ```

---

### Adım 3: PDF Metin Çıkarımında Türkçe Karakter Temizliği (Text Normalizer)

Eski akademik PDF'lerden ve taranmış belgelerden kaynaklanan karakter bozukluklarını gidermek için `TextNormalizer` modülü eklenir.

#### Değişim Tablosu:
| Bozuk Karakter | Doğru Karakter | Örnek |
| :---: | :---: | :--- |
| `ø` / `Ø` | `i` / `İ` | `ÜNøTE` -> `ÜNİTE` |
| `Õ` | `ı` | `ayÕrt` -> `ayırt` |
| `ú` | `ş` | `konmuú` -> `konmuş` |
| `÷` | `ğ` | `do÷ru` -> `doğru` |
| `Ý` | `İ` | `Ýstanbul` -> `İstanbul` |
| `Þ` | `Ş` | `Þart` -> `Şart` |

#### Uygulama:
`ExtractDokumanTextConsumer.cs` dosyasında `PdfPig` metni çıkarıldıktan hemen sonra `TextNormalizer.NormalizeTurkishText(rawText)` fonksiyonu çağrılır.

---

### Adım 4: Mevcut Videoların Toplu Yeniden İşlenmesi (Batch Re-Process)

Veritabanında bulunan 218 videonun orijinal dosyaları MinIO'da saklandığı için hiçbir dosya kaybı yoktur. Bu videoları otomatik olarak sırayla gerçek transkripte ve özete kavuşturacak bir arka plan komutu eklenir.

#### Uygulama Detayları:
1. `VideolarController.cs` altına yönetici endpoint'i eklenir:
   ```http
   POST /api/egitimler/{egitimId}/videolar/reprocess-all
   ```
2. Bu endpoint, belirtilen eğitimdeki mock metne sahip tüm videolar için kuyruğa `VideoUploadedEvent` fırlatır.
3. Worker sırayla:
   - MinIO'dan videoyu çeker.
   - FFmpeg ile sesini çıkarır.
   - `GeminiAudioSttProvider` ile gerçek metne dönüştürür.
   - Gemini ile özetini çıkarır.
   - Ollama `bge-m3` ile 1024 boyutlu gerçek vektörünü oluşturup pgvector'e kaydeder.

---

### Adım 5: Eski Seri Bölümlerinin QC Raporlarının Tamamlanması

Mevcut serideki Bölüm 1 (`Hukukun Ontolojik Temelleri ve Adalet Felsefesi`) daha önce üretildiği için QC skorları 0 kalmıştır.

#### Uygulama Detayları:
1. `POST /api/series-requests/82682be6-40d2-44e9-a67e-337e119acdb0/videos/1/revisions` çağrılarak Bölüm 1 için de ReQC çalıştırılır.
2. Veya tek seferlik bir migrasyon scripti ile `ToplamIddiaSayisi = 0` olan bölümler arka planda `QualityCheckService` üzerinden geçirilip güncellenir.
3. Böylece arayüzde tüm bölümler yeşil/doğrulanmış kartlarla görüntülenir.

---

## 4. Uygulama Sırası ve Öncelik Tablosu

| Öncelik | Görev | Hedef Dosyalar | Beklenen Çıktı |
| :---: | :--- | :--- | :--- |
| **P1** | **Gemini Audio STT Entegrasyonu** | `GeminiAudioSttProvider.cs`, `ServiceRegistration.cs` | Videoların gerçek konuşmaları Türkçe metin olarak elde edilir. |
| **P2** | **Vektör Kolon Boyutu (1024)** | `VideoChunkDocumentConfiguration.cs`, EF Migration | Ollama `bge-m3` vektörleri DB'ye hatasız yazılır. |
| **P3** | **PDF Metin Karakter Düzeltici** | `TextNormalizer.cs`, `ExtractDokumanTextConsumer.cs` | `ø, Õ, ú, ÷` bozuklukları temizlenir, doküman kalitesi artar. |
| **P4** | **Bölüm 1'in QC'sinin Alınması** | API / Worker | Episode Viewer'da Bölüm 1 de %60-%80 doğruluk raporu kazanır. |
| **P5** | **Eski Videoların Yeniden İşlenmesi** | `VideolarController.cs` (Batch Endpoint) | 218 videonun tamamı sahte metinden kurtarılıp gerçek içerikle dolar. |

---

## 5. Doğrulama Kriterleri (Acceptance Criteria)

1. **Ses Transkripsiyonu:** Bir test videosu yüklendiğinde `video_transcripts.ham_metin` alanında mock metin yerine konuşmacının gerçek cümleleri yer almalı.
2. **Vektör İndeksleme:** `video_chunk_documents` tablosundaki yeni satırlarda `vector_norm(embedding) > 0` ve `vector_dims(embedding) = 1024` olmalı.
3. **Doküman Bütünlüğü:** Yüklenen PDF metinlerinde `ø, Õ` gibi bozuk karakterler sıfıra inmeli.
4. **UI Gösterimi:** `localhost:4201/episode-viewer/...` sayfasında hem Bölüm 1 hem Bölüm 2 için Güven Skoru > %0 ve detaylı iddia listesi eksiksiz listelenmeli.
