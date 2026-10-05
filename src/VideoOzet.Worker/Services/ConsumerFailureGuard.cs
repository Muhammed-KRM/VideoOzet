using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VideoOzet.Data.Context;

namespace VideoOzet.Worker.Services;

/// <summary>
/// Consumer'ların catch bloklarında kullanılan "asla fırlatmayan" hata kaydı yardımcıları.
///
/// Neden gerekli?
/// - Ana iş akışında SaveChanges başarısız olduğunda, aynı scoped DbContext'in ChangeTracker'ı
///   hatalı entity'leri tutmaya devam eder. LogService de aynı DbContext'i kullandığı için,
///   catch bloğundaki her SaveChanges aynı hatayı tekrar fırlatır.
/// - catch bloğundan kaçan istisna MassTransit retry politikasını tetikler ve TÜM iş akışı
///   (dakikalar süren LLM çağrıları dahil) baştan çalıştırılır → sonsuz/uzun hata döngüsü.
///
/// Bu yardımcılar: ChangeTracker'ı temizler, işlemi izole eder ve ikincil hatayı sadece loglar.
/// </summary>
public static class ConsumerFailureGuard
{
    /// <summary>
    /// Durumu "Hata" olarak işaretleme gibi bir telafi işlemini güvenli şekilde çalıştırır.
    /// <paramref name="markAction"/> sadece entity'leri değiştirmeli; SaveChanges burada çağrılır.
    /// </summary>
    public static async Task TryPersistFailureStateAsync(
        AppDbContext dbContext,
        ILogger logger,
        Func<AppDbContext, CancellationToken, Task> markAction)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            await markAction(dbContext, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hata durumu veritabanına yazılamadı (ikincil hata yutuldu, retry tetiklenmeyecek).");
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Loglama / bildirim gibi yan etkileri, ana hatayı maskelemeden ve retry tetiklemeden çalıştırır.
    /// </summary>
    public static async Task TryRunAsync(ILogger logger, string operationName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Operation} başarısız oldu (ikincil hata yutuldu).", operationName);
        }
    }
}
