# DOK3: Endüstri, Benzer Projeler ve Literatür Araştırması (Başkaları Nasıl Yapmış?)

> **4'lü Mimari Dokümantasyon Seti — Navigasyon:**
> - **DOK1:** [DOK1_HEDEF_VE_GEREKSINIMLER.md](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK1_HEDEF_VE_GEREKSINIMLER.md) — *Hedef, Kapsam, İş Paketleri ve Kabul Kriterleri*
> - **DOK2:** [DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) — *Mevcut Kod Debug ve Akış Raporu (Şu Anki Sistem Nasıl Çalışıyor?)*
> - **DOK3 (Bu Dosya):** [DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md) — *Endüstri, Benzer Projeler ve Literatür Araştırması*
> - **DOK4:** [DOK4_ADIM_ADIM_GELISTIRME_PLANI.md](file:///d:/Hoca/VideoOzet/docs/ham-metin-rag-ve-kalite-mimarisi/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md) — *Adım Adım Geliştirme Planı (Nasıl Geliştireceğiz?)*

---

## 1. Endüstri Probleminin Özeti

VideoÖzet AI'nin çözmeye çalıştığı problem, modern bilgi-yoğun yapay zeka sistemlerinin **4 çekirdek mühendislik problemine** birebir karşılık gelmektedir:
1. **Çok uzun heterojen kaynaklarda (~1,5M+ karakter PDF + saatlik ses transkripti) bağlam penceresini taşırmadan yüksek ayrıntıyı koruma,**
2. **Özel isimler, felsefi/hukuki terimler ve çok dilli (TR/EN) içeriklerde doğru geri çağırma (`Recall`) ve kaynak dengesi sağlama,**
3. **Dar TPM/token limitli modellerde dahi kesme (`truncation`) yapmadan derin stüdyo metni üretebilme,**
4. **Üretilen metindeki her iddianın ve özellikle sayısal verinin halüsinasyonsuz biçimde kaynaklara dayandırılması (`Faithfulness / Groundedness`).**

Aşağıda endüstride ve akademik literatürde bu 4 problemi çözen öncü yaklaşımlar incelenmiştir.

---

## 2. Endüstri Liderleri ve Akademik Yaklaşımlar

### 2.1. Anthropic Contextual Retrieval (2024) — Bağlamsal Parçalama + Hibrit RRF
Geleneksel RAG sistemlerinde bir doküman `500` kelimelik parçalara bölündüğünde, aradaki bir parça (*ör. "Bu deneyde dopamin artışı yerine seçici dikkat mekanizması ölçülmüştür"*) hangi yazara, makaleye veya alt başlığa ait olduğunu kaybeder. Ayrıca yoğun vektör aramaları (*dense embeddings*) *"Gazzâlî", "Mâtürîdî", "hypernudge"* gibi kesin özel isimlerde ve nadir terimlerde beklenenden düşük skor üretebilir.

Anthropic'in kıyaslama çalışmalarında gösterdiği çözüm 3 bileşenlidir:
1. **Contextual Chunk Headers (Bağlamsal Parça Üstbilgisi):** Her parça embedding'e ve LLM'e verilmeden önce başına kaynak adı, bölüm başlığı/zaman aralığı, dil ve kullanım modu eklenir (örn. `[Kaynak: Robert Sapolsky - Determined | Bölüm: Biyolojik Determinizm | Dil: TR]`). Bu sayede hem vektör uzayında hem okuma aşamasında bağlam kopmaz.
2. **Hibrit Arama (`Dense Vector + BM25/Lexical`):** Anlamsal vektör aramasının yanına sözcüksel tam/kısmi eşleşme konur. PostgreSQL ekosisteminde bu rolü ek bir Elasticsearch/Qdrant sunucusu kurmadan yerleşik **`tsvector` (GIN)** ve Türkçe ekler/özel isim benzerliği için **`pg_trgm`** modülü üstlenir.
3. **Reciprocal Rank Fusion (RRF):** Farklı ölçeklerdeki vektör benzerliği ile anahtar kelime skorunu normalize etmek için sıra tabanlı birleştirme kullanılır:

$$RRF(d) = \sum_{m \in \{\text{dense}, \text{lexical}\}} \frac{w_m}{k + r_m(d)}$$

Burada $k = 60$ sabittir, $r_m(d)$ ise $d$ parçasının $m$ yöntemindeki sıralamasıdır. Anthropic bu yapıyla geri çağırma başarısızlık oranını (retrieval failure rate) **`%49`**, yeniden sıralamayla birlikte **`%67`** oranında düşürmüştür.

---

### 2.2. Google NotebookLM & Perplexity Pro — Kanıt-Bağlı Sentez ve Kapalı Dünya Varsayımı
Google NotebookLM ve benzeri kaynak-odaklı araştırma araçları (`40–50+` PDF/video kaynağı aynı not defterinde işleyebilen sistemler), kullanıcıya asla kaynaktan kopuk genel ansiklopedik bilgi sunmamak için şu mimari ilkeleri uygular:
- **Numaralı Kanıt Blokları (`[K1]`, `[K2]`, ...):** RAG'dan dönen parçalar LLM'e rastgele metin yığını olarak değil, kalıcı indeks numaralarıyla verilir ve çıktıdaki her kritik iddianın sonuna satır içi atıf (`[K3]`, `[K7]`) konması istenir.
- **Closed-World Assumption (Kapalı Dünya Varsayımı):** *"Verilen kaynaklarda bulunmayan hiçbir istatistiği, oranı, yılı veya harici deney adını eğitim verinden tamamlama"* kuralı uygulanır.
- **Explicit Abstention (Açıkça Eksik Bildirimi):** Kullanıcının istediği bir alt başlık kaynaklarda geçmiyorsa model boşluğu uydurma bilgiyle doldurmak yerine *"Bu alt başlık yüklenen kaynaklarda doğrudan desteklenmemektedir"* beyanında bulunur.

---

### 2.3. LlamaIndex & LangChain — Hiyerarşik İndeksleme (Özet Kartı vs. Ham Pencere) ve Çoklu Alt-Sorgu
LlamaIndex ve LangChain mimarilerinde **"Özet İndeksleme"** ile **"Ham Metin İndeksleme"** birbirinin alternatifi değil, farklı iki işin uzmanıdır:
- **Document Summary / Topic Index (Kaynak Kimlik Kartı):** Yalnız *"Hangi kaynak ne anlatıyor? Seri planı nasıl bölünmeli?"* soruları için kullanılır (VideoÖzet'te [SourceTopicMapper.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.Business/Services/SourceTopicMapper.cs) bu işin temelini taşır).
- **Overlapping Sentence-Window / Full-Text Index:** Somut argümanları, deneyleri, vaka örneklerini ve video zaman damgalarını çekmek için **`450–600` kelimelik, `%10–20` örtüşmeli (`overlap`), cümle sınırını bozmayan ham metin parçaları** kullanılır.
- **Sub-Question / Multi-Query Decomposition:** Tekil 4.000 karakterlik bir kullanıcı taslağını tek vektöre sıkıştırmak bilgiyi bulanıklaştırır. LlamaIndex *SubQuestionQueryEngine* örüntüsü uzun talebi alt-sorgulara (`q1, q2, ..., qm`) böler ve her alt-sorgu için bağımsız arama yapıp sonuçları dengeli biçimde birleştirir.

---

### 2.4. Maximal Marginal Relevance (MMR) ve Kaynak Başına Kota (`Per-Source Cap`)
Çok kaynaklı eğitimlerde uzun bir hukuk/felsefe PDF'i veya 106 dakikalık bir ders transkripti 40–60 chunk üretirken başka bir yoğun makale 10 chunk üretebilir. Saf `Top-K CosineDistance` sıralaması tek bir uzun dosyanın tüm bağlamı ele geçirmesine yol açar (`Akbulut PDF` olayında olduğu gibi 6/10 chunk tek kaynaktan gelmiştir).

Carbonell & Goldstein (1998) tarafından geliştirilen **Maximal Marginal Relevance (MMR)** formülü hem sorguyla uyumu hem halihazırda seçilmiş parçalardan farklılığı enbüyükler:

$$\text{MMR} = \arg\max_{d_i \in R \setminus S} \left[ \lambda \cdot \text{Sim}_1(d_i, Q) - (1 - \lambda) \cdot \max_{d_j \in S} \text{Sim}_2(d_i, d_j) \right]$$

Buna ek olarak **Deterministik Kaynak Kotası (`MaxChunksPerSource`)** uygulanır: örneğin `>= 10` kaynaklı bir eğitimde hiçbir PDF/video toplam seçimin %20–25'inden (örn. `3` parçadan) fazlasını alamaz ve her alt-sorgunun en az `1–2` temsilci parçaya kavuşması garanti edilir.

---

### 2.5. Uzun Form İçerik Üretiminde Bölüm Bazlı Sentez (`Plan-and-Write / Sectioned Generation`)
Groq ücretsiz katmanı gibi `8K TPM` sınırına sahip modellerde `40.000` karakterlik bağlamı tek prompta sokup baş-son kırpması (`CompactPromptForGroq`) yapmak bağlamın %80–90'ını siler. Literatürdeki **Hierarchical Plan-and-Write (ve Map-Reduce Sectioned Synthesis)** çözümünde:
1. Geniş bağlam pencereli birincil modeller (Gemini 2.5/3.x, Claude Sonnet, GPT-4o vb.) tam bağlamı 2 adımda (`Araştırma Özeti` → `Video Planı`) işler.
2. Dar TPM/bağlam limitli yedek bir sağlayıcıya düşüldüğünde ise metin **kırpılmaz**; üretim alt-başlık/sahne gruplarına bölünür (`Sahne 1–3`, `Sahne 4–6`, ...). Her grup yalnız kendi ilgili `[K#]` parçalarını ve önceki bölümün 3–4 cümlelik akış özetini (*Rolling Summary*) alır. Böylece küçük modellerle bile `%0` bilgi kaybıyla uzun stüdyo çıktısı üretilir.

---

### 2.6. Kanıt Doğrulama ve Öz-Düzeltme (`FActScore`, `Ragas Faithfulness`, `Self-RAG`, `CRAG`)
Akademik literatürde **FActScore (Min et al., 2023)** ve **Ragas Faithfulness** metrikleri üretilen metni tek seferde "iyi/kötü" demek yerine **atomik iddialara (atomic claims)** ayırır:

$$\text{Faithfulness} = \frac{|C_{\text{supported}}|}{|C_{\text{total}}|}$$

Ancak tek başına puanlamak halüsinasyonu kullanıcıya göstermeyi engellemez. **CRAG (Corrective RAG)** ve **Self-RAG (Asai et al., 2023)** sistemleri şu güvenlik döngüsünü uygular:
1. **Deterministik Sayı/İstatistik Kontrolü:** LLM tabanlı doğrulama öncesinde çıktıdaki rakamlar, yüzdeler (`%70`, `%80`) ve kaynak adları programatik olarak taranır; kaynak metinde hiç geçmeyen sayılar kırmızı bayrakla işaretlenir.
2. **Çift Doküman Denetimi:** Yalnız özet değil, son kullanıcıya/promptera gidecek final metin de denetlenir.
3. **1-Turlu Öz-Düzeltme (`Critique-and-Revise`):** Eğer `desteklenmedi > 0` ise doğrulama raporu (hangi cümle neden desteksiz) ve gerçek `[K#]` parçaları yazıcı modele geri verilerek uydurma iddialar temizlenir veya kaynakta karşılığı olan ifadelerle düzeltilir.

---

## 3. Yöntemler Karşılaştırma Özeti

| Yaklaşım / Problem | Eski/Naif Yöntem | Endüstri Standardı | Sağladığı Kazanım |
| :--- | :--- | :--- | :--- |
| **İndeks Birimi** | 2K karakter özet + kör 500 kelimelik kesim | **Tam `HamMetin` + Zaman Damgalı Transkript** (`%15` overlap, cümle sınırı, bağlamsal header) | %94 sıkıştırma kaybını %0'a indirir |
| **Arama Mekanizması** | Tek vektör sorgusu, yalnız cosine similarity | **Multi-Query + Hibrit RRF (`pgvector` + `tsvector`/`pg_trgm`) + Çapraz Dil** | Özel isim ve teknik kavramlarda yüksek geri çağırma |
| **Kaynak Seçimi** | `Take(10)` (tek dosya monopolü) | **MMR (`λ≈0.72`) + `MaxChunksPerSource` kotası + Kaynakça eleme** | Tüm alt başlıklara ve kaynaklara dengeli yayılım |
| **Dar Model Sentezi** | `CompactPromptForGroq` baş-son kırpma | **Sağlayıcıya Duyarlı Bölüm/Sahne Bazlı Üretim** | Yedek modellerde dahi %0 kaynak silinmesi |
| **Kalite Kontrol** | Yalnız özet, kırık `chunkIds`, sahte `%100` fallback | **Özet + VideoPlanı + Sayısal Tarayıcı + Sahte %100 İptali + 1-Tur Öz-Düzeltme** | Gerçekçi güven skoru ve üretimde halüsinasyon temizliği |

---

## 4. VideoÖzet AI Mimarisi İçin Somut Çıkarımlar

Projemizin mevcut teknoloji yığını (`.NET 10`, PostgreSQL `pgvector` uzantısı, Ollama `bge-m3` 1024-boyutlu çok dilli embedding, MassTransit/RabbitMQ, Angular + SignalR) bu endüstri standartlarını **sıfır ek veritabanı veya harici arama motoru kurulumu gerektirmeden** uygulamaya uygundur:
1. **Tek Veritabanında Hibrit RAG:** PostgreSQL üzerinde halihazırda `pgvector` (HNSW indeksi) aktiftir. Aynı `video_chunk_documents` tablosuna `'simple'` sözlüklü `tsvector` GIN indeksi (ve opsiyonel `pg_trgm`) eklenerek hem Türkçe ekli özel isimler (*Gazzâlî'nin, Akbulut'un*) hem İngilizce terimler (*hypernudge, compatibilism*) tek SQL/EF sorgusuyla puanlanabilir.
2. **Çok Dilli `bge-m3` Gücü + Terim Genişletmesi:** Ollama üzerinde çalışan `bge-m3` doğal olarak Türkçe-İngilizce çapraz dil anlamsal eşleme yeteneğine sahiptir; buna alt-sorguda hafif terim genişletmesi eklendiğinde İngilizce PDF'lerin (`Stanford`, `Sapolsky`, `Dennett`, `Floridi`) sözcüksel skorları da yükselir.
3. **Mevcut Veri Yatırımını Koruma:** Veritabanında halihazırda çıkarılmış `1,5M+` karakter `HamMetin` kayıtları mevcuttur. İyileştirmeler mevcut `DokumanMetin` ve `VideoTranscript` tablolarından okuyarak **tek tıkla yeniden indeksleme** desteğiyle saniyeler/dakikalar içinde canlıya alınabilir.
