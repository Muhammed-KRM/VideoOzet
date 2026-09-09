using FluentValidation;
using VideoOzet.Business.DTOs.Video;
using System.IO;

namespace VideoOzet.Business.Validators;

public class VideoUploadValidator : AbstractValidator<VideoUploadDto>
{
    public VideoUploadValidator()
    {
        RuleFor(x => x.EgitimId)
            .NotEmpty().WithMessage("Eğitim ID boş olamaz.");

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Dosya adı boş olamaz.");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("İçerik türü boş olamaz.")
            .Must(ct => ct.StartsWith("video/")).WithMessage("Sadece video dosyaları yüklenebilir.");

        RuleFor(x => x.FileStream)
            .NotNull().WithMessage("Dosya içeriği boş olamaz.")
            .Must(fs => fs.Length > 0).WithMessage("Dosya boyutu 0'dan büyük olmalıdır.")
            .Must(fs => fs.Length <= 1024L * 1024L * 1024L * 2L).WithMessage("Dosya boyutu maksimum 2GB olabilir."); // 2GB
    }
}
