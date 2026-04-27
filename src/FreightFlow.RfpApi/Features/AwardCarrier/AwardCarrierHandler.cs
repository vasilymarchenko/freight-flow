using System.Text.Json;
using FreightFlow.RfpApi.Domain.Events;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.RfpApi.Features.AwardCarrier;

public sealed class AwardCarrierHandler
{
    private readonly RfpDbContext                _db;
    private readonly ILogger<AwardCarrierHandler> _logger;

    public AwardCarrierHandler(RfpDbContext db, ILogger<AwardCarrierHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task HandleAsync(
        Guid               rfpId,
        AwardCarrierCommand command,
        CancellationToken  ct = default)
    {
        // Load Bids so IssueAward can validate that the requested bid exists.
        var rfp = await _db.Rfps
            .Include(r => r.Bids)
            .FirstOrDefaultAsync(r => r.Id == RfpId.From(rfpId), ct)
            ?? throw new RfpNotFoundException(rfpId);

        var award = rfp.IssueAward(BidId.From(command.BidId));

        // AwardIssued triggers the Saga in WorkflowWorker — must be in the same transaction.
        var evt = rfp.DomainEvents.OfType<AwardIssued>().First();
        _db.OutboxMessages.Add(OutboxMessage.Create(
            typeof(AwardIssued).AssemblyQualifiedName!,
            JsonSerializer.Serialize(evt)));

        await _db.SaveChangesAsync(ct);
        rfp.ClearDomainEvents();

        _logger.LogInformation(
            "Award issued for RFP {RfpId} to carrier {CarrierId} via bid {BidId}",
            rfpId, award.CarrierId, command.BidId);
    }
}
