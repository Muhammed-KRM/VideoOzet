using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class TokenPackageConfiguration : IEntityTypeConfiguration<TokenPackage>
{
    public void Configure(EntityTypeBuilder<TokenPackage> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Price).HasColumnType("decimal(18,2)");
        
        builder.HasData(
            new TokenPackage { Id = 1, Name = "Başlangıç", TokenCount = 5, Price = 99m, IsPopular = false, BadgeText = null },
            new TokenPackage { Id = 2, Name = "Popüler", TokenCount = 10, Price = 149m, IsPopular = true, BadgeText = "⭐ En Çok Tercih Edilen" },
            new TokenPackage { Id = 3, Name = "Profesyonel", TokenCount = 25, Price = 299m, IsPopular = false, BadgeText = "🔥 En Avantajlı" },
            new TokenPackage { Id = 4, Name = "Kurumsal", TokenCount = 50, Price = 499m, IsPopular = false, BadgeText = "💎 Maksimum Tasarruf" }
        );
    }
}

public class VitrinPackageConfiguration : IEntityTypeConfiguration<VitrinPackage>
{
    public void Configure(EntityTypeBuilder<VitrinPackage> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).IsRequired().HasMaxLength(50);
        builder.Property(v => v.Price).HasColumnType("decimal(18,2)");
        
        builder.HasData(
            new VitrinPackage { Id = 1, Name = "Haftalık", DurationInDays = 7, Price = 79, IncludesAmberGlow = true, IncludesTopRanking = true, IncludesHomeCarousel = false },
            new VitrinPackage { Id = 2, Name = "Aylık", DurationInDays = 30, Price = 249, IncludesAmberGlow = true, IncludesTopRanking = true, IncludesHomeCarousel = false },
            new VitrinPackage { Id = 3, Name = "Premium", DurationInDays = 30, Price = 449, IncludesAmberGlow = true, IncludesTopRanking = true, IncludesHomeCarousel = true }
        );
    }
}
