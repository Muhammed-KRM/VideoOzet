using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Helpers;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;

namespace VideoOzet.Business.Services;

public class StudioQaService : IStudioQaService
{
    private readonly AppDbContext _dbContext;
    private readonly ISynthesisProvider _synthesisProvider;
    private readonly ISourceContextBuilder _sourceContextBuilder;
    private readonly ISourceTopicMapper _sourceTopicMapper;
    private readonly ILogger<StudioQaService> _logger;

    private static readonly ConcurrentDictionary<string, DateTime> ActiveThreads = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public StudioQaService(
        AppDbContext dbContext,
        ISynthesisProvider synthesisProvider,
        ISourceContextBuilder sourceContextBuilder,
        ISourceTopicMapper sourceTopicMapper,
        ILogger<StudioQaService> logger)
    {
        _dbContext = dbContext;
        _synthesisProvider = synthesisProvider;
        _sourceContextBuilder = sourceContextBuilder;
        _sourceTopicMapper = sourceTopicMapper;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StudioQaMessageDto>> GetHistoryAsync(
        Guid? egitimId,
        Guid? contentRequestId,
        int? bolumNo,
        Guid? kaynakId,
        string kapsam,
        CancellationToken ct = default)
    {
        var normalizedScope = NormalizeScope(kapsam, bolumNo, kaynakId);
        var query = BuildThreadQuery(egitimId, contentRequestId, bolumNo, kaynakId, normalizedScope);

        var items = await query
            .OrderBy(m => m.OlusturmaTarihi)
            .Take(80)
            .ToListAsync(ct);

        return items.Select(MapToDto).ToList();
    }

    public bool IsThreadProcessing(
        Guid? egitimId,
        Guid? contentRequestId,
        int? bolumNo,
        Guid? kaynakId,
        string kapsam)
    {
        var normalizedScope = NormalizeScope(kapsam, bolumNo, kaynakId);
        var key = BuildThreadKey(egitimId, contentRequestId, bolumNo, kaynakId, normalizedScope);
        if (ActiveThreads.TryGetValue(key, out var startedUtc))
        {
            if (DateTime.UtcNow - startedUtc < TimeSpan.FromMinutes(12))
                return true;
            ActiveThreads.TryRemove(key, out _);
        }
        return false;
    }

    private static string BuildThreadKey(
        Guid? egitimId,
        Guid? contentRequestId,
        int? bolumNo,
        Guid? kaynakId,
        string scope)
    {
        if (scope == "Bolum" && contentRequestId.HasValue)
            return $"Bolum:{contentRequestId.Value}:{bolumNo ?? 1}";
        if (scope == "Plan" && contentRequestId.HasValue)
            return $"Plan:{contentRequestId.Value}";
        if (scope == "Kaynak" && kaynakId.HasValue)
            return $"Kaynak:{kaynakId.Value}";
        return $"{scope}:{egitimId ?? Guid.Empty}";
    }

    public async Task ClearHistoryAsync(
        Guid? egitimId,
        Guid? contentRequestId,
        int? bolumNo,
        Guid? kaynakId,
        string kapsam,
        CancellationToken ct = default)
    {
        var normalizedScope = NormalizeScope(kapsam, bolumNo, kaynakId);
        var items = await BuildThreadQuery(egitimId, contentRequestId, bolumNo, kaynakId, normalizedScope)
            .ToListAsync(ct);

        if (items.Count > 0)
        {
            _dbContext.StudioQaMessages.RemoveRange(items);
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    public async Task<StudioQaMessageDto> AskAsync(StudioQaAskRequestDto request, CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Soru))
            throw new ArgumentException("Soru metni boş olamaz.");

        var soru = request.Soru.Trim();
        var normalizedScope = NormalizeScope(request.Kapsam, request.BolumNo, request.KaynakId);

        ContentRequest? contentRequest = null;
        Guid resolvedEgitimId = request.EgitimId ?? Guid.Empty;

        if (request.ContentRequestId.HasValue && request.ContentRequestId.Value != Guid.Empty)
        {
            contentRequest = await _dbContext.ContentRequests
                .Include(r => r.KonuAnalizi)
                .Include(r => r.SeriPlanlari)
                    .ThenInclude(p => p.SeriBolumler)
                        .ThenInclude(b => b.Revizyonlar)
                .FirstOrDefaultAsync(r => r.Id == request.ContentRequestId.Value, ct);

            if (contentRequest != null && resolvedEgitimId == Guid.Empty)
                resolvedEgitimId = contentRequest.EgitimId;
        }

        if (resolvedEgitimId == Guid.Empty && request.KaynakId.HasValue)
        {
            var vidEgitim = await _dbContext.Videolar.AsNoTracking()
                .Where(v => v.Id == request.KaynakId.Value)
                .Select(v => v.EgitimId)
                .FirstOrDefaultAsync(ct);

            if (vidEgitim != Guid.Empty)
            {
                resolvedEgitimId = vidEgitim;
            }
            else
            {
                resolvedEgitimId = await _dbContext.Dokumanlar.AsNoTracking()
                    .Where(d => d.Id == request.KaynakId.Value)
                    .Select(d => d.EgitimId)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (resolvedEgitimId == Guid.Empty)
            throw new InvalidOperationException("İlgili eğitim bulunamadı.");

        // Mevcut sohbet geçmişinden son 8 mesajı oku
        var recentHistory = await BuildThreadQuery(
                resolvedEgitimId,
                request.ContentRequestId,
                request.BolumNo,
                request.KaynakId,
                normalizedScope)
            .OrderByDescending(m => m.OlusturmaTarihi)
            .Take(8)
            .ToListAsync(ct);
        recentHistory.Reverse();

        // Kullanıcı mesajını kaydet
        var userMsg = new StudioQaMessage
        {
            Id = Guid.NewGuid(),
            EgitimId = resolvedEgitimId,
            ContentRequestId = request.ContentRequestId,
            BolumNo = request.BolumNo,
            KaynakId = request.KaynakId,
            Kapsam = normalizedScope,
            Rol = "user",
            Mesaj = soru,
            OlusturmaTarihi = DateTime.UtcNow
        };
        _dbContext.StudioQaMessages.Add(userMsg);
        await _dbContext.SaveChangesAsync(ct);

        var threadKey = BuildThreadKey(resolvedEgitimId, request.ContentRequestId, request.BolumNo, request.KaynakId, normalizedScope);
        ActiveThreads[threadKey] = DateTime.UtcNow;

        try
        {
            // 3 Katmanlı Bağlam Yönlendirici (Context Router)
            var (contextBlock, contextBadgeSummary) = normalizedScope switch
            {
                "Bolum" => await BuildEpisodeScopeContextAsync(contentRequest, resolvedEgitimId, request.BolumNo ?? 1, soru, recentHistory, ct),
                "Plan" => await BuildPlanScopeContextAsync(contentRequest, resolvedEgitimId, soru, ct),
                _ => await BuildSourceScopeContextAsync(resolvedEgitimId, request.KaynakId, soru, ct)
            };

            var historyBlock = FormatConversationHistory(recentHistory);
            var prompt = BuildStudioQaPrompt(normalizedScope, request.BolumNo, soru, historyBlock, contextBlock);

            var rawLlmResponse = await _synthesisProvider.AskStudioQuestionAsync(prompt, ct);
            var (answerMarkdown, suggestion) = ParseQaResponse(rawLlmResponse, normalizedScope, request.BolumNo);

            var assistantMsg = new StudioQaMessage
            {
                Id = Guid.NewGuid(),
                EgitimId = resolvedEgitimId,
                ContentRequestId = request.ContentRequestId,
                BolumNo = request.BolumNo,
                KaynakId = request.KaynakId,
                Kapsam = normalizedScope,
                Rol = "assistant",
                Mesaj = answerMarkdown,
                OneriJson = suggestion != null ? JsonSerializer.Serialize(suggestion, JsonOpts) : null,
                KullanilanBaglamOzeti = contextBadgeSummary,
                OlusturmaTarihi = DateTime.UtcNow
            };

            _dbContext.StudioQaMessages.Add(assistantMsg);
            await _dbContext.SaveChangesAsync(ct);

            return MapToDto(assistantMsg);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kullanıcı sayfadan ayrılmış olsa bile hatayı sohbet geçmişine kaydet ki döndüğünde sessizce kaybolmasın
            var errMsg = new StudioQaMessage
            {
                Id = Guid.NewGuid(),
                EgitimId = resolvedEgitimId,
                ContentRequestId = request.ContentRequestId,
                BolumNo = request.BolumNo,
                KaynakId = request.KaynakId,
                Kapsam = normalizedScope,
                Rol = "assistant",
                Mesaj = $"⚠️ **Yanıt üretilirken bir hata oluştu:** {ex.Message}",
                OlusturmaTarihi = DateTime.UtcNow
            };
            try
            {
                _dbContext.StudioQaMessages.Add(errMsg);
                await _dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch { }
            throw;
        }
        finally
        {
            ActiveThreads.TryRemove(threadKey, out _);
        }
    }

    public async Task<(bool Basarili, string Mesaj, int? HedefBolumNo)> ApplySuggestionAsync(
        StudioQaApplySuggestionDto dto,
        CancellationToken ct = default)
    {
        if (dto == null || dto.ContentRequestId == Guid.Empty || dto.HedefBolumNo <= 0)
            return (false, "Geçersiz bölüm veya istek bilgisi.", null);

        if (string.IsNullOrWhiteSpace(dto.EskiMetin) || string.IsNullOrWhiteSpace(dto.YeniMetin))
            return (false, "Değiştirilecek eski ve yeni metin boş olamaz.", dto.HedefBolumNo);

        var bolum = await _dbContext.SeriBolumler
            .Include(b => b.Revizyonlar)
            .Include(b => b.SeriPlani)
            .Where(b => b.SeriPlani.ContentRequestId == dto.ContentRequestId && b.BolumNo == dto.HedefBolumNo)
            .OrderByDescending(b => b.SeriPlani.PlanNo)
            .FirstOrDefaultAsync(ct);

        if (bolum == null)
            return (false, $"Bölüm {dto.HedefBolumNo} bulunamadı.", dto.HedefBolumNo);

        var activeRev = (bolum.AktifRevizyonId.HasValue
                ? bolum.Revizyonlar.FirstOrDefault(r => r.Id == bolum.AktifRevizyonId.Value)
                : null)
            ?? bolum.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();

        if (activeRev == null)
            return (false, $"Bölüm {dto.HedefBolumNo} için üretilmiş revizyon bulunamadı.", dto.HedefBolumNo);

        bool isOzet = string.Equals(dto.HedefAlan, "ArastirmaOzeti", StringComparison.OrdinalIgnoreCase);
        string currentText = isOzet ? activeRev.ArastirmaOzeti : activeRev.VideoPlani;

        var replaced = TryReplaceFlexible(currentText, dto.EskiMetin, dto.YeniMetin, out var updatedText);
        if (!replaced)
        {
            return (false,
                "Önerideki eski metin mevcut dokümanda birebir bulunamadı. 'Manuel Düzenle' butonunu kullanarak doğrudan yapıştırabilirsiniz.",
                dto.HedefBolumNo);
        }

        if (isOzet)
            activeRev.ArastirmaOzeti = updatedText;
        else
            activeRev.VideoPlani = updatedText;

        if (dto.MesajId.HasValue && dto.MesajId.Value != Guid.Empty)
        {
            var qaMsg = await _dbContext.StudioQaMessages.FirstOrDefaultAsync(m => m.Id == dto.MesajId.Value, ct);
            if (qaMsg != null && !string.IsNullOrWhiteSpace(qaMsg.OneriJson))
            {
                try
                {
                    var parsedSug = JsonSerializer.Deserialize<StudioQaSuggestionDto>(qaMsg.OneriJson, JsonOpts);
                    if (parsedSug != null)
                    {
                        parsedSug.Uygulandi = true;
                        qaMsg.OneriJson = JsonSerializer.Serialize(parsedSug, JsonOpts);
                    }
                }
                catch { }
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return (true, $"Bölüm {dto.HedefBolumNo} ({(isOzet ? "Araştırma Özeti" : "Video Planı")}) metni başarıyla güncellendi.", dto.HedefBolumNo);
    }

    private async Task<(string ContextText, string SummaryBadge)> BuildEpisodeScopeContextAsync(
        ContentRequest? contentRequest,
        Guid egitimId,
        int bolumNo,
        string soru,
        List<StudioQaMessage> recentHistory,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var badgeParts = new List<string> { $"Bölüm {bolumNo} Tam Metni" };

        var latestPlan = contentRequest?.SeriPlanlari
            ?.OrderByDescending(p => p.PlanNo)
            .FirstOrDefault();

        if (contentRequest != null)
        {
            sb.AppendLine("=== SERİ GENEL KONUSU VE [YAZAR-TEZİ] ===");
            sb.AppendLine(contentRequest.Konu);
            if (!string.IsNullOrWhiteSpace(contentRequest.EkTonTalimati))
                sb.AppendLine($"Ek Ton/Üslup: {contentRequest.EkTonTalimati}");
            if (!string.IsNullOrWhiteSpace(latestPlan?.OnayTalimati))
                sb.AppendLine($"Plan Onay Direktifi: {latestPlan.OnayTalimati}");
            sb.AppendLine();
        }

        var orderedEpisodes = latestPlan?.SeriBolumler
            ?.OrderBy(b => b.BolumNo)
            .ToList() ?? new List<SeriBolum>();

        // KATMAN 1: Tüm Seri Haritası Kuşbakışı Özeti
        if (orderedEpisodes.Count > 0)
        {
            sb.AppendLine("=== TÜM SERİ HARİTASI (KUŞBAKIŞI BÖLÜM PLANI 1..N) ===");
            foreach (var ep in orderedEpisodes)
            {
                var rev = GetActiveRevision(ep);
                sb.AppendLine($"- BÖLÜM {ep.BolumNo}: \"{ep.CalismaBasligi}\" (~{ep.HedefSureDk} dk, Durum: {ep.Durum})");
                if (!string.IsNullOrWhiteSpace(ep.Kanca))
                    sb.AppendLine($"  Açılış Kancası: {ep.Kanca}");
                sb.AppendLine($"  Ana Fikir: {ep.AnaFikir}");
                sb.AppendLine($"  Konular: {ep.KonularJson}");
                if (!string.IsNullOrWhiteSpace(ep.SahneIskeletiJson) && ep.SahneIskeletiJson != "[]")
                    sb.AppendLine($"  Sahne İskeleti: {ep.SahneIskeletiJson}");
                if (!string.IsNullOrWhiteSpace(rev?.DevirNotuJson))
                    sb.AppendLine($"  Süreklilik/Devir Defteri: {rev.DevirNotuJson}");
            }
            sb.AppendLine();
            badgeParts.Add($"Seri Haritası ({orderedEpisodes.Count} Bölüm)");
        }

        // Açık olan bölümün tam metni
        var currentEp = orderedEpisodes.FirstOrDefault(b => b.BolumNo == bolumNo);
        var currentRev = currentEp != null ? GetActiveRevision(currentEp) : null;
        if (currentEp != null && currentRev != null)
        {
            sb.AppendLine($"=== ŞU AN AÇIK OLAN AKTİF BÖLÜM (BÖLÜM {bolumNo}: {currentEp.CalismaBasligi}) TAM METNİ ===");
            sb.AppendLine($"--- [Bölüm {bolumNo} Araştırma Özeti] ---");
            sb.AppendLine(TruncateMiddleIfHuge(currentRev.ArastirmaOzeti, 30000));
            sb.AppendLine();
            sb.AppendLine($"--- [Bölüm {bolumNo} Video Planı ve Prompter Metni] ---");
            sb.AppendLine(TruncateMiddleIfHuge(currentRev.VideoPlani, 45000));
            sb.AppendLine();
        }

        // KATMAN 2: Çapraz Bölüm Tam Metin Okuyucu (Diğer üretilmiş bölümleri önceliklendirerek ekle)
        var otherEpisodesWithContent = orderedEpisodes
            .Where(b => b.BolumNo != bolumNo && GetActiveRevision(b) != null)
            .ToList();

        if (otherEpisodesWithContent.Count > 0)
        {
            var combinedQueryText = $"{soru} {string.Join(" ", recentHistory.TakeLast(2).Select(h => h.Mesaj))}";
            var prioritizedOthers = PrioritizeCrossEpisodes(otherEpisodesWithContent, bolumNo, combinedQueryText);

            int remainingCrossCharBudget = 75000;
            var includedEpNumbers = new List<int>();

            sb.AppendLine("=== DİĞER BÖLÜMLERİN ÜRETİLMİŞ TAM METİNLERİ (ÇAPRAZ BÖLÜM KONTROLÜ İÇİN) ===");
            foreach (var otherEp in prioritizedOthers)
            {
                if (remainingCrossCharBudget < 4000)
                    break;

                var otherRev = GetActiveRevision(otherEp)!;
                int perEpBudget = Math.Max(12000, remainingCrossCharBudget / Math.Max(1, prioritizedOthers.Count));
                var otherPlanSnippet = TruncateMiddleIfHuge(otherRev.VideoPlani, (int)(perEpBudget * 0.7));
                var otherOzetSnippet = TruncateMiddleIfHuge(otherRev.ArastirmaOzeti, (int)(perEpBudget * 0.3));

                sb.AppendLine($"--- [DİĞER BÖLÜM -> BÖLÜM {otherEp.BolumNo}: {otherEp.CalismaBasligi}] ---");
                if (!string.IsNullOrWhiteSpace(otherOzetSnippet))
                {
                    sb.AppendLine($"[Bölüm {otherEp.BolumNo} Araştırma Özeti]:");
                    sb.AppendLine(otherOzetSnippet);
                }
                if (!string.IsNullOrWhiteSpace(otherPlanSnippet))
                {
                    sb.AppendLine($"[Bölüm {otherEp.BolumNo} Video Planı & Prompter Metni]:");
                    sb.AppendLine(otherPlanSnippet);
                }
                sb.AppendLine();

                remainingCrossCharBudget -= (otherPlanSnippet.Length + otherOzetSnippet.Length);
                includedEpNumbers.Add(otherEp.BolumNo);
            }

            if (includedEpNumbers.Count > 0)
            {
                includedEpNumbers.Sort();
                badgeParts.Add($"Çapraz Bölümler ({string.Join(", ", includedEpNumbers.Select(n => $"B{n}"))})");
            }
        }

        // KATMAN 2.5: Kullanıcının Soruda İsmen Belirttiği veya Plan Sonrası Yeni Eklediği TÜM Kaynakları Doğrudan Oku
        var allPlanAssignedIds = orderedEpisodes
            .SelectMany(b => ParseGuidArray(b.AtananKaynaklarJson))
            .ToHashSet();

        var (mentionedIds, mentionedSourceText, mentionedNames) = await LoadExplicitlyMentionedSourcesAsync(
            egitimId,
            soru,
            contentRequest?.OlusturmaTarihi,
            allPlanAssignedIds,
            ct);

        if (!string.IsNullOrWhiteSpace(mentionedSourceText))
        {
            sb.AppendLine("=== KULLANICININ DOĞRUDAN BELİRTTİĞİ VEYA SONRADAN EKLEDİĞİ YENİ KAYNAKLARIN TAM İÇERİĞİ ===");
            sb.AppendLine(mentionedSourceText);
            sb.AppendLine();
            badgeParts.Add($"Yeni/Özel Kaynak ({mentionedNames.Count}: {string.Join(", ", mentionedNames.Take(4))}{(mentionedNames.Count > 4 ? "…" : "")})");
        }

        // KATMAN 3: Eğitim Kaynaklarından İlgili RAG Kanıtları ([K#])
        var assignedSourceIds = ParseGuidArray(currentEp?.AtananKaynaklarJson);
        var preferredForQa = mentionedIds
            .Concat(assignedSourceIds)
            .Distinct()
            .ToList();

        var ragQueries = new List<string> { soru };
        if (currentEp != null)
            ragQueries.Add(currentEp.CalismaBasligi);

        var ragResult = await _sourceContextBuilder.BuildContextAsync(
            egitimId,
            ragQueries,
            maxChars: 32000,
            preferredSourceIds: preferredForQa.Count > 0 ? preferredForQa : null,
            targetChunksOverride: Math.Max(18, preferredForQa.Count + 4),
            ct: ct);

        if (!string.IsNullOrWhiteSpace(ragResult?.ContextText))
        {
            sb.AppendLine("=== EĞİTİM KAYNAKLARINDAN (PDF/VİDEO) İLGİLİ KANIT PARÇALARI ([K#]) ===");
            sb.AppendLine(ragResult.ContextText);
            badgeParts.Add($"{ragResult.UsedChunkIds.Count} Kaynak Kanıtı");
        }

        return (sb.ToString(), string.Join(" · ", badgeParts));
    }

    private async Task<(string ContextText, string SummaryBadge)> BuildPlanScopeContextAsync(
        ContentRequest? contentRequest,
        Guid egitimId,
        string soru,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var badgeParts = new List<string> { "Seri Haritası" };

        var latestPlan = contentRequest?.SeriPlanlari
            ?.OrderByDescending(p => p.PlanNo)
            .FirstOrDefault();

        if (contentRequest != null)
        {
            sb.AppendLine("=== KULLANICI KONUSU VE [YAZAR-TEZİ] ===");
            sb.AppendLine(contentRequest.Konu);
            if (!string.IsNullOrWhiteSpace(contentRequest.EkTonTalimati))
                sb.AppendLine($"Ek Ton Talimatı: {contentRequest.EkTonTalimati}");
            sb.AppendLine();

            if (contentRequest.KonuAnalizi != null)
            {
                sb.AppendLine("=== AI KONU ANALİZİ ===");
                sb.AppendLine($"Ana Fikir: {contentRequest.KonuAnalizi.AnaFikir}");
                sb.AppendLine($"Beklenen Konular: {contentRequest.KonuAnalizi.BeklenenKonularJson}");
                sb.AppendLine($"Kaynakta Doğrudan Olmayıp Yazar Tezi Olanlar: {contentRequest.KonuAnalizi.KaynaktaOlmayanlarJson}");
                sb.AppendLine($"Öneri Gerekçesi: {contentRequest.KonuAnalizi.OneriGerekcesi}");
                sb.AppendLine();
            }
        }

        var assignedMap = new Dictionary<Guid, List<int>>();
        if (latestPlan != null)
        {
            sb.AppendLine($"=== GÜNCEL SERİ PLANI (Plan No: {latestPlan.PlanNo}, Bölüm Sayısı: {latestPlan.SeriBolumler.Count}) ===");
            if (!string.IsNullOrWhiteSpace(latestPlan.KullaniciKisitlariJson))
                sb.AppendLine($"Kullanıcı Kısıtları: {latestPlan.KullaniciKisitlariJson}");
            if (!string.IsNullOrWhiteSpace(latestPlan.DisaridaBirakilanlarJson))
                sb.AppendLine($"Dışarıda Bırakılanlar: {latestPlan.DisaridaBirakilanlarJson}");
            if (!string.IsNullOrWhiteSpace(latestPlan.OnayTalimati))
                sb.AppendLine($"Onay Talimatı: {latestPlan.OnayTalimati}");

            foreach (var b in latestPlan.SeriBolumler.OrderBy(x => x.BolumNo))
            {
                var bSources = ParseGuidArray(b.AtananKaynaklarJson);
                foreach (var sid in bSources)
                {
                    if (!assignedMap.TryGetValue(sid, out var list))
                    {
                        list = new List<int>();
                        assignedMap[sid] = list;
                    }
                    list.Add(b.BolumNo);
                }

                sb.AppendLine($"- BÖLÜM {b.BolumNo}: \"{b.CalismaBasligi}\" (~{b.HedefSureDk} dk, Durum: {b.Durum})");
                sb.AppendLine($"  Kanca: {b.Kanca}");
                sb.AppendLine($"  Ana Fikir: {b.AnaFikir}");
                sb.AppendLine($"  Konular: {b.KonularJson}");
                sb.AppendLine($"  Sahne İskeleti: {b.SahneIskeletiJson}");
            }
            sb.AppendLine();
        }

        var allVideos = await _dbContext.Videolar.AsNoTracking()
            .Where(v => v.EgitimId == egitimId)
            .OrderBy(v => v.OlusturmaTarihi)
            .Select(v => new { v.Id, Baslik = v.Baslik, Tur = "Video", Mode = v.KullanimTuru.ToString(), v.OlusturmaTarihi })
            .ToListAsync(ct);

        var allDocs = await _dbContext.Dokumanlar.AsNoTracking()
            .Where(d => d.EgitimId == egitimId)
            .OrderBy(d => d.OlusturmaTarihi)
            .Select(d => new { d.Id, Baslik = d.DosyaAdi, Tur = "Dokuman", Mode = d.KullanimTuru.ToString(), d.OlusturmaTarihi })
            .ToListAsync(ct);

        var allSources = allVideos.Concat(allDocs).ToList();
        if (allSources.Count > 0)
        {
            var reqCreated = contentRequest?.OlusturmaTarihi;
            sb.AppendLine($"=== EĞİTİMDEKİ TÜM KAYNAK KARTLARI ({allSources.Count} Kaynak ve Atama Durumu) ===");
            foreach (var c in allSources)
            {
                bool addedAfterPlan = reqCreated.HasValue && c.OlusturmaTarihi > reqCreated.Value;
                var status = assignedMap.TryGetValue(c.Id, out var eps)
                    ? $"ATANDI -> Bölüm {string.Join(", ", eps)}"
                    : (addedAfterPlan ? "HİÇBİR BÖLÜME ATANMADI — PLAN SONRASI YENİ EKLENDİ!" : "HİÇBİR BÖLÜME ATANMADI");
                sb.AppendLine($"- [{c.Tur}] {c.Baslik} (ID: {c.Id} | Mod: {c.Mode} | {status})");
            }
            sb.AppendLine();
            badgeParts.Add($"{allSources.Count} Kaynak Kartı");
        }

        var (planMentionedIds, planMentionedText, planMentionedNames) = await LoadExplicitlyMentionedSourcesAsync(
            egitimId,
            soru,
            contentRequest?.OlusturmaTarihi,
            assignedMap.Keys.ToHashSet(),
            ct);

        if (!string.IsNullOrWhiteSpace(planMentionedText))
        {
            sb.AppendLine("=== KULLANICININ BELİRTTİĞİ VEYA PLAN SONRASI YENİ EKLEDİĞİ KAYNAKLARIN İÇERİĞİ ===");
            sb.AppendLine(planMentionedText);
            sb.AppendLine();
            badgeParts.Add($"Yeni/Özel Kaynak ({planMentionedNames.Count})");
        }

        var ragResult = await _sourceContextBuilder.BuildContextAsync(
            egitimId,
            new[] { soru },
            maxChars: 30000,
            preferredSourceIds: planMentionedIds.Count > 0 ? planMentionedIds : null,
            targetChunksOverride: Math.Max(16, planMentionedIds.Count + 6),
            ct: ct);

        if (!string.IsNullOrWhiteSpace(ragResult?.ContextText))
        {
            sb.AppendLine("=== SORUYLA İLGİLİ KAYNAK METİN KANITLARI ([K#]) ===");
            sb.AppendLine(ragResult.ContextText);
            badgeParts.Add($"{ragResult.UsedChunkIds.Count} Kanıt Parçası");
        }

        return (sb.ToString(), string.Join(" · ", badgeParts));
    }

    private async Task<(string ContextText, string SummaryBadge)> BuildSourceScopeContextAsync(
        Guid egitimId,
        Guid? kaynakId,
        string soru,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var badgeParts = new List<string>();

        if (kaynakId.HasValue && kaynakId.Value != Guid.Empty)
        {
            var dokuman = await _dbContext.Dokumanlar.AsNoTracking()
                .Include(d => d.DokumanMetin)
                .FirstOrDefaultAsync(d => d.Id == kaynakId.Value, ct);

            if (dokuman != null)
            {
                badgeParts.Add($"Doküman: {dokuman.DosyaAdi}");
                sb.AppendLine($"=== AÇIK OLAN DOKÜMAN KAYNAĞI: {dokuman.DosyaAdi} (Mod: {dokuman.KullanimTuru}) ===");
                if (!string.IsNullOrWhiteSpace(dokuman.DokumanMetin?.CevrilmisOzetMetni))
                {
                    sb.AppendLine("--- Türkçe Damıtılmış Özeti ---");
                    sb.AppendLine(TruncateMiddleIfHuge(dokuman.DokumanMetin.CevrilmisOzetMetni, 18000));
                }
                else if (!string.IsNullOrWhiteSpace(dokuman.DokumanMetin?.DamitilmisOzetMetni))
                {
                    sb.AppendLine("--- Damıtılmış Bilgi Özeti ---");
                    sb.AppendLine(TruncateMiddleIfHuge(dokuman.DokumanMetin.DamitilmisOzetMetni, 18000));
                }

                if (!string.IsNullOrWhiteSpace(dokuman.DokumanMetin?.HamMetin))
                {
                    sb.AppendLine("--- Orijinal Tam Metin ---");
                    sb.AppendLine(TruncateMiddleIfHuge(dokuman.DokumanMetin.HamMetin, 45000));
                }
                sb.AppendLine();
            }
            else
            {
                var video = await _dbContext.Videolar.AsNoTracking()
                    .Include(v => v.Summary)
                    .Include(v => v.Transcript)
                    .FirstOrDefaultAsync(v => v.Id == kaynakId.Value, ct);

                if (video != null)
                {
                    badgeParts.Add($"Video: {video.Baslik}");
                    sb.AppendLine($"=== AÇIK OLAN VİDEO KAYNAĞI: {video.Baslik} (Mod: {video.KullanimTuru}) ===");
                    if (!string.IsNullOrWhiteSpace(video.Summary?.CevrilmisOzetMetni))
                    {
                        sb.AppendLine("--- Türkçe Damıtılmış Özeti ---");
                        sb.AppendLine(TruncateMiddleIfHuge(video.Summary.CevrilmisOzetMetni, 18000));
                    }
                    else if (!string.IsNullOrWhiteSpace(video.Summary?.OzetMetni))
                    {
                        sb.AppendLine("--- Video Özeti ---");
                        sb.AppendLine(TruncateMiddleIfHuge(video.Summary.OzetMetni, 18000));
                    }

                    if (!string.IsNullOrWhiteSpace(video.Transcript?.HamMetin))
                    {
                        sb.AppendLine("--- Ham Transkript Metni ---");
                        sb.AppendLine(TruncateMiddleIfHuge(video.Transcript.HamMetin, 45000));
                    }
                    sb.AppendLine();
                }
            }
        }

        // Çapraz kaynak & seri planı farkındalığı
        var latestSeriesReq = await _dbContext.ContentRequests.AsNoTracking()
            .Include(r => r.SeriPlanlari)
                .ThenInclude(p => p.SeriBolumler)
            .Where(r => r.EgitimId == egitimId)
            .OrderByDescending(r => r.OlusturmaTarihi)
            .FirstOrDefaultAsync(ct);

        var latestPlan = latestSeriesReq?.SeriPlanlari?.OrderByDescending(p => p.PlanNo).FirstOrDefault();
        if (latestPlan != null && latestPlan.SeriBolumler.Count > 0)
        {
            sb.AppendLine("=== BU EĞİTİMDEKİ GÜNCEL SERİ HARİTASI ÖZETİ ===");
            foreach (var b in latestPlan.SeriBolumler.OrderBy(x => x.BolumNo))
            {
                bool hasThisSource = kaynakId.HasValue && ParseGuidArray(b.AtananKaynaklarJson).Contains(kaynakId.Value);
                sb.AppendLine($"- Bölüm {b.BolumNo}: \"{b.CalismaBasligi}\" {(hasThisSource ? "[BU KAYNAK BU BÖLÜME ATANMIŞ]" : "")} — {b.AnaFikir}");
            }
            sb.AppendLine();
            badgeParts.Add("Seri Planı Özeti");
        }

        var ragResult = await _sourceContextBuilder.BuildContextAsync(
            egitimId,
            new[] { soru },
            maxChars: 28000,
            preferredSourceIds: kaynakId.HasValue ? new[] { kaynakId.Value } : null,
            targetChunksOverride: 14,
            ct: ct);

        if (!string.IsNullOrWhiteSpace(ragResult?.ContextText))
        {
            sb.AppendLine("=== EĞİTİMDEKİ DİĞER KAYNAKLARDAN VE İNDEKSTEN İLGİLİ PARÇALAR ([K#]) ===");
            sb.AppendLine(ragResult.ContextText);
            badgeParts.Add($"{ragResult.UsedChunkIds.Count} Çapraz Kanıt");
        }

        return (sb.ToString(), string.Join(" · ", badgeParts));
    }

    private static string BuildStudioQaPrompt(
        string scope,
        int? bolumNo,
        string soru,
        string historyBlock,
        string contextBlock)
    {
        var activeTargetInfo = scope switch
        {
            "Bolum" => $"Kullanıcı şu anda BÖLÜM {bolumNo ?? 1} ekranındadır; ancak tüm seri haritası, diğer üretilmiş bölümler ve kaynak kanıtları da aşağıda verilmiştir.",
            "Plan" => "Kullanıcı şu anda AKILLI SERİ PLANLAYICI ekranındadır; seri haritası, konu analizi ve eğitimdeki tüm kaynakların atama durumları aşağıda verilmiştir.",
            _ => "Kullanıcı şu anda EĞİTİM KAYNAK DETAYI ekranındadır; açık olan kaynağın tam metni ve eğitimdeki diğer ilgili kaynak kanıtları aşağıda verilmiştir."
        };

        return $$"""
            Sen VideoÖzet AI Stüdyosunun Baş Editör ve Akademik Araştırma Asistanısın.
            {{activeTargetInfo}}

            MUTLAK DAVRANIŞ VE NİYET KURALLARI (ÇOK ÖNEMLİ!):
            1. **NİYET HASSASİYETİ (SADECE SORUYSA SADECE CEVAPLA!):**
               - Eğer kullanıcı yalnızca bir soru soruyorsa ("Neden böyle yaptın?", "2. bölümde bundan bahsedilmiş mi?", "Bu kaynakta X ne diyor?", "Hangi bölümde anlatıldı?"), **KESİNLİKLE istenmeyen metin yeniden yazımı veya değişiklik önerisi dayatma!** `yanitModu` değerini `"sadece_cevap"` ve `oneri` değerini `null` yap; yalnızca soruyu kanıtlarıyla, net, samimi ve doğru biçimde yanıtla.
               - YALNIZCA kullanıcı açıkça bir değişiklik/düzeltme/öneri istediyse ("Nasıl düzeltelim?", "Ne önerirsin?", "Son kısmı değiştir", "Öneri ver", "Nasıl yazabiliriz?") VEYA bir hatayı düzeltmek için senden alternatif metin talep ettiyse `yanitModu` değerini `"oneri_ve_duzeltme"` yap ve `oneri` nesnesini doldur.
            2. **ÇAPRAZ BÖLÜM VE DÖKÜMAN DÜRÜSTLÜĞÜ:**
               - Kullanıcı başka bir bölümde (ör. Bölüm 2 veya Bölüm 3) bir konunun geçip geçmediğini ya da bölümler arası geçiş/kapanış tutarsızlığını sorarsa, aşağıdaki `DİĞER BÖLÜMLERİN ÜRETİLMİŞ TAM METİNLERİ` ve `TÜM SERİ HARİTASI` verilerini satır satır kontrol et.
               - Gerçekten bir tutarsızlık varsa (örneğin 1. bölümün sonunda "bir sonraki videoda X'i konuşacağız" denmiş ama X konusu 2. bölüm yerine 3. bölümde işlenmişse), bunu savunmaya geçmeden açıkça doğrula ve nedenini/hangi bölümde yer aldığını net biçimde açıkla.
            3. **CERRAHİ ÖNERİ KURALI (`oneri` doluysa):**
               - `hedefBolumNo`: Değişikliğin uygulanacağı bölüm numarası (varsayılan: {{bolumNo ?? 1}}).
               - `hedefAlan`: `"VideoPlani"` (konuşma/prompter veya sahne metni için) ya da `"ArastirmaOzeti"`.
               - `eskiMetin`: İlgili bölümün mevcut metninde BİREBİR geçen, değiştirilecek 1–4 cümlelik tam pasaj (uydurma veya özetleme yapma, metinden aynen kopyala ki otomatik değiştirilebilsin!).
               - `yeniMetin`: `eskiMetin` yerine konacak önerilen yeni pasaj (aynı Markdown/blockquote formatını koru).

            --- ÖNCEKİ SOHBET GEÇMİŞİ ---
            {{historyBlock}}

            --- SANA SUNULAN TAM BAĞLAM (BÖLÜMLER, PLAN VE KAYNAKLAR) ---
            {{contextBlock}}

            --- KULLANICININ YENİ SORUSU ---
            {{soru}}

            YANIT FORMATI (SADECE GEÇERLİ JSON DÖNDÜR):
            {
              "yanitModu": "sadece_cevap",
              "cevapMarkdown": "Kullanıcıya gösterilecek net, okunaklı Markdown yanıtı...",
              "oneri": null
            }
            (Eğer kullanıcı öneri/düzeltme istediyse "yanitModu": "oneri_ve_duzeltme" ve "oneri": { "hedefBolumNo": {{bolumNo ?? 1}}, "hedefAlan": "VideoPlani", "aciklama": "...", "eskiMetin": "...", "yeniMetin": "..." } doldur.)
            """;
    }

    private static (string AnswerMarkdown, StudioQaSuggestionDto? Suggestion) ParseQaResponse(
        string raw,
        string scope,
        int? defaultBolumNo)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ("Yanıt üretilemedi.", null);

        try
        {
            var elem = LlmJson.Deserialize<JsonElement>(raw);
            if (elem.ValueKind == JsonValueKind.Object)
            {
                string answer = string.Empty;
                if (elem.TryGetProperty("cevapMarkdown", out var cm) && cm.ValueKind == JsonValueKind.String)
                    answer = cm.GetString() ?? string.Empty;
                else if (elem.TryGetProperty("cevap", out var c) && c.ValueKind == JsonValueKind.String)
                    answer = c.GetString() ?? string.Empty;

                string mode = elem.TryGetProperty("yanitModu", out var ym) && ym.ValueKind == JsonValueKind.String
                    ? ym.GetString() ?? "sadece_cevap"
                    : "sadece_cevap";

                StudioQaSuggestionDto? suggestion = null;
                if (scope == "Bolum" &&
                    !string.Equals(mode, "sadece_cevap", StringComparison.OrdinalIgnoreCase) &&
                    elem.TryGetProperty("oneri", out var oneriElem) &&
                    oneriElem.ValueKind == JsonValueKind.Object)
                {
                    var eski = oneriElem.TryGetProperty("eskiMetin", out var em) && em.ValueKind == JsonValueKind.String
                        ? em.GetString() ?? string.Empty
                        : string.Empty;
                    var yeni = oneriElem.TryGetProperty("yeniMetin", out var ym2) && ym2.ValueKind == JsonValueKind.String
                        ? ym2.GetString() ?? string.Empty
                        : string.Empty;

                    if (!string.IsNullOrWhiteSpace(eski) && !string.IsNullOrWhiteSpace(yeni))
                    {
                        int hedefBolum = defaultBolumNo ?? 1;
                        if (oneriElem.TryGetProperty("hedefBolumNo", out var hb) && hb.TryGetInt32(out var parsedNo) && parsedNo > 0)
                            hedefBolum = parsedNo;

                        var hedefAlan = oneriElem.TryGetProperty("hedefAlan", out var ha) && ha.ValueKind == JsonValueKind.String
                            ? ha.GetString() ?? "VideoPlani"
                            : "VideoPlani";

                        var aciklama = oneriElem.TryGetProperty("aciklama", out var ac) && ac.ValueKind == JsonValueKind.String
                            ? ac.GetString() ?? string.Empty
                            : string.Empty;

                        suggestion = new StudioQaSuggestionDto
                        {
                            HedefBolumNo = hedefBolum,
                            HedefAlan = hedefAlan,
                            Aciklama = aciklama,
                            EskiMetin = eski.Trim(),
                            YeniMetin = yeni.Trim(),
                            Uygulandi = false
                        };
                    }
                }

                if (!string.IsNullOrWhiteSpace(answer))
                    return (answer.Trim(), suggestion);
            }
        }
        catch
        {
            // Model çok uzun Markdown içinde kaçırılmamış tırnak/satır sonu nedeniyle bozuk JSON döndürdüyse sadece cevapMarkdown gövdesini ayıkla
        }

        var unwrapped = UnwrapMalformedJsonEnvelope(raw);
        return (unwrapped, null);
    }

    private static string UnwrapMalformedJsonEnvelope(string raw)
    {
        var trimmed = raw.Trim();
        var match = Regex.Match(
            trimmed,
            @"^\{\s*""yanitModu""[\s\S]*?""cevapMarkdown""\s*:\s*""([\s\S]*?)""\s*(?:,\s*""oneri""[\s\S]*?)?\}$");

        if (match.Success && match.Groups[1].Value.Length > 0)
        {
            return match.Groups[1].Value
                .Replace("\\r\\n", "\n")
                .Replace("\\n", "\n")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Trim();
        }

        return trimmed;
    }

    private static List<SeriBolum> PrioritizeCrossEpisodes(List<SeriBolum> others, int activeBolumNo, string queryText)
    {
        var lower = queryText.ToLowerInvariant();
        return others
            .OrderByDescending(ep =>
            {
                int score = 0;
                if (Regex.IsMatch(lower, $@"\b{ep.BolumNo}\s*\.\s*(bölüm|bolum|video)") ||
                    Regex.IsMatch(lower, $@"\b(bölüm|bolum|video)\s*{ep.BolumNo}\b"))
                {
                    score += 100;
                }
                if (ep.BolumNo == activeBolumNo + 1 || ep.BolumNo == activeBolumNo - 1)
                    score += 40;

                var titleWords = (ep.CalismaBasligi ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length >= 4);
                foreach (var w in titleWords)
                {
                    if (lower.Contains(w.ToLowerInvariant()))
                        score += 15;
                }

                return score;
            })
            .ThenBy(ep => ep.BolumNo)
            .ToList();
    }

    private static bool TryReplaceFlexible(string source, string oldSnippet, string newSnippet, out string updated)
    {
        updated = source;
        if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(oldSnippet))
            return false;

        int directIdx = source.IndexOf(oldSnippet, StringComparison.Ordinal);
        if (directIdx >= 0)
        {
            updated = source.Substring(0, directIdx) + newSnippet + source.Substring(directIdx + oldSnippet.Length);
            return true;
        }

        var normalizedSource = source.Replace("\r\n", "\n");
        var normalizedOld = oldSnippet.Replace("\r\n", "\n").Trim();
        int normIdx = normalizedSource.IndexOf(normalizedOld, StringComparison.OrdinalIgnoreCase);
        if (normIdx >= 0)
        {
            updated = normalizedSource.Substring(0, normIdx) + newSnippet.Replace("\r\n", "\n") + normalizedSource.Substring(normIdx + normalizedOld.Length);
            return true;
        }

        // Esnek boşluk / blockquote ('>') toleranslı eşleştirme
        var tokens = Regex.Split(normalizedOld.Trim(), @"[\s>]+")
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(Regex.Escape)
            .ToArray();

        if (tokens.Length >= 3)
        {
            var pattern = string.Join(@"[\s>""“”]+", tokens);
            var match = Regex.Match(normalizedSource, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                updated = normalizedSource.Substring(0, match.Index)
                    + newSnippet.Replace("\r\n", "\n")
                    + normalizedSource.Substring(match.Index + match.Length);
                return true;
            }
        }

        return false;
    }

    private IQueryable<StudioQaMessage> BuildThreadQuery(
        Guid? egitimId,
        Guid? contentRequestId,
        int? bolumNo,
        Guid? kaynakId,
        string scope)
    {
        var q = _dbContext.StudioQaMessages.AsQueryable().Where(m => m.Kapsam == scope);

        if (scope == "Bolum" && contentRequestId.HasValue)
        {
            q = q.Where(m => m.ContentRequestId == contentRequestId.Value && m.BolumNo == bolumNo);
        }
        else if (scope == "Plan" && contentRequestId.HasValue)
        {
            q = q.Where(m => m.ContentRequestId == contentRequestId.Value);
        }
        else if (scope == "Kaynak" && kaynakId.HasValue)
        {
            q = q.Where(m => m.KaynakId == kaynakId.Value);
        }
        else if (egitimId.HasValue && egitimId.Value != Guid.Empty)
        {
            q = q.Where(m => m.EgitimId == egitimId.Value);
        }

        return q;
    }

    private static string NormalizeScope(string? kapsam, int? bolumNo, Guid? kaynakId)
    {
        if (string.Equals(kapsam, "Kaynak", StringComparison.OrdinalIgnoreCase) || (kaynakId.HasValue && kaynakId.Value != Guid.Empty))
            return "Kaynak";
        if (string.Equals(kapsam, "Plan", StringComparison.OrdinalIgnoreCase))
            return "Plan";
        if (bolumNo.HasValue && bolumNo.Value > 0)
            return "Bolum";
        return "Plan";
    }

    private static BolumRevizyonu? GetActiveRevision(SeriBolum ep)
    {
        if (ep.Revizyonlar == null || ep.Revizyonlar.Count == 0)
            return null;

        if (ep.AktifRevizyonId.HasValue)
        {
            var active = ep.Revizyonlar.FirstOrDefault(r => r.Id == ep.AktifRevizyonId.Value);
            if (active != null)
                return active;
        }

        return ep.Revizyonlar.OrderByDescending(r => r.RevizyonNo).FirstOrDefault();
    }

    private static string FormatConversationHistory(List<StudioQaMessage> history)
    {
        if (history.Count == 0)
            return "(Henüz önceki mesaj yok.)";

        var sb = new StringBuilder();
        foreach (var msg in history)
        {
            var roleLabel = msg.Rol == "user" ? "KULLANICI" : "ASİSTAN";
            var snippet = msg.Mesaj.Length > 1500 ? msg.Mesaj.Substring(0, 1500) + "..." : msg.Mesaj;
            sb.AppendLine($"[{roleLabel}]: {snippet}");
        }
        return sb.ToString();
    }

    private static string TruncateMiddleIfHuge(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        if (text.Length <= maxChars)
            return text;

        int head = (int)(maxChars * 0.55);
        int tail = maxChars - head - 60;
        return text.Substring(0, head) + "\n\n...[ORTA KISIM KISALTILDI]...\n\n" + text.Substring(text.Length - Math.Max(0, tail));
    }

    private static List<Guid> ParseGuidArray(string? raw)
    {
        var list = new List<Guid>();
        if (string.IsNullOrWhiteSpace(raw))
            return list;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var g) && g != Guid.Empty)
                        list.Add(g);
                }
            }
        }
        catch { }
        return list;
    }

    private async Task<(List<Guid> Ids, string TextBlock, List<string> Names)> LoadExplicitlyMentionedSourcesAsync(
        Guid egitimId,
        string queryText,
        DateTime? planCreatedAtUtc,
        HashSet<Guid>? alreadyAssignedIds,
        CancellationToken ct)
    {
        var ids = new List<Guid>();
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(queryText) || egitimId == Guid.Empty)
            return (ids, string.Empty, names);

        var normQuery = NormalizeForMatch(queryText);
        bool asksNewOrUnassigned =
            normQuery.Contains("yeni ekle") ||
            normQuery.Contains("son ekle") ||
            normQuery.Contains("yeni yukle") ||
            normQuery.Contains("son yukle") ||
            normQuery.Contains("yeni kaynak") ||
            normQuery.Contains("yeni dosya") ||
            normQuery.Contains("yeni video") ||
            normQuery.Contains("yeni dokuman") ||
            normQuery.Contains("ekledigim kaynak") ||
            normQuery.Contains("ekledigim dosya") ||
            normQuery.Contains("eklenen kaynak") ||
            normQuery.Contains("atanmayan kaynak") ||
            normQuery.Contains("disarida kalan kaynak");

        var docs = await _dbContext.Dokumanlar.AsNoTracking()
            .Include(d => d.DokumanMetin)
            .Where(d => d.EgitimId == egitimId)
            .OrderByDescending(d => d.OlusturmaTarihi)
            .ToListAsync(ct);

        var vids = await _dbContext.Videolar.AsNoTracking()
            .Include(v => v.Summary)
            .Include(v => v.Transcript)
            .Where(v => v.EgitimId == egitimId)
            .OrderByDescending(v => v.OlusturmaTarihi)
            .ToListAsync(ct);

        var candidates = new List<(Guid Id, string Name, string Kind, string Mode, DateTime Created, string Content, int Score)>();

        foreach (var d in docs)
        {
            int score = ScoreTitleMention(normQuery, d.DosyaAdi);
            bool isAddedAfterPlan = planCreatedAtUtc.HasValue && d.OlusturmaTarihi > planCreatedAtUtc.Value;
            bool isUnassigned = alreadyAssignedIds != null && alreadyAssignedIds.Count > 0 && !alreadyAssignedIds.Contains(d.Id);

            if (score > 0 || (asksNewOrUnassigned && (isAddedAfterPlan || isUnassigned)))
            {
                var body = BuildSourceExcerpt(
                    d.DokumanMetin?.CevrilmisOzetMetni ?? d.DokumanMetin?.DamitilmisOzetMetni,
                    d.DokumanMetin?.HamMetin,
                    asksNewOrUnassigned && score == 0 ? 12000 : 24000);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    int finalScore = score > 0 ? score : (isAddedAfterPlan ? 120 : 70);
                    var label = isAddedAfterPlan ? "Doküman - Plan Sonrası Yeni Eklendi" : "Doküman";
                    candidates.Add((d.Id, d.DosyaAdi, label, d.KullanimTuru.ToString(), d.OlusturmaTarihi, body, finalScore));
                }
            }
        }

        foreach (var v in vids)
        {
            int score = ScoreTitleMention(normQuery, v.Baslik);
            bool isAddedAfterPlan = planCreatedAtUtc.HasValue && v.OlusturmaTarihi > planCreatedAtUtc.Value;
            bool isUnassigned = alreadyAssignedIds != null && alreadyAssignedIds.Count > 0 && !alreadyAssignedIds.Contains(v.Id);

            if (score > 0 || (asksNewOrUnassigned && (isAddedAfterPlan || isUnassigned)))
            {
                var body = BuildSourceExcerpt(
                    v.Summary?.CevrilmisOzetMetni ?? v.Summary?.OzetMetni,
                    v.Transcript?.HamMetin,
                    asksNewOrUnassigned && score == 0 ? 12000 : 24000);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    int finalScore = score > 0 ? score : (isAddedAfterPlan ? 120 : 70);
                    var label = isAddedAfterPlan ? "Video - Plan Sonrası Yeni Eklendi" : "Video";
                    candidates.Add((v.Id, v.Baslik, label, v.KullanimTuru.ToString(), v.OlusturmaTarihi, body, finalScore));
                }
            }
        }

        if (candidates.Count == 0 && asksNewOrUnassigned)
        {
            foreach (var latestVid in vids.Take(3))
            {
                var body = BuildSourceExcerpt(latestVid.Summary?.CevrilmisOzetMetni ?? latestVid.Summary?.OzetMetni, latestVid.Transcript?.HamMetin, 12000);
                if (!string.IsNullOrWhiteSpace(body))
                    candidates.Add((latestVid.Id, latestVid.Baslik, "Video (Son Eklenen)", latestVid.KullanimTuru.ToString(), latestVid.OlusturmaTarihi, body, 50));
            }
            foreach (var latestDoc in docs.Take(3))
            {
                var body = BuildSourceExcerpt(latestDoc.DokumanMetin?.CevrilmisOzetMetni ?? latestDoc.DokumanMetin?.DamitilmisOzetMetni, latestDoc.DokumanMetin?.HamMetin, 12000);
                if (!string.IsNullOrWhiteSpace(body))
                    candidates.Add((latestDoc.Id, latestDoc.DosyaAdi, "Doküman (Son Eklenen)", latestDoc.KullanimTuru.ToString(), latestDoc.OlusturmaTarihi, body, 50));
            }
        }

        var topMatches = candidates
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Created)
            .Take(8)
            .ToList();

        if (topMatches.Count == 0)
            return (ids, string.Empty, names);

        var sb = new StringBuilder();
        foreach (var m in topMatches)
        {
            ids.Add(m.Id);
            names.Add(m.Name.Length > 28 ? m.Name.Substring(0, 28) + "…" : m.Name);
            sb.AppendLine($"--- [{m.Kind}: {m.Name} | Mod: {m.Mode}] ---");
            sb.AppendLine(m.Content);
            sb.AppendLine();
        }

        return (ids, sb.ToString(), names);
    }

    private static string BuildSourceExcerpt(string? summary, string? rawText, int maxChars)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.AppendLine("[Damıtılmış Özet]:");
            sb.AppendLine(TruncateMiddleIfHuge(summary, maxChars / 2));
        }
        if (!string.IsNullOrWhiteSpace(rawText))
        {
            sb.AppendLine("[Kaynak Tam Metni]:");
            sb.AppendLine(TruncateMiddleIfHuge(rawText, string.IsNullOrWhiteSpace(summary) ? maxChars : maxChars / 2));
        }
        return sb.ToString().Trim();
    }

    private static int ScoreTitleMention(string normQuery, string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
            return 0;

        var strippedExt = Regex.Replace(rawTitle, @"\.(pdf|docx|txt|mp4|mkv|mp3|wav)$", "", RegexOptions.IgnoreCase);
        var normTitle = NormalizeForMatch(strippedExt);
        if (normTitle.Length < 4)
            return 0;

        if (normQuery.Contains(normTitle))
            return 200;

        var stopWords = new HashSet<string>
        {
            "video", "dokuman", "makale", "bolum", "ders", "kitap", "risalesi", "uzerine",
            "ozgur", "irade", "free", "will", "and", "the", "for", "from", "with", "prof"
        };

        var tokens = normTitle
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 4 && !int.TryParse(t, out _) && !stopWords.Contains(t))
            .Distinct()
            .ToList();

        if (tokens.Count == 0)
            return 0;

        int matchedCount = 0;
        int longestMatch = 0;
        foreach (var tok in tokens)
        {
            if (normQuery.Contains(tok))
            {
                matchedCount++;
                if (tok.Length > longestMatch)
                    longestMatch = tok.Length;
            }
        }

        if (matchedCount >= 2)
            return matchedCount * 35 + longestMatch;
        if (matchedCount == 1 && longestMatch >= 6)
            return 25 + longestMatch;

        return 0;
    }

    private static string NormalizeForMatch(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var s = text.ToLowerInvariant()
            .Replace('ı', 'i')
            .Replace('İ', 'i')
            .Replace('ş', 's')
            .Replace('ğ', 'g')
            .Replace('ü', 'u')
            .Replace('ö', 'o')
            .Replace('ç', 'c')
            .Replace('â', 'a')
            .Replace('î', 'i')
            .Replace('û', 'u');

        return Regex.Replace(s, @"[^a-z0-9]+", " ").Trim();
    }

    private static StudioQaMessageDto MapToDto(StudioQaMessage m)
    {
        StudioQaSuggestionDto? sug = null;
        if (!string.IsNullOrWhiteSpace(m.OneriJson))
        {
            try
            {
                sug = JsonSerializer.Deserialize<StudioQaSuggestionDto>(m.OneriJson, JsonOpts);
            }
            catch { }
        }

        return new StudioQaMessageDto
        {
            Id = m.Id,
            Kapsam = m.Kapsam,
            BolumNo = m.BolumNo,
            KaynakId = m.KaynakId,
            Rol = m.Rol,
            Mesaj = m.Mesaj,
            Oneri = sug,
            KullanilanBaglamOzeti = m.KullanilanBaglamOzeti,
            OlusturmaTarihi = m.OlusturmaTarihi
        };
    }
}
