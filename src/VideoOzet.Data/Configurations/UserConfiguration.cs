using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.Email).IsRequired().HasMaxLength(150);
        builder.Property(u => u.FullName).IsRequired().HasMaxLength(100);
        
        // Cüzdan bakiyesi negatif olamaz tarzı constraint'ler database bazlı eklenebilir,
        // şimdilik EF bazında standart bırakıyoruz. Concurrency için lock eklenebilir.
        builder.Property(u => u.TokenBalance).HasDefaultValue(0);
    }
}
