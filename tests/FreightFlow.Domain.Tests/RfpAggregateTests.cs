using FreightFlow.RfpApi.Domain;
using FreightFlow.SharedKernel;
using Shouldly;

namespace FreightFlow.Domain.Tests;

public sealed class RfpAggregateTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Rfp CreateDraftRfp(int maxBidRounds = 3)
        => Rfp.Create(
            shipperId:    Guid.NewGuid(),
            openAt:       DateTimeOffset.UtcNow.AddDays(1),
            closeAt:      DateTimeOffset.UtcNow.AddDays(8),
            maxBidRounds: maxBidRounds);

    private static Rfp CreateOpenRfp(int maxBidRounds = 3)
    {
        var rfp = CreateDraftRfp(maxBidRounds);
        rfp.Open();
        return rfp;
    }

    private static LanePrice[] SomeLanePrices()
        => [new LanePrice(LaneId.New(), new Money(1500m, "USD"))];

    // ── AddLane ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddLane_WhenDraft_Succeeds()
    {
        var rfp = CreateDraftRfp();

        rfp.AddLane(new ZipCode("10001"), new ZipCode("90210"), FreightClass.Class70, 100);

        rfp.Lanes.Count.ShouldBe(1);
    }

    [Fact]
    public void AddLane_WhenNotDraft_ThrowsDomainException()
    {
        var rfp = CreateOpenRfp();

        var act = () => rfp.AddLane(new ZipCode("10001"), new ZipCode("90210"), FreightClass.Class70, 100);

        act.ShouldThrow<DomainException>().Message.ShouldContain("Draft");
    }

    // ── SubmitBid ─────────────────────────────────────────────────────────────

    [Fact]
    public void SubmitBid_WhenOpen_Succeeds()
    {
        var rfp = CreateOpenRfp();

        var bid = rfp.SubmitBid(CarrierId.New(), SomeLanePrices());

        rfp.Bids.Count.ShouldBe(1);
        bid.Round.ShouldBe(1);
    }

    [Fact]
    public void SubmitBid_WhenRfpNotOpen_ThrowsRfpNotOpenException()
    {
        var rfp = CreateDraftRfp();

        var act = () => rfp.SubmitBid(CarrierId.New(), SomeLanePrices());

        act.ShouldThrow<RfpNotOpenException>();
    }

    [Fact]
    public void SubmitBid_WhenMaxRoundsExceeded_ThrowsMaxBidRoundsExceededException()
    {
        var rfp = CreateOpenRfp(maxBidRounds: 1);
        rfp.SubmitBid(CarrierId.New(), SomeLanePrices());

        var act = () => rfp.SubmitBid(CarrierId.New(), SomeLanePrices());

        act.ShouldThrow<MaxBidRoundsExceededException>().Message.ShouldContain("1");
    }

    // ── IssueAward ────────────────────────────────────────────────────────────

    [Fact]
    public void IssueAward_WhenRfpNotClosed_ThrowsDomainException()
    {
        var rfp = CreateOpenRfp();
        var bid = rfp.SubmitBid(CarrierId.New(), SomeLanePrices());

        // RFP is Open — must be Closed before awarding.
        var act = () => rfp.IssueAward(bid.Id);

        act.ShouldThrow<DomainException>().Message.ShouldContain("Closed");
    }

    [Fact]
    public void IssueAward_WhenBidExists_Succeeds()
    {
        var rfp = CreateOpenRfp();
        var bid = rfp.SubmitBid(CarrierId.New(), SomeLanePrices());
        rfp.Close();

        rfp.IssueAward(bid.Id);

        rfp.Status.ShouldBe(RfpStatus.Awarded);
        rfp.Award.ShouldNotBeNull();
    }

    [Fact]
    public void IssueAward_WhenNoBids_ThrowsDomainException()
    {
        var rfp = CreateOpenRfp();
        rfp.Close();

        var act = () => rfp.IssueAward(BidId.New());

        act.ShouldThrow<DomainException>().Message.ShouldContain("no bids");
    }

    [Fact]
    public void IssueAward_WhenAlreadyAwarded_ThrowsDomainException()
    {
        var rfp = CreateOpenRfp();
        var bid = rfp.SubmitBid(CarrierId.New(), SomeLanePrices());
        rfp.Close();
        rfp.IssueAward(bid.Id);

        // RFP is now Awarded — second attempt fails the Closed guard.
        var act = () => rfp.IssueAward(bid.Id);

        act.ShouldThrow<DomainException>().Message.ShouldContain("Closed");
    }

    // ── Domain events ─────────────────────────────────────────────────────────

    [Fact]
    public void Create_RaisesRfpCreatedEvent()
    {
        var rfp = CreateDraftRfp();

        rfp.DomainEvents.ShouldContain(e => e is FreightFlow.RfpApi.Domain.Events.RfpCreated);
    }

    [Fact]
    public void Open_RaisesRfpOpenedEvent()
    {
        var rfp = CreateDraftRfp();

        rfp.Open();

        rfp.DomainEvents.ShouldContain(e => e is FreightFlow.RfpApi.Domain.Events.RfpOpened);
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Close_WhenOpen_SetsStatusToClosed()
    {
        var rfp = CreateOpenRfp();

        rfp.Close();

        rfp.Status.ShouldBe(RfpStatus.Closed);
    }

    [Fact]
    public void Close_WhenNotOpen_ThrowsDomainException()
    {
        var rfp = CreateDraftRfp();

        var act = () => rfp.Close();

        act.ShouldThrow<DomainException>().Message.ShouldContain("Open");
    }

    [Fact]
    public void Close_WhenOpen_RaisesRfpClosedEvent()
    {
        var rfp = CreateOpenRfp();

        rfp.Close();

        rfp.DomainEvents.ShouldContain(e => e is FreightFlow.RfpApi.Domain.Events.RfpClosed);
    }
}
