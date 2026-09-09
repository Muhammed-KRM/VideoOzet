using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoOzet.Data.Entities;

namespace VideoOzet.Data.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Content).IsRequired().HasMaxLength(2000);

        // Gönderen ilişkisi (Multiple Cascade Path hatasını önlemek için Restrict)
        builder.HasOne(m => m.Sender)
            .WithMany(u => u.SentMessages)
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Alan ilişkisi (Multiple Cascade Path hatasını önlemek için Restrict)
        builder.HasOne(m => m.Receiver)
            .WithMany(u => u.ReceivedMessages)
            .HasForeignKey(m => m.ReceiverId)
            .OnDelete(DeleteBehavior.Restrict);
            
        // İlan ilişkisi
        builder.HasOne(m => m.Listing)
            .WithMany()
            .HasForeignKey(m => m.ListingId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
