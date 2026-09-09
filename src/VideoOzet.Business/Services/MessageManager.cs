using FluentValidation;
using Ganss.Xss;
using MassTransit;
using VideoOzet.Business.DTOs;
using VideoOzet.Business.Events;
using VideoOzet.Business.Exceptions;
using VideoOzet.Business.Interfaces;
using VideoOzet.Data.Entities;
using VideoOzet.Data.Enums;
using VideoOzet.Data.Repositories;

namespace VideoOzet.Business.Services;

public class MessageManager : IMessageService
{
    // ═══════════════════════════════════════════════
    // HATA KODLARI — MessageManager (Prefix: MM)
    // ═══════════════════════════════════════════════
    private const string EC_INBOX       = "MM-001"; // GetInboxAsync
    private const string EC_CONVO       = "MM-002"; // GetConversationAsync
    private const string EC_SEND        = "MM-003"; // SendMessageAsync
    private const string EC_UNLOCK      = "MM-004"; // UnlockMessageAsync
    // ═══════════════════════════════════════════════

    private readonly IRepository<Message> _messageRepo;
    private readonly ITokenService _tokenService;
    private readonly IValidator<MessageSendDto> _sendValidator;
    private readonly ISettingService _settingService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogService _logService;

    public MessageManager(
        IRepository<Message> messageRepo,
        ITokenService tokenService,
        IValidator<MessageSendDto> sendValidator,
        ISettingService settingService,
        IPublishEndpoint publishEndpoint,
        ILogService logService)
    {
        _messageRepo = messageRepo;
        _tokenService = tokenService;
        _sendValidator = sendValidator;
        _settingService = settingService;
        _publishEndpoint = publishEndpoint;
        _logService = logService;
    }

    public async Task<List<MessageDto>> GetInboxAsync(Guid userId)
    {
        try
        {
            var messages = await _messageRepo.FindAsync(m => m.ReceiverId == userId);
            return messages.OrderByDescending(m => m.CreatedAt).Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_INBOX, ex, userId, userId);
            throw;
        }
    }

    public async Task<List<MessageDto>> GetConversationAsync(Guid userId, Guid otherUserId)
    {
        try
        {
            var messages = await _messageRepo.FindAsync(m =>
                (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                (m.SenderId == otherUserId && m.ReceiverId == userId));
            return messages.OrderBy(m => m.CreatedAt).Select(MapToDto).ToList();
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_CONVO, ex, new { userId, otherUserId }, userId);
            throw;
        }
    }

    /// <summary>
    /// Mesaj gönderme — İki senaryo desteklenir:
    /// Senaryo A (Normal): İlan üzerinden ücretsiz mesaj. İlan sahibi jeton harcayarak açar.
    /// Senaryo B (Direkt Teklif): Gönderen 1 jeton harcar, alıcı mesajı zaten açık olarak görür.
    /// </summary>
    public async Task<MessageDto> SendMessageAsync(MessageSendDto dto, Guid senderId)
    {
        try
        {
        var validationResult = await _sendValidator.ValidateAsync(dto);
        if (!validationResult.IsValid)
            throw new BusinessException(string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));

        var sanitizer = new HtmlSanitizer();
        var message = new Message
        {
            SenderId = senderId,
            ReceiverId = dto.ReceiverId,
            ListingId = dto.ListingId,
            Content = sanitizer.Sanitize(dto.Content),
            IsInitiatedWithToken = dto.IsDirectOffer,
        };

        if (dto.IsDirectOffer)
        {
            var cost = await _settingService.GetIntSettingAsync("DirectOfferCost", 2);
            await _tokenService.SpendTokenAsync(senderId, cost, "Direkt teklif mesajı gönderildi");
            message.Status = MessageStatus.Unlocked;
        }
        else
        {
            message.Status = MessageStatus.Locked;
        }

        await _messageRepo.AddAsync(message);
        await _messageRepo.SaveChangesAsync();

        // Alıcıya bildirim gönder
        try
        {
            await _publishEndpoint.Publish(new SendNotificationEvent
            {
                UserId = dto.ReceiverId,
                Type = "MessageReceived",
                Title = "Yeni Mesajınız Var 💬",
                Message = dto.IsDirectOffer
                    ? "Birileri size direkt teklif gönderdi."
                    : "İlanınıza yeni bir mesaj geldi.",
                ActionUrl = "/panel/mesajlarim",
                SendEmail = true,
                IdempotencyKey = $"msg-{message.Id}"
            });
        }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync("MSG-NOTIF", ex, new { messageId = message.Id });
        }

        return MapToDto(message);
        }
        catch (BusinessException) { throw; }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_SEND, ex, dto, senderId);
            throw;
        }
    }

    /// <summary>
    /// İlan sahibi kilitli mesajı 1 jeton harcayarak açar (Senaryo A).
    /// </summary>
    public async Task UnlockMessageAsync(Guid messageId, Guid userId)
    {
        try
        {
        var message = (await _messageRepo.FindAsync(m => m.Id == messageId)).FirstOrDefault()
            ?? throw new NotFoundException("Mesaj", messageId);

        if (message.ReceiverId != userId)
            throw new UnauthorizedException("Bu mesajı açma yetkiniz yok.");

        if (message.Status == MessageStatus.Unlocked)
            throw new BusinessException("Bu mesaj zaten açılmış.");

        var previouslyUnlocked = await _messageRepo.FindAsync(m =>
            m.SenderId == message.SenderId &&
            m.ReceiverId == userId &&
            m.Status == MessageStatus.Unlocked);

        if (!previouslyUnlocked.Any())
        {
            var cost = await _settingService.GetIntSettingAsync("MessageUnlockCost", 1);
            await _tokenService.SpendTokenAsync(userId, cost, "Mesaj kilidi açıldı");
        }

        message.Status = MessageStatus.Unlocked;
        message.ReadAt = DateTime.UtcNow;
        _messageRepo.Update(message);
        await _messageRepo.SaveChangesAsync();
        }
        catch (BusinessException) { throw; }
        catch (Exception ex)
        {
            await _logService.LogFunctionErrorAsync(EC_UNLOCK, ex, new { messageId }, userId);
            throw;
        }
    }

    private static MessageDto MapToDto(Message m) => new()
    {
        Id = m.Id,
        SenderId = m.SenderId,
        SenderName = m.Sender?.FullName ?? "",
        SenderImageUrl = m.Sender?.ProfileImageUrl,
        ReceiverId = m.ReceiverId,
        ListingId = m.ListingId,
        ListingTitle = m.Listing?.Title,
        Content = m.Content,
        IsInitiatedWithToken = m.IsInitiatedWithToken,
        IsUnlocked = m.Status == MessageStatus.Unlocked,
        Status = m.Status,
        CreatedAt = m.CreatedAt,
        ReadAt = m.ReadAt
    };
}
