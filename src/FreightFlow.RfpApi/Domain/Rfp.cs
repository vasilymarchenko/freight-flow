using FreightFlow.RfpApi.Domain.Events;
using FreightFlow.SharedKernel;

namespace FreightFlow.RfpApi.Domain;

public enum RfpStatus { Draft, Open, Closed, Awarded }

public sealed class Rfp : AggregateRoot
{
    private readonly List<Lane>  _lanes = [];
    private readonly List<Bid>   _bids  = [];

    public RfpId            Id            { get; private set; }
    public Guid             ShipperId     { get; private set; }
    public RfpStatus        Status        { get; private set; }
    public DateTimeOffset   OpenAt        { get; private set; }
    public DateTimeOffset   CloseAt       { get; private set; }
    public int              MaxBidRounds  { get; private set; }
    public IReadOnlyList<Lane> Lanes      => _lanes.AsReadOnly();
    public IReadOnlyList<Bid>  Bids       => _bids.AsReadOnly();
    public Award?           Award         { get; private set; }

    private Rfp() { }  // EF Core

    public static Rfp Create(
        Guid shipperId,
        DateTimeOffset openAt,
        DateTimeOffset closeAt,
        int maxBidRounds)
    {
        var rfp = new Rfp
        {
            Id           = RfpId.New(),
            ShipperId    = shipperId,
            Status       = RfpStatus.Draft,
            OpenAt       = openAt,
            CloseAt      = closeAt,
            MaxBidRounds = maxBidRounds
        };
        rfp.Raise(new RfpCreated(rfp.Id, shipperId, DateTimeOffset.UtcNow));
        return rfp;
    }

    public void AddLane(ZipCode originZip, ZipCode destinationZip, FreightClass freightClass, int volume)
    {
        if (Status != RfpStatus.Draft)
            throw new DomainException("Cannot add a lane to an RFP that is not in Draft status.");

        _lanes.Add(new Lane(LaneId.New(), originZip, destinationZip, freightClass, volume));
    }

    public void Open()
    {
        if (Status != RfpStatus.Draft)
            throw new DomainException("Only a Draft RFP can be opened.");

        Status = RfpStatus.Open;
        Raise(new RfpOpened(Id, DateTimeOffset.UtcNow));
    }

    public void Close()
    {
        if (Status != RfpStatus.Open)
            throw new DomainException("Only an Open RFP can be closed.");

        Status = RfpStatus.Closed;
        Raise(new RfpClosed(Id, DateTimeOffset.UtcNow));
    }

    public Bid SubmitBid(CarrierId carrierId, IEnumerable<LanePrice> lanePrices)
    {
        if (Status != RfpStatus.Open)
            throw new RfpNotOpenException();

        var currentRound = _bids.Count == 0 ? 0 : _bids.Max(b => b.Round);
        var nextRound    = currentRound + 1;

        if (nextRound > MaxBidRounds)
            throw new MaxBidRoundsExceededException(MaxBidRounds);

        var bid = new Bid(BidId.New(), carrierId, nextRound, lanePrices);
        _bids.Add(bid);
        Raise(new BidSubmitted(Id, bid.Id, carrierId, DateTimeOffset.UtcNow));
        return bid;
    }

    public Award IssueAward(BidId bidId)
    {
        if (_bids.Count == 0)
            throw new DomainException("Cannot award an RFP that has no bids.");

        if (Status == RfpStatus.Awarded)
            throw new DomainException("Cannot award an RFP that is already awarded.");

        var bid = _bids.FirstOrDefault(b => b.Id == bidId)
            ?? throw new DomainException($"Bid '{bidId}' not found on this RFP.");

        Award  = new Award(bidId, bid.CarrierId);
        Status = RfpStatus.Awarded;
        Raise(new AwardIssued(Id, bidId, bid.CarrierId, DateTimeOffset.UtcNow));
        return Award;
    }
}
