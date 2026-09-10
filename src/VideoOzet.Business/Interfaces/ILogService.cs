using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;

namespace VideoOzet.Business.Interfaces;

public class EndpointLogEntry
{
    public string? TraceId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Query { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public int StatusCode { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int DurationMs { get; set; }
}

public interface ILogService
{
    Task LogEndpointAsync(EndpointLogEntry entry);

    Task<long> LogPipelineStartAsync(Guid? videoId, Guid? egitimId, PipelineAsamasi asama, string? traceId = null);
    
    Task LogPipelineEndAsync(long logId, string? ciktiMetadata);
    
    Task LogPipelineErrorAsync(long logId, Exception ex);

    Task LogFunctionErrorAsync(
        string functionName,
        Exception ex,
        object? parameters = null,
        string? traceId = null,
        string errorCode = "FUNC_ERR",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0);
}
