using System.Text.Json;
using FreightFlow.RfpApi.Domain;
using FreightFlow.RfpApi.Domain.Events;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;

namespace FreightFlow.RfpApi.Features.CreateRfp;

public sealed class CreateRfpHandler
{
    private readonly RfpDbContext                _db;
    private readonly ILogger<CreateRfpHandler>  _logger;

    public CreateRfpHandler(RfpDbContext db, ILogger<CreateRfpHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<RfpId> HandleAsync(CreateRfpCommand command, CancellationToken ct = default)
    {
        var rfp = Rfp.Create(
            command.ShipperId,
            command.OpenAt,
            command.CloseAt,
            command.MaxBidRounds);

        _db.Rfps.Add(rfp);

        // Outbox row goes into the same SaveChanges call — atomicity guaranteed.
        var evt = rfp.DomainEvents.OfType<RfpCreated>().First();
        _db.OutboxMessages.Add(OutboxMessage.Create(
            typeof(RfpCreated).AssemblyQualifiedName!,
            JsonSerializer.Serialize(evt)));

        await _db.SaveChangesAsync(ct);
        rfp.ClearDomainEvents();

        _logger.LogInformation(
            "RFP {RfpId} created for shipper {ShipperId}",
            rfp.Id, rfp.ShipperId);

        return rfp.Id;
    }
}
