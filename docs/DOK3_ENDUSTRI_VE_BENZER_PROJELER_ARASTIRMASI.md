# DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Başkaları Nasıl Yapmış?)

> **4'lü Dokümantasyon Navigasyonu:**
> - [DOK1 — Hedef ve Gereksinimler](file:///d:/Hoca/VideoOzet/docs/DOK1_HEDEF_VE_GEREKSINIMLER.md)
> - [DOK2 — Mevcut Kod Debug ve Akış Raporu](file:///d:/Hoca/VideoOzet/docs/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md)
> - **[DOK3 — Endüstri, Benzer Projeler ve Literatür Araştırması (Aktif)](file:///d:/Hoca/VideoOzet/docs/DOK3_ENDUSTRI_VE_BENZER_PROJELER_ARASTIRMASI.md)**
> - [DOK4 — Adım Adım Geliştirme Planı](file:///d:/Hoca/VideoOzet/docs/DOK4_ADIM_ADIM_GELISTIRME_PLANI.md)

---

## 1. İncelenen 5 Temel Problem ve Sektördeki Referans Sistemler

[DOK2](file:///d:/Hoca/VideoOzet/docs/DOK2_MEVCUT_KOD_DEBUG_VE_AKIS_RAPORU.md) analizinde saptadığımız darboğazlar (46 kaynağın 30'unun ıskalanması, 20 dakikalık senaryonun sığ kalması, plan revizyonlarının yok sayılması ve kullanıcının kendi yorumunun QC tarafından silinmesi) modern bilgi-yoğun yapay zeka sistemlerinde bilinen ve çözülmüş 5 temel mühendislik problemine karşılık gelir:

| Problem Alanı | İncelenen Açık Kaynak / Endüstri Sistemleri | Temel Mimari / Algoritmik Çözüm |
|---|---|---|
| **1. Çok Kaynaklı Planlama & İnsan Müdahalesi** | **Stanford STORM & Co-STORM**, **Google NotebookLM**, **GPT-Researcher** | Yazım öncesi hiyerarşik taslak (Outline) + İnsan-AI ortak zihin haritası düzenleme + Kaynak→Bölüm açık ataması |
| **2. Uzun Senaryo Üretiminde Yüzeyselleşme** | **AgentWrite / LongWriter** (THUDM, ICLR 2025), **DOC** (Yang et al.) | Tek çağrı yerine *Plan-and-Write*: Kelime bütçeli alt sahne ayrıştırma ve sıralı sahne genişletme |
| **3. 40+ Kaynakta Eksiksiz Kapsam (Coverage)** | **RAPTOR** (Stanford, ICLR 2024), **Anthropic Contextual Retrieval** | İki katmanlı hiyerarşik indeks (Kaynak Özet Kartı + Ham Parça) ve Kota-Kısıtlı RRF + MMR seçimi |
| **4. Yazar Tezi ile Kaynak Kanıtını Ayıran QC** | **VeriScore** (EMNLP 2024) vs. **FActScore / Google SAFE** | İddia sınıflandırma: *Doğrulanabilir Olgusal İddia* vs. *Yazar Sentezi / Analitik Çerçeve* ayrımı |
| **5. Çok Bölümlü Seride Tutarlılık & Revizyon** | **MemGPT**, **RecurrentGPT**, **DAG Dirty-Flag Propagation** | Yapılandırılmış Bölüm Devir Defteri (State Ledger) ve revizyonda bağımlı bölümleri bayraklama |

---

## 2. Mimari ve Matematiksel Modellerin Karşılaştırmalı Analizi

### 2.1. Çok Kaynaklı Planlama: Stanford STORM / Co-STORM ve NotebookLM Yaklaşımı

Klasik RAG sistemleri (bizim mevcut `TopicAnalysisConsumer` + `SeriesPlanConsumer` akışımız gibi) kaynaklardan düz bir özet çıkarıp tek adımda taslak üretir ve revizyonda eski taslağı silip baştan yazar.

* **Stanford STORM (Shao et al., NAACL 2024) & Co-STORM (Jiang et al., EMNLP 2024):**
  - Yazım aşamasına geçmeden önce tüm ağırlığı **Yapılandırılmış Taslak (Hierarchical Outline)** aşamasına verir; her başlık altına hangi kaynak referanslarının (`[K#]`) ve hangi alt soruların bağlandığını açıkça yazar.
  - **Co-STORM** sisteminde kullanıcı bir "Moderatör/Yönetmen" rolündedir: Kullanıcının eklediği perspektif düğümleri (*User-Injected Discourse Nodes*) kaynak eksikliği nedeniyle budanmaz; aksine sistem kaynakları kullanıcının perspektifini destekleyecek veya karşılaştıracak şekilde yeniden örgütler.
  - Revizyonlarda **Durum Korumalı Fark Güncellemesi (Stateful Outline Patching)** uygulanır: Mevcut taslak ağacı $O_{t}$ ve kullanıcı kısıtı $C$ birlikte verilerek $O_{t+1} = \text{Patch}(O_t, C)$ üretilir ve ardından deterministik şema doğrulayıcı (`|O_{t+1}| == C.VideoSayisi`) çalıştırılır.
* **Google NotebookLM Mimarisi:**
  - Yüklenen 50 kaynağın tamamı için yükleme anında tam kapsamlı **Kaynak Rehberi (Source Guide)** üretilir. Üst düzey planlama yapılırken vektör aramasıyla 24 parça çekilmez; **tüm kaynakların Source Guide özetleri** modele birlikte verilir. Böylece 50 kaynağın tamamı %100 görünür olur.

### 2.2. Uzun Metin ve Senaryo Üretimi: AgentWrite & LongWriter (Bai et al., ICLR 2025)

**Neden Mevcut Sistemde Düşünürler 2 Cümleyle Geçiştiriliyor?**
THUDM araştırmacılarının *LongWriter* çalışmasında kanıtladığı üzere, modern LLM'lerin **girdi bağlam penceresi** 128K–1M token olsa bile, eğitildikleri SFT dağılımı nedeniyle **tek bir çağrıdaki anlamlı çıktı uzunluğu tavanı (Output Length Ceiling)** genellikle **1.200–2.000 kelime** civarındadır. Tek bir çağrıda 20 dakikalık (~3.000 kelime prompter + ~1.000 kelime kurgu yönergesi = ~4.000 kelime) senaryo istendiğinde model içsel olarak "sıkıştırma moduna" geçer ve her filozofun görüşünü birer cümlelik özet listesine indirger.

**AgentWrite Çözümü (Divide-and-Conquer Plan-then-Write):**
1. Hedef süre $T$ (dakika) ve konuşma hızı $r_{\text{wpm}} \approx 140\text{ kelime/dk}$ üzerinden toplam hedef konuşma hacmi hesaplanır:
   $$W_{\text{toplam}} = T \cdot r_{\text{wpm}}$$
2. Bölüm $S$ adet sahneye ($S = \max(4, \lceil T / 3 \rceil)$) bölünür ve her sahneye açık bir süre $\Delta t_i$ ve kelime hedefi $W_i = \Delta t_i \cdot r_{\text{wpm}}$ atanır:
   $$\sum_{i=1}^{S} \Delta t_i = T, \qquad \sum_{i=1}^{S} W_i = W_{\text{toplam}}$$
3. Her sahne (veya 2'şerli sahne bloğu) yazılırken modele:
   - Bölümün genel sahne iskeleti (bütünlük için),
   - Bir önceki sahnenin son paragrafı (pürüzsüz geçiş için),
   - Sadece o sahnenin odaklandığı düşünür/konulara ait derin `[K#]` kaynak parçaları,
   - Açık kelime bütçesi ($W_i$ kelime konuşma metni)
   verilir. Bu yöntemle 20–30 dakikalık videolar tek çağrıda yorulmadan **3.000–4.500 kelimelik** tam stüdyo kalitesinde üretilir.

### 2.3. Çoklu Kaynak Kapsamı: Hiyerarşik İndeks (RAPTOR + Contextual Retrieval) ve Kota-Kısıtlı MMR

Mevcut [SourceContextBuilder.cs](file:///d:/Hoca/VideoOzet/src/VideoOzet.Business/Services/SourceContextBuilder.cs#L25) düz (flat) parça havuzundan sabit $N=24$ parça seçtiği için büyük kaynak havuzlarında kaynakların %65'ini dışarıda bırakır.

Endüstrideki standart çözüm **2 Aşamalı Hiyerarşik Seçim (Two-Stage Hierarchical Retrieval)** modelidir:
1. **Aşama 1 — Bölüm Bazlı Kaynak Ataması ($M_b \subseteq \mathcal{D}$):** Seri planlama aşamasında her bölüm $b$ için ilgili kaynak kümesi $M_b$ belirlenir.
2. **Aşama 2 — Kaynak Tabanlı Taban Kota + MMR (Floor-Quota Constrained MMR):** Bölüm $b$ üretilirken hedef parça bütçesi $K$ (ör. $K = \min(64, \max(30, 3 \cdot |M_b|))$) olarak ölçeklenir. Önce $M_b$ içindeki **her atanmış kaynaktan en yüksek RRF skorlu en az 1 parça** (taban kota $q_{\min} = 1$) garanti altına alınır; kalan bütçe çeşitlilik cezalı MMR skoru ile doldurulur:
   $$\text{MMR}(c) = \lambda \cdot \text{Rel}(c, Q) - (1 - \lambda) \max_{s \in \mathcal{S}} \text{Sim}(c, s) - \gamma \cdot \text{Count}(\text{src}(c), \mathcal{S})$$
   Böylece bölüme atanan **hiçbir kaynak %0 payla dışarıda kalmaz** (`Recall@Source = %100`).

Ayrıca **Anthropic Contextual Retrieval** prensibine uygun olarak, chunk metninin içine değişebilir durum bilgisini (`Mod: DOĞRUDAN REFERANS`) sabit metin olarak gömmek yerine; kullanım modu çalışma anında (query/formatting time) üst veri tablosundan dinamik olarak başlığa eklenmelidir. Böylece kullanıcı modu değiştirdiğinde yeniden embedding almaya gerek kalmadan %100 tutarlılık sağlanır.

### 2.4. Kalite Kontrol (QC): FActScore / SAFE Kısıtı ve VeriScore Çözümü

* **FActScore (Min et al., 2023) & Google SAFE (Wei et al., 2024):** Metni tümüyle atomik olgusal iddialara böler ve kaynakta karşılığı olmayan her cümleyi "Desteklenmedi (Hallucination)" sayar. Bizim mevcut `QualityCheckService` + `Auto-Refine` yapımız tam olarak bu hataya düşmektedir: Kullanıcının bizzat istediği *"kaderin mizaç olması ve sınavın zorluğuna göre puanlanması"* gibi felsefi/analitik çerçeveyi kaynakta bulamayınca halüsinasyon sayıp silmektedir!
* **VeriScore (Song et al., EMNLP 2024) Yaklaşımı:** Uzun metinlerdeki ifadeleri doğrulamadan önce **İddia Türü Sınıflandırması (Claim Taxonomy)** uygular:
  1. **`OLGUSAL_KAYNAK_IDDIASI` (Verifiable Empirical/Attributed Claim):** İstatistikler, yüzdeler (`%30`), deney sonuçları (Libet, Haynes), belirli bir düşünür/yazar atıfları (*"Spinoza şunu der"*). → **Mutlaka `[K#]` kaynaklarında doğrulanmalıdır; yoksa Auto-Refine ile çıkarılır.**
  2. **`YAZAR_PERSPEKTIFI_VE_SENTEZ` (Authorial Thesis & Pedagogical Framing):** Kullanıcının `[YAZAR-TEZİ]` direktifinden gelen kavramsal yorumlar, pedagojik benzetmeler, geçiş soruları ve değer yargıları. → **Kaynak aramasından muaftır; `[YAZAR-TEZİ]` ile uyumlu olduğu sürece korunur ve puan kırılmaz.**

---

## 3. VideoOzet Projemiz İçin Somut Mimari Çıkarımlar

> [!TIP]
> Literatür ve endüstri analizinden elde edilen bulgular doğrultusunda projemizde uygulayacağımız net mimari kararlar:

1. **NotebookLM Tipi Tam Kaynak Kartları:** PDF özetlemedeki 22.000 karakterlik kırpma kaldırılacak; `SourceTopicMapper` kesik özeti değil, tam metin parçalarını tarayarak her kaynak için zengin konu/argüman/düşünür haritası çıkaracak.
2. **Co-STORM Tipi Durum Korumalı Planlama + Deterministik Kısıt Denetimi:** Plan revizyonlarında eski plan JSON'u modele verilecek, C# katmanında `VideoSayisi` ve `VarsayilanSureDk` zorlanacak ve harita kartları onay öncesi doğrudan düzenlenebilir (`SeriBolumler` senkronizasyonlu) olacak.
3. **AgentWrite Tipi Sahne-Sahne (Iterative) Senaryo Motoru:** Bölüm üretiminde süreye göre sahne bütçesi hesaplanacak; sahne iskeleti çıkarıldıktan sonra sahne grupları ayrı LLM çağrılarıyla derinlemesine yazılacak.
4. **Taban Kotalı Hiyerarşik RAG + Çalışma Anı Mod Başlığı:** Bölüme atanan her kaynaktan en az 1 kanıt parçası garanti edilecek (dinamik 30–64 chunk bütçesi); `Mod` etiketi chunk metninden ayrıştırılıp çalışma anında okunacak.
5. **VeriScore Tipi Çift Katmanlı QC:** `[YAZAR-TEZİ]` ile `[K#]` olgusal iddiaları birbirinden ayrılarak hem uydurma istatistikler %100 engellenecek hem de kullanıcının kendi anlatmak istediği argümanlar %100 korunacak.
