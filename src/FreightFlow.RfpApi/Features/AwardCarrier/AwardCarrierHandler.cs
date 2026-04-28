using System.Text.Json;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;
using SharedContracts = FreightFlow.SharedKernel;

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
        // Load Bids + LanePrices + Lanes — all needed to build the AwardIssued integration event.
        var rfp = await _db.Rfps
            .Include(r => r.Lanes)
            .Include(r => r.Bids)
            .FirstOrDefaultAsync(r => r.Id == RfpId.From(rfpId), ct)
            ?? throw new RfpNotFoundException(rfpId);

        var award = rfp.IssueAward(BidId.From(command.BidId));

        // Build the enriched integration event (SharedKernel.AwardIssued) so the
        // WorkflowWorker saga has everything it needs without a cross-service query.
        // Use the first lane price from the winning bid as the primary lane/rate.
        var winningBid  = rfp.Bids.FirstOrDefault(b => b.Id == award.BidId)
            ?? throw new DomainException($"Winning bid {award.BidId} not found on RFP {rfpId}.");
        var firstPrice  = winningBid.LanePrices.FirstOrDefault()
            ?? throw new DomainException($"Winning bid {award.BidId} has no lane prices.");
        var primaryLane = rfp.Lanes.FirstOrDefault(l => l.Id == firstPrice.LaneId)
            ?? throw new DomainException($"Lane {firstPrice.LaneId} not found on RFP {rfpId}.");

        var integrationEvent = new SharedContracts.AwardIssued(
            RfpId:           rfp.Id,
            BidId:           award.BidId,
            CarrierId:       award.CarrierId,
            LaneId:          firstPrice.LaneId,
            AgreedRate:      firstPrice.Price,
            VolumeToReserve: primaryLane.Volume,
            OccurredAt:      DateTimeOffset.UtcNow);

        // AwardIssued triggers the Saga in WorkflowWorker — must be in the same transaction.
        _db.OutboxMessages.Add(OutboxMessage.Create(
            typeof(SharedContracts.AwardIssued).AssemblyQualifiedName!,
            JsonSerializer.Serialize(integrationEvent)));

        await _db.SaveChangesAsync(ct);
        rfp.ClearDomainEvents();

        _logger.LogInformation(
            "Award issued for RFP {RfpId} to carrier {CarrierId} via bid {BidId}",
            rfpId, award.CarrierId, command.BidId);
    }
}
