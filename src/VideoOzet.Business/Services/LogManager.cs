using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Context;
using VideoOzet.Data.Entities;

namespace VideoOzet.Business.Services;

public class LogManager : ILogService
{
    private readonly AppDbContext _db;

    public LogManager(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogEndpointAsync(EndpointLogEntry entry)
    {
        try
        {
            var log = new EndpointLog
            {
                TraceId    = entry.TraceId,
                Method     = entry.Method,
                Path       = entry.Path,
                Query      = entry.Query,
                RequestBody  = MaskSensitiveData(entry.RequestBody),
                ResponseBody = entry.ResponseBody,
                StatusCode = entry.StatusCode,
                UserId     = entry.UserId,
                UserEmail  = entry.UserEmail,
                IpAddress  = entry.IpAddress,
                UserAgent  = entry.UserAgent,
                DurationMs = entry.DurationMs,
                CreatedAt  = DateTime.UtcNow
            };

            _db.EndpointLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Log yazma hatası uygulamayı çökertmemeli
        }
    }

    public async Task LogFunctionErrorAsync(
        string errorCode,
        Exception ex,
        object? inputData = null,
        Guid? userId = null,
        string? traceId = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            var className = Path.GetFileNameWithoutExtension(filePath);

            string? inputValue = null;
            if (inputData is not null)
            {
                try { inputValue = JsonSerializer.Serialize(inputData); }
                catch { inputValue = inputData.ToString(); }
            }

            var log = new FunctionLog
            {
                ErrorCode    = errorCode,
                ClassName    = className,
                MethodName   = memberName,
                FilePath     = filePath,
                LineNumber   = lineNumber,
                ErrorMessage = ex.Message,
                StackTrace   = ex.StackTrace,
                InputType    = inputData?.GetType().Name,
                InputValue   = inputValue,
                UserId       = userId,
                TraceId      = traceId,
                Severity     = ex is OutOfMemoryException or StackOverflowException ? "Critical" : "Error",
                CreatedAt    = DateTime.UtcNow
            };

            _db.FunctionLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Log yazma hatası uygulamayı çökertmemeli
        }
    }

    private static string? MaskSensitiveData(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json;

        return Regex.Replace(
            json,
            @"""(password|token|refreshToken|aesKey|ibanEncrypted|tcknEncrypted)""\s*:\s*""[^""]*""",
            @"""$1"":""***""",
            RegexOptions.IgnoreCase);
    }
}
