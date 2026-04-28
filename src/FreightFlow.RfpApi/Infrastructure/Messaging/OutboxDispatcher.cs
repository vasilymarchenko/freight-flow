using System.Text.Json;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FreightFlow.RfpApi.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public int PollIntervalSeconds { get; set; } = 5;
}

public sealed class OutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory            _scopeFactory;
    private readonly IOptionsMonitor<OutboxOptions>  _options;
    private readonly ILogger<OutboxDispatcher>       _logger;

    public OutboxDispatcher(
        IServiceScopeFactory           scopeFactory,
        IOptionsMonitor<OutboxOptions> options,
        ILogger<OutboxDispatcher>      logger)
    {
        _scopeFactory = scopeFactory;
        _options      = options;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox dispatcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbox dispatcher error — will retry after poll interval");
            }

            var delay = TimeSpan.FromSeconds(_options.CurrentValue.PollIntervalSeconds);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope  = _scopeFactory.CreateAsyncScope();
        var db                 = scope.ServiceProvider.GetRequiredService<RfpDbContext>();
        var publishEndpoint    = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var messages = await db.OutboxMessages
            .Where(m => m.SentAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (messages.Count == 0) return;

        foreach (var message in messages)
        {
            try
            {
                var type = Type.GetType(message.MessageType)
                    ?? throw new InvalidOperationException(
                        $"Cannot resolve type '{message.MessageType}'.");

                var payload = JsonSerializer.Deserialize(message.Payload, type)
                    ?? throw new InvalidOperationException(
                        $"Cannot deserialize outbox message {message.Id}.");

                await publishEndpoint.Publish(payload, type, ct);

                message.MarkSent();

                _logger.LogInformation(
                    "Dispatched outbox message {MessageId} of type {MessageType}",
                    message.Id, type.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to dispatch outbox message {MessageId} — will retry",
                    message.Id);
            }
        }

        // Persist all MarkSent updates in a single round-trip.
        await db.SaveChangesAsync(ct);
    }
}
