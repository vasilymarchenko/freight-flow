using FreightFlow.CarrierApi.Domain;
using FreightFlow.SharedKernel;
using Shouldly;

namespace FreightFlow.Domain.Tests;

public sealed class CarrierAggregateTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Carrier CreateActiveCarrier()
        => Carrier.Onboard(
            new DotNumber("1234567"),
            "Acme Trucking",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            new CarrierProfile(["Dry Van"], ["HAZMAT"], null));

    // ── ReserveCapacity ───────────────────────────────────────────────────────

    [Fact]
    public void ReserveCapacity_WhenActiveAndSufficient_Succeeds()
    {
        var carrier = CreateActiveCarrier();
        var laneId  = LaneId.New();
        carrier.AddCapacity(laneId, 100);

        carrier.ReserveCapacity(laneId, 50);

        carrier.CapacityRecords[0].ReservedVolume.ShouldBe(50);
    }

    [Fact]
    public void ReserveCapacity_WhenCarrierNotActive_ThrowsCarrierNotActiveException()
    {
        var carrier = CreateActiveCarrier();
        var laneId  = LaneId.New();
        carrier.AddCapacity(laneId, 100);
        carrier.Deactivate();

        var act = () => carrier.ReserveCapacity(laneId, 10);

        act.ShouldThrow<CarrierNotActiveException>();
    }

    [Fact]
    public void ReserveCapacity_WhenInsufficientCapacity_ThrowsInsufficientCapacityException()
    {
        var carrier = CreateActiveCarrier();
        var laneId  = LaneId.New();
        carrier.AddCapacity(laneId, 10);

        var act = () => carrier.ReserveCapacity(laneId, 100);

        act.ShouldThrow<InsufficientCapacityException>();
    }

    [Fact]
    public void ReserveCapacity_BelowZeroNet_ThrowsInsufficientCapacityException()
    {
        var carrier = CreateActiveCarrier();
        var laneId  = LaneId.New();
        carrier.AddCapacity(laneId, 0);

        var act = () => carrier.ReserveCapacity(laneId, 1);

        act.ShouldThrow<InsufficientCapacityException>();
    }

    // ── ReleaseCapacity ───────────────────────────────────────────────────────

    [Fact]
    public void ReleaseCapacity_WhenReservationExists_Succeeds()
    {
        var carrier = CreateActiveCarrier();
        var laneId  = LaneId.New();
        carrier.AddCapacity(laneId, 100);
        carrier.ReserveCapacity(laneId, 50);

        carrier.ReleaseCapacity(laneId, 50);

        carrier.CapacityRecords[0].ReservedVolume.ShouldBe(0);
    }

    [Fact]
    public void ReleaseCapacity_WhenMoreThanReserved_ThrowsDomainException()
    {
        var carrier = CreateActiveCarrier();
        var laneId  = LaneId.New();
        carrier.AddCapacity(laneId, 100);
        carrier.ReserveCapacity(laneId, 10);

        var act = () => carrier.ReleaseCapacity(laneId, 50);

        act.ShouldThrow<DomainException>();
    }

    [Fact]
    public void ReleaseCapacity_WhenNoRecord_ThrowsDomainException()
    {
        var carrier = CreateActiveCarrier();

        var act = () => carrier.ReleaseCapacity(LaneId.New(), 10);

        act.ShouldThrow<DomainException>();
    }

    // ── Onboard ───────────────────────────────────────────────────────────────

    [Fact]
    public void Onboard_RaisesCarrierOnboardedEvent()
    {
        var carrier = CreateActiveCarrier();

        carrier.DomainEvents.ShouldContain(e => e is FreightFlow.CarrierApi.Domain.Events.CarrierOnboarded);
    }

    // ── Deactivate / Revoke ───────────────────────────────────────────────────

    [Fact]
    public void Deactivate_WhenActive_SetsInactiveStatus()
    {
        var carrier = CreateActiveCarrier();

        carrier.Deactivate();

        carrier.AuthorityStatus.ShouldBe(AuthorityStatus.Inactive);
    }

    [Fact]
    public void Deactivate_WhenRevoked_ThrowsDomainException()
    {
        var carrier = CreateActiveCarrier();
        carrier.Revoke();

        var act = () => carrier.Deactivate();

        act.ShouldThrow<DomainException>().Message.ShouldContain("revoked");
    }

    [Fact]
    public void Revoke_WhenActive_SetsRevokedStatus()
    {
        var carrier = CreateActiveCarrier();

        carrier.Revoke();

        carrier.AuthorityStatus.ShouldBe(AuthorityStatus.Revoked);
    }
}
