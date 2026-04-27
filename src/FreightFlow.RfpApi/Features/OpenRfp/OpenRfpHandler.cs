using System.Text.Json;
using FreightFlow.RfpApi.Domain.Events;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.RfpApi.Features.OpenRfp;

public sealed class OpenRfpHandler
{
    private readonly RfpDbContext             _db;
    private readonly ILogger<OpenRfpHandler> _logger;

    public OpenRfpHandler(RfpDbContext db, ILogger<OpenRfpHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task HandleAsync(Guid rfpId, CancellationToken ct = default)
    {
        var rfp = await _db.Rfps
            .FirstOrDefaultAsync(r => r.Id == RfpId.From(rfpId), ct)
            ?? throw new RfpNotFoundException(rfpId);

        rfp.Open();

        var evt = rfp.DomainEvents.OfType<RfpOpened>().First();
        _db.OutboxMessages.Add(OutboxMessage.Create(
            typeof(RfpOpened).AssemblyQualifiedName!,
            JsonSerializer.Serialize(evt)));

        await _db.SaveChangesAsync(ct);
        rfp.ClearDomainEvents();

        _logger.LogInformation("RFP {RfpId} opened", rfpId);
    }
}
