namespace VideoOzet.API.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string APIKEYNAME = "X-API-Key";

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        // CORS preflight requests must pass without auth
        if (context.Request.Method == "OPTIONS")
        {
            await _next(context);
            return;
        }

        // Swagger UI, health checks, and SignalR hubs
        if (context.Request.Path.StartsWithSegments("/swagger") || 
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/hubs"))
        {
            await _next(context);
            return;
        }

        // Check header first, then query parameter
        string? extractedApiKey = null;
        if (context.Request.Headers.TryGetValue(APIKEYNAME, out var headerValue))
        {
            extractedApiKey = headerValue.ToString();
        }
        else if (context.Request.Query.TryGetValue("apiKey", out var queryValue) ||
                 context.Request.Query.TryGetValue("access_token", out queryValue))
        {
            extractedApiKey = queryValue.ToString();
        }

        if (string.IsNullOrEmpty(extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API Key was not provided.");
            return;
        }

        var appSettingsApiKey = configuration.GetValue<string>("ApiKey") 
                                ?? configuration.GetValue<string>("ADMIN_API_KEY");

        if (appSettingsApiKey == null || !appSettingsApiKey.Equals(extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized client.");
            return;
        }

        await _next(context);
    }
}
