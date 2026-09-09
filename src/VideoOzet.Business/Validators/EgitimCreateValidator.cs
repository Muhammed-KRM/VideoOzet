using FluentValidation;
using VideoOzet.Business.DTOs.Egitim;

namespace VideoOzet.Business.Validators;

public class EgitimCreateValidator : AbstractValidator<EgitimCreateDto>
{
    public EgitimCreateValidator()
    {
        RuleFor(x => x.Ad)
            .NotEmpty().WithMessage("Eğitim adı boş olamaz.")
            .MaximumLength(200).WithMessage("Eğitim adı en fazla 200 karakter olabilir.");

        RuleFor(x => x.Aciklama)
            .MaximumLength(1000).WithMessage("Açıklama en fazla 1000 karakter olabilir.");
    }
}
