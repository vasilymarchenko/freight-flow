namespace FreightFlow.RfpApi.Infrastructure.Persistence;

public sealed class OutboxMessage
{
    public Guid            Id          { get; private set; }
    public string          MessageType { get; private set; } = string.Empty;
    public string          Payload     { get; private set; } = string.Empty;
    public DateTimeOffset  CreatedAt   { get; private set; }
    public DateTimeOffset? SentAt      { get; private set; }

    private OutboxMessage() { }  // EF Core

    public static OutboxMessage Create(string messageType, string payload) => new()
    {
        Id          = Guid.NewGuid(),
        MessageType = messageType,
        Payload     = payload,
        CreatedAt   = DateTimeOffset.UtcNow
    };

    public void MarkSent() => SentAt = DateTimeOffset.UtcNow;
}
