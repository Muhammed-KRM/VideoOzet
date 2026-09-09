using System.Runtime.CompilerServices;

namespace VideoOzet.Business.Interfaces;

public interface ILogService
{
    Task LogEndpointAsync(EndpointLogEntry entry);

    Task LogFunctionErrorAsync(
        string errorCode,
        Exception ex,
        object? inputData = null,
        Guid? userId = null,
        string? traceId = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0);
}

public class EndpointLogEntry
{
    public string? TraceId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Query { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public int StatusCode { get; set; }
    public Guid? UserId { get; set; }
    public string? UserEmail { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int DurationMs { get; set; }
}
