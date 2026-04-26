using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FreightFlow.CarrierApi.Infrastructure.Persistence;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.MessageType).HasColumnName("message_type").HasMaxLength(500);
        builder.Property(o => o.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.SentAt).HasColumnName("sent_at");
    }
}
