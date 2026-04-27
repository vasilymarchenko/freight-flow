namespace FreightFlow.RfpApi.Features.SubmitBid;

public sealed record LanePriceInput(Guid LaneId, decimal Amount, string Currency);

public sealed record SubmitBidCommand(
    Guid                         CarrierId,
    IReadOnlyList<LanePriceInput> LanePrices);
