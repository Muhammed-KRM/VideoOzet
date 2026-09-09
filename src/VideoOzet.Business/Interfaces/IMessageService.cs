using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Interfaces;

public interface IMessageService
{
    Task<List<MessageDto>> GetInboxAsync(Guid userId);
    Task<List<MessageDto>> GetConversationAsync(Guid userId, Guid otherUserId);
    Task<MessageDto> SendMessageAsync(MessageSendDto dto, Guid senderId);
    Task UnlockMessageAsync(Guid messageId, Guid userId);
}
