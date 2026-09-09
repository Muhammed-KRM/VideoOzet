using FluentValidation;
using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Validators;

public class MessageSendValidator : AbstractValidator<MessageSendDto>
{
    public MessageSendValidator()
    {
        RuleFor(x => x.ReceiverId)
            .NotEmpty().WithMessage("Alıcı belirtilmelidir.");

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Mesaj içeriği boş olamaz.")
            .MinimumLength(5).WithMessage("Mesaj en az 5 karakter olmalıdır.")
            .MaximumLength(2000).WithMessage("Mesaj en fazla 2000 karakter olabilir.");
    }
}
