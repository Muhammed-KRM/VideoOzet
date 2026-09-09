using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        
        builder.Property(u => u.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(200).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(u => u.FullName).HasColumnName("full_name").HasMaxLength(150).IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasDefaultValue(VideoOzet.Data.Enums.UserRole.User).IsRequired();
        builder.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(u => u.RefreshToken).HasColumnName("refresh_token").HasMaxLength(200);
        builder.Property(u => u.RefreshTokenExpiryTime).HasColumnName("refresh_token_expiry_time").HasColumnType("timestamptz");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").HasColumnType("timestamptz").IsRequired();
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");

        builder.HasIndex(u => u.Email).IsUnique();
    }
}

