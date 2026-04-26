namespace FreightFlow.CarrierApi.Features.GetCarrier;

public sealed record CarrierDto(
    Guid            Id,
    string          DotNumber,
    string          Name,
    string          AuthorityStatus,
    string          InsuranceExpiry,
    CarrierProfileDto Profile,
    DateTimeOffset  CreatedAt);

public sealed record CarrierProfileDto(
    string[] EquipmentTypes,
    string[] Certifications,
    string?  Notes);
