using FreightFlow.SharedKernel;
using Shouldly;

namespace FreightFlow.Domain.Tests;

public sealed class ValueObjectTests
{
    // ── Money ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Money_ValidAmount_Succeeds()
    {
        var money = new Money(100m, "USD");

        money.Amount.ShouldBe(100m);
        money.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Money_ZeroAmount_Succeeds()
    {
        var money = new Money(0m, "USD");

        money.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Money_NegativeAmount_ThrowsDomainException()
    {
        Action act = () => { _ = new Money(-1m, "USD"); };

        act.ShouldThrow<DomainException>().Message.ShouldContain("non-negative");
    }

    [Fact]
    public void Money_InvalidCurrency_ThrowsDomainException()
    {
        Action act = () => { _ = new Money(100m, "US"); };

        act.ShouldThrow<DomainException>().Message.ShouldContain("ISO 4217");
    }

    // ── ZipCode ───────────────────────────────────────────────────────────────

    [Fact]
    public void ZipCode_Valid_Succeeds()
    {
        var zip = new ZipCode("90210");

        zip.Value.ShouldBe("90210");
    }

    [Fact]
    public void ZipCode_TooShort_ThrowsDomainException()
    {
        Action act = () => { _ = new ZipCode("9021"); };

        act.ShouldThrow<DomainException>();
    }

    [Fact]
    public void ZipCode_NonNumeric_ThrowsDomainException()
    {
        Action act = () => { _ = new ZipCode("9A210"); };

        act.ShouldThrow<DomainException>();
    }

    // ── DotNumber ─────────────────────────────────────────────────────────────

    [Fact]
    public void DotNumber_Valid_Succeeds()
    {
        var dot = new DotNumber("1234567");

        dot.Value.ShouldBe("1234567");
    }

    [Fact]
    public void DotNumber_Empty_ThrowsDomainException()
    {
        Action act = () => { _ = new DotNumber(""); };

        act.ShouldThrow<DomainException>();
    }

    [Fact]
    public void DotNumber_NonNumeric_ThrowsDomainException()
    {
        Action act = () => { _ = new DotNumber("ABC123"); };

        act.ShouldThrow<DomainException>();
    }

    // ── Strongly-typed IDs ────────────────────────────────────────────────────

    [Fact]
    public void RfpId_New_ProducesUniqueValues()
    {
        var id1 = RfpId.New();
        var id2 = RfpId.New();

        id1.ShouldNotBe(id2);
    }

    [Fact]
    public void RfpId_FromGuid_RoundTrips()
    {
        var guid = Guid.NewGuid();
        var id   = RfpId.From(guid);

        id.Value.ShouldBe(guid);
    }

    [Fact]
    public void CarrierId_IsNotInterchangeableWithRfpId()
    {
        // Compile-time check by design — this test documents the intent.
        var carrierId = CarrierId.New();
        var rfpId     = RfpId.New();

        carrierId.ShouldNotBe((object)rfpId); // different types; no implicit conversion
    }
}
