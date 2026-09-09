using FluentValidation;
using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Validators;

public class ListingCreateValidator : AbstractValidator<ListingCreateDto>
{
    public ListingCreateValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("İlan başlığı zorunludur.")
            .MinimumLength(10).WithMessage("İlan başlığı en az 10 karakter olmalıdır.")
            .MaximumLength(150).WithMessage("İlan başlığı en fazla 150 karakter olabilir.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("İlan açıklaması zorunludur.")
            .MinimumLength(10).WithMessage("İlan açıklaması en az 10 karakter olmalıdır.")
            .MaximumLength(3000).WithMessage("İlan açıklaması en fazla 3000 karakter olabilir.");

        RuleFor(x => x.HourlyPrice)
            .GreaterThan(0).WithMessage("Saatlik ücret 0'dan büyük olmalıdır.")
            .LessThanOrEqualTo(10000).WithMessage("Saatlik ücret 10.000 TL'yi geçemez.");

        RuleFor(x => x.BranchId)
            .GreaterThan(0).WithMessage("Branş seçimi zorunludur.");

        RuleFor(x => x.DistrictId)
            .GreaterThan(0).WithMessage("İlçe seçimi zorunludur.");

        RuleFor(x => x.LessonType)
            .IsInEnum().WithMessage("Geçerli bir ders türü seçiniz.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Geçerli bir ilan türü seçiniz.");
    }
}
