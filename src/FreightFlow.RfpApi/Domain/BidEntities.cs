using FreightFlow.SharedKernel;

namespace FreightFlow.RfpApi.Domain;

public sealed class LanePrice
{
    public LaneId LaneId { get; private set; }
    public Money  Price  { get; private set; }

    private LanePrice() { }  // EF Core

    public LanePrice(LaneId laneId, Money price)
    {
        LaneId = laneId;
        Price  = price;
    }
}

public sealed class Bid
{
    private readonly List<LanePrice> _lanePrices = [];

    public BidId                     Id           { get; private set; }
    public CarrierId                 CarrierId    { get; private set; }
    public int                       Round        { get; private set; }
    public DateTimeOffset            SubmittedAt  { get; private set; }
    public IReadOnlyList<LanePrice>  LanePrices   => _lanePrices.AsReadOnly();

    private Bid() { }  // EF Core

    internal Bid(BidId id, CarrierId carrierId, int round, IEnumerable<LanePrice> lanePrices)
    {
        Id          = id;
        CarrierId   = carrierId;
        Round       = round;
        SubmittedAt = DateTimeOffset.UtcNow;
        _lanePrices.AddRange(lanePrices);
    }
}

public sealed class Award
{
    public BidId         BidId     { get; private set; }
    public CarrierId     CarrierId { get; private set; }
    public DateTimeOffset AwardedAt { get; private set; }

    private Award() { }  // EF Core

    internal Award(BidId bidId, CarrierId carrierId)
    {
        BidId     = bidId;
        CarrierId = carrierId;
        AwardedAt = DateTimeOffset.UtcNow;
    }
}
