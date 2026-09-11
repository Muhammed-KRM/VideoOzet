using FluentValidation;
using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Validators;

public class CreateContentRequestDtoValidator : AbstractValidator<CreateContentRequestDto>
{
    public CreateContentRequestDtoValidator()
    {
        RuleFor(x => x.Konu)
            .NotEmpty().WithMessage("Konu boş olamaz.")
            .MaximumLength(10000).WithMessage("Konu en fazla 10.000 karakter olabilir.");

        RuleFor(x => x.HedefUzunluk)
            .MaximumLength(100).WithMessage("Hedef uzunluk en fazla 100 karakter olabilir.");

        RuleFor(x => x.HedefKitle)
            .MaximumLength(100).WithMessage("Hedef kitle en fazla 100 karakter olabilir.");
    }
}
