using MassTransit;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class TokenManager : ITokenService
{
    // ═══════════════════════════════════════════════
    // HATA KODLARI — TokenManager (Prefix: TM)
    // ═══════════════════════════════════════════════
    private const string EC_BALANCE     = "TM-001"; // GetBalanceAsync
    private const string EC_PACKAGES    = "TM-002"; // GetPackagesAsync
    private const string EC_HISTORY     = "TM-003"; // GetTransactionHistoryAsync
    private const string EC_SPEND       = "TM-004"; // SpendTokenAsync
    private const string EC_ADD         = "TM-005"; // AddTokenAsync
    // ═══════════════════════════════════════════════

    private readonly IUserRepository _userRepo;
    private readonly IRepository<TokenTransaction> _transactionRepo;
    private readonly IRepository<TokenPackage> _packageRepo;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogService _logService;

    public TokenManager(
        IUserRepository userRepo,
        IRepository<TokenTransaction> transactionRepo,
        IRepository<TokenPackage> packageRepo,
        IPublishEndpoint publishEndpoint,
        ILogService logService)
    {
        _userRepo = userRepo;
        _transactionRepo = transactionRepo;
        _packageRepo = packageRepo;
        _publishEndpoint = publishEndpoint;
        _logService = logService;
    }

    public async Task<int> GetBalanceAsync(Guid userId)
    {
        try
        {
            var user = await _userRepo.GetByIdAsync(userId) ?? throw new NotFoundException("Kullanıcı", userId);
            return user.TokenBalance;
        }
        catch (NotFoundException) { throw; }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_BALANCE, ex, userId, userId); throw; }
    }

    public async Task<List<TokenPackageDto>> GetPackagesAsync()
    {
        try
        {
            var packages = await _packageRepo.GetAllAsync();
            return packages.Select(p => new TokenPackageDto
            {
                Id = p.Id, Name = p.Name, TokenCount = p.TokenCount,
                Price = p.Price, IsPopular = p.IsPopular, BadgeText = p.BadgeText
            }).ToList();
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_PACKAGES, ex); throw; }
    }

    public async Task<List<TokenTransactionDto>> GetTransactionHistoryAsync(Guid userId)
    {
        try
        {
            var transactions = await _transactionRepo.FindAsync(t => t.UserId == userId);
            return transactions.OrderByDescending(t => t.CreatedAt).Select(t => new TokenTransactionDto
            {
                Id = t.Id, Type = t.Type, Amount = t.Amount,
                Description = t.Description, CreatedAt = t.CreatedAt
            }).ToList();
        }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_HISTORY, ex, userId, userId); throw; }
    }

    public async Task SpendTokenAsync(Guid userId, int amount, string reason)
    {
        try
        {
        var user = await _userRepo.GetByIdAsync(userId) ?? throw new NotFoundException("Kullanıcı", userId);
        if (user.TokenBalance < amount) throw new InsufficientTokenException(user.TokenBalance, amount);

        user.TokenBalance -= amount;
        _userRepo.Update(user);
        await _transactionRepo.AddAsync(new TokenTransaction
        {
            UserId = userId, Amount = -amount, Type = TransactionType.Spend, Description = reason
        });
        await _userRepo.SaveChangesAsync();
        }
        catch (NotFoundException) { throw; }
        catch (InsufficientTokenException) { throw; }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_SPEND, ex, new { userId, amount, reason }, userId); throw; }
    }

    public async Task AddTokenAsync(Guid userId, int amount, string reason)
    {
        try
        {
        var user = await _userRepo.GetByIdAsync(userId) ?? throw new NotFoundException("Kullanıcı", userId);
        user.TokenBalance += amount;
        _userRepo.Update(user);
        await _transactionRepo.AddAsync(new TokenTransaction
        {
            UserId = userId, Amount = amount, Type = TransactionType.Purchase, Description = reason
        });
        await _userRepo.SaveChangesAsync();

        // Jeton yükleme bildirimi — await ile, hata loglanır
        try
        {
            await _publishEndpoint.Publish(new SendNotificationEvent
            {
                UserId = userId,
                Type = "TokenLoaded",
                Title = "Jeton Yüklendi 💰",
                Message = $"{amount} jeton hesabınıza yüklendi. Yeni bakiyeniz: {user.TokenBalance} jeton.",
                ActionUrl = "/panel/jetonlarim",
                SendEmail = false,
                IdempotencyKey = $"token-{userId}-{DateTime.UtcNow:yyyyMMddHHmm}"
            });
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync("TM-NOTIF", ex, new { userId, amount });
        }
        }
        catch (NotFoundException) { throw; }
        catch (Exception ex) { await _logService.LogFunctionErrorAsync(EC_ADD, ex, new { userId, amount, reason }, userId); throw; }
    }
}
