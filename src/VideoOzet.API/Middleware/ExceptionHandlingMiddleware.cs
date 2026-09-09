using System.Net;
using System.Text.Json;
using VideoOzet.Business.Exceptions;

namespace VideoOzet.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Beklenmeyen hata: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var (statusCode, title, detail) = exception switch
        {
            InsufficientTokenException e => (StatusCodes.Status400BadRequest, "Yetersiz Jeton", e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, "Bulunamadı", e.Message),
            UnauthorizedException e => (StatusCodes.Status403Forbidden, "Yetkisiz Erişim", e.Message),
            BusinessException e => (StatusCodes.Status400BadRequest, "İş Kuralı Hatası", e.Message),
            _ => (StatusCodes.Status500InternalServerError, "Sunucu Hatası", "Beklenmeyen bir hata oluştu.")
        };

        context.Response.StatusCode = statusCode;

        // RFC 7807 ProblemDetails formatı
        var problemDetails = new
        {
            type = $"https://VideoOzet.com/errors/{statusCode}",
            title,
            status = statusCode,
            detail,
            traceId = context.TraceIdentifier
        };

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
