using System.Security.Claims;
using System.Text;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.API.Middleware;

public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly string[] SkipPaths =
        ["/swagger", "/favicon", "/uploads", "/_blazor", "/health"];

    public RequestResponseLoggingMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (SkipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        context.Request.EnableBuffering();
        var requestBody = await ReadBodyAsync(context.Request.Body);
        context.Request.Body.Position = 0;

        var originalResponseBody = context.Response.Body;
        using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        await _next(context);

        stopwatch.Stop();

        responseBuffer.Position = 0;
        var responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();
        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalResponseBody);
        context.Response.Body = originalResponseBody;

        Guid? userId = null;
        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdClaim, out var parsedId)) userId = parsedId;
        var userEmail = context.User.FindFirstValue(ClaimTypes.Email);

        var entry = new EndpointLogEntry
        {
            TraceId      = context.TraceIdentifier,
            Method       = context.Request.Method,
            Path         = path,
            Query        = context.Request.QueryString.Value,
            RequestBody  = requestBody,
            ResponseBody = responseBody.Length > 5000
                ? responseBody[..5000] + "...[truncated]"
                : responseBody,
            StatusCode   = context.Response.StatusCode,
            UserId       = userId,
            UserEmail    = userEmail,
            IpAddress    = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent    = context.Request.Headers.UserAgent.ToString(),
            DurationMs   = (int)stopwatch.ElapsedMilliseconds
        };

        // Yeni scope ile log yaz — request scope'u dispose olsa bile güvenli
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var logService = scope.ServiceProvider.GetRequiredService<ILogService>();
                await logService.LogEndpointAsync(entry);
            }
            catch
            {
                // Log hatası ana akışı etkilemesin
            }
        });
    }

    private static async Task<string?> ReadBodyAsync(Stream body)
    {
        if (!body.CanRead || body.Length == 0) return null;
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
