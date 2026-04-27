using System.Text.Json;
using FreightFlow.RfpApi.Domain;
using FreightFlow.RfpApi.Domain.Events;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.RfpApi.Features.SubmitBid;

public sealed class SubmitBidHandler
{
    private readonly RfpDbContext               _db;
    private readonly ILogger<SubmitBidHandler> _logger;

    public SubmitBidHandler(RfpDbContext db, ILogger<SubmitBidHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<BidId> HandleAsync(
        Guid            rfpId,
        SubmitBidCommand command,
        CancellationToken ct = default)
    {
        // Load with Bids so the domain can calculate the next round number.
        // Tracking is required because we mutate the aggregate.
        var rfp = await _db.Rfps
            .Include(r => r.Bids)
            .FirstOrDefaultAsync(r => r.Id == RfpId.From(rfpId), ct)
            ?? throw new RfpNotFoundException(rfpId);

        var lanePrices = command.LanePrices
            .Select(lp => new LanePrice(LaneId.From(lp.LaneId), new Money(lp.Amount, lp.Currency)))
            .ToList();

        // SubmitBid updates UpdatedAt → EF Core generates UPDATE rfps SET updated_at = ...
        // The WHERE xmin = <original> clause detects concurrent bid submissions.
        var bid = rfp.SubmitBid(CarrierId.From(command.CarrierId), lanePrices);

        // Write BidSubmitted in the same SaveChanges call — guaranteed delivery.
        var evt = rfp.DomainEvents.OfType<BidSubmitted>().Last();
        _db.OutboxMessages.Add(OutboxMessage.Create(
            typeof(BidSubmitted).AssemblyQualifiedName!,
            JsonSerializer.Serialize(evt)));

        await _db.SaveChangesAsync(ct);
        rfp.ClearDomainEvents();

        _logger.LogInformation(
            "Bid {BidId} submitted for RFP {RfpId} by carrier {CarrierId} (round {Round})",
            bid.Id, rfpId, command.CarrierId, bid.Round);

        return bid.Id;
    }
}
