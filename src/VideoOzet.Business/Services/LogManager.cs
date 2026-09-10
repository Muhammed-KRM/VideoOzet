using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;
using VideoOzet.Business.Utils;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Services;

public class LogManager : ILogService
{
    private readonly AppDbContext _context;
    private readonly ILogger<LogManager> _logger;

    public LogManager(AppDbContext context, ILogger<LogManager> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogEndpointAsync(EndpointLogEntry entry)
    {
        try
        {
            var log = new EndpointLog
            {
                TraceId = entry.TraceId,
                Method = entry.Method,
                Path = entry.Path,
                Query = entry.Query,
                RequestBody = SensitiveDataMasker.MaskJson(entry.RequestBody),
                ResponseBody = entry.ResponseBody,
                StatusCode = entry.StatusCode,
                IpAddress = entry.IpAddress,
                UserAgent = entry.UserAgent,
                DurationMs = entry.DurationMs,
                CreatedAt = DateTime.UtcNow
            };

            _context.EndpointLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write endpoint log to database.");
        }
    }

    public async Task<long> LogPipelineStartAsync(Guid? videoId, Guid? egitimId, PipelineAsamasi asama, string? traceId = null)
    {
        var log = new PipelineLog
        {
            VideoId = videoId,
            EgitimId = egitimId,
            Asama = asama,
            Durum = "Basladi",
            TraceId = traceId,
            BaslangicZamani = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.PipelineLogs.Add(log);
        await _context.SaveChangesAsync();
        return log.Id;
    }

    public async Task LogPipelineEndAsync(long logId, string? ciktiMetadata)
    {
        var log = await _context.PipelineLogs.FindAsync(logId);
        if (log != null)
        {
            log.Durum = "Tamamlandi";
            log.BitisZamani = DateTime.UtcNow;
            if (log.BaslangicZamani != default)
            {
                log.SureMs = (int)(log.BitisZamani.Value - log.BaslangicZamani).TotalMilliseconds;
            }
            log.CiktiMetadata = SensitiveDataMasker.MaskJson(ciktiMetadata ?? "{}");
            await _context.SaveChangesAsync();
        }
    }

    public async Task LogPipelineErrorAsync(long logId, Exception ex)
    {
        var log = await _context.PipelineLogs.FindAsync(logId);
        if (log != null)
        {
            log.Durum = "Hata";
            log.BitisZamani = DateTime.UtcNow;
            if (log.BaslangicZamani != default)
            {
                log.SureMs = (int)(log.BitisZamani.Value - log.BaslangicZamani).TotalMilliseconds;
            }
            log.HataMesaji = ex.Message;
            log.HataDetayi = ex.StackTrace;
            await _context.SaveChangesAsync();
        }
    }

    public async Task LogFunctionErrorAsync(
        string functionName,
        Exception ex,
        object? parameters = null,
        string? traceId = null,
        string errorCode = "FUNC_ERR",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            // Önce normal ILogger ile konsola/dosyaya yaz
            _logger.LogError(ex, "Function error in {FunctionName} [{ErrorCode}]", functionName, errorCode);

            // Sonra DB'ye detaylı kaydet
            string paramJson = "{}";
            if (parameters != null)
            {
                try { paramJson = JsonSerializer.Serialize(parameters); }
                catch { paramJson = parameters.ToString() ?? "{}"; }
            }

            var className = Path.GetFileNameWithoutExtension(filePath);

            var log = new FunctionLog
            {
                ErrorCode = errorCode,
                ClassName = string.IsNullOrEmpty(className) ? "Unknown" : className,
                MethodName = functionName,
                FilePath = filePath,
                LineNumber = lineNumber,
                ErrorMessage = ex.Message,
                StackTrace = ex.StackTrace,
                InputType = parameters?.GetType().Name,
                InputValue = SensitiveDataMasker.MaskJson(paramJson),
                TraceId = traceId,
                Severity = ex is OutOfMemoryException or StackOverflowException ? "Critical" : "Error",
                CreatedAt = DateTime.UtcNow
            };

            _context.FunctionLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception dbEx)
        {
            _logger.LogError(dbEx, "Failed to write function error log to database.");
        }
    }
}
