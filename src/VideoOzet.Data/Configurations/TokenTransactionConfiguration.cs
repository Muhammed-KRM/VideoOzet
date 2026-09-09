using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class TokenTransactionConfiguration : IEntityTypeConfiguration<TokenTransaction>
{
    public void Configure(EntityTypeBuilder<TokenTransaction> builder)
    {
        builder.HasKey(t => t.Id);
        
        builder.Property(t => t.Description).IsRequired().HasMaxLength(250);
        builder.Property(t => t.ReferenceId).HasMaxLength(100);

        builder.HasOne(t => t.User)
            .WithMany(u => u.TokenTransactions)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
