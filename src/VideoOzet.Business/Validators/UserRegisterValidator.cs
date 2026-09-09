using FluentValidation;
using VideoOzet.Business.DTOs;

namespace VideoOzet.Business.Validators;

public class UserRegisterValidator : AbstractValidator<UserRegisterDto>
{
    public UserRegisterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta adresi zorunludur.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Şifre zorunludur.")
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")
            .Matches(@"[A-Z]").WithMessage("Şifre en az bir büyük harf içermelidir.")
            .Matches(@"[a-z]").WithMessage("Şifre en az bir küçük harf içermelidir.")
            .Matches(@"[0-9]").WithMessage("Şifre en az bir rakam içermelidir.");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Ad Soyad zorunludur.")
            .MinimumLength(3).WithMessage("Ad Soyad en az 3 karakter olmalıdır.")
            .MaximumLength(100).WithMessage("Ad Soyad en fazla 100 karakter olabilir.");
    }
}
