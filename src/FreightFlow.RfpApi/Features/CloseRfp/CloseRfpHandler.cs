using System.Text.Json;
using FreightFlow.RfpApi.Domain.Events;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.RfpApi.Features.CloseRfp;

public sealed class CloseRfpHandler
{
    private readonly RfpDbContext              _db;
    private readonly ILogger<CloseRfpHandler> _logger;

    public CloseRfpHandler(RfpDbContext db, ILogger<CloseRfpHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task HandleAsync(Guid rfpId, CancellationToken ct = default)
    {
        var rfp = await _db.Rfps
            .FirstOrDefaultAsync(r => r.Id == RfpId.From(rfpId), ct)
            ?? throw new RfpNotFoundException(rfpId);

        rfp.Close();

        var evt = rfp.DomainEvents.OfType<RfpClosed>().First();
        _db.OutboxMessages.Add(OutboxMessage.Create(
            typeof(RfpClosed).AssemblyQualifiedName!,
            JsonSerializer.Serialize(evt)));

        await _db.SaveChangesAsync(ct);
        rfp.ClearDomainEvents();

        _logger.LogInformation("RFP {RfpId} closed", rfpId);
    }
}
