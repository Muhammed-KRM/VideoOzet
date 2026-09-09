using System.Security.Claims;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Repositories;

namespace VideoOzet.API.Middleware;

public class BanCheckMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly string[] AllowedPaths =
        ["/api/auth/login", "/api/auth/register", "/api/auth/refresh", "/health", "/swagger"];

    public BanCheckMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (AllowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await _next(context);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IRepository<User>>();
        var user = await userRepo.GetByIdAsync(userId);

        if (user?.BannedUntil != null && user.BannedUntil > DateTime.UtcNow)
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";

            var isPermanent = user.BannedUntil == DateTime.MaxValue;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "account_banned",
                message = isPermanent
                    ? "Hesabınız kalıcı olarak askıya alınmıştır."
                    : $"Hesabınız {user.BannedUntil:dd.MM.yyyy HH:mm} tarihine kadar askıya alınmıştır.",
                bannedUntil = isPermanent ? (DateTime?)null : user.BannedUntil,
                isPermanent,
                reason = user.BanReason
            });
            return;
        }

        await _next(context);
    }
}
