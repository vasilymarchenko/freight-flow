using System.Text.RegularExpressions;

namespace FreightFlow.SharedKernel;

public readonly record struct Money
{
    public decimal Amount   { get; }
    public string  Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (amount < 0)
            throw new DomainException($"Money amount must be non-negative, got {amount}.");
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new DomainException($"Currency must be a 3-character ISO 4217 code, got '{currency}'.");

        Amount   = amount;
        Currency = currency.ToUpperInvariant();
    }

    public override string ToString() => $"{Amount:F2} {Currency}";
}

public readonly record struct ZipCode
{
    private static readonly Regex _pattern = new(@"^\d{5}$", RegexOptions.Compiled);

    public string Value { get; }

    public ZipCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !_pattern.IsMatch(value))
            throw new DomainException($"ZipCode must be a 5-digit US ZIP, got '{value}'.");

        Value = value;
    }

    public override string ToString() => Value;
}

public readonly record struct DotNumber
{
    private static readonly Regex _pattern = new(@"^\d+$", RegexOptions.Compiled);

    public string Value { get; }

    public DotNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !_pattern.IsMatch(value))
            throw new DomainException($"DotNumber must be a non-empty numeric string, got '{value}'.");

        Value = value;
    }

    public override string ToString() => Value;
}

public enum FreightClass
{
    Class50,
    Class55,
    Class60,
    Class65,
    Class70,
    Class77_5,
    Class85,
    Class92_5,
    Class100,
    Class110,
    Class125,
    Class150,
    Class175,
    Class200,
    Class250,
    Class300,
    Class400,
    Class500
}
