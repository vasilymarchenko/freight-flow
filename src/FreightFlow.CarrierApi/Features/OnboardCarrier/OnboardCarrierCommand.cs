namespace FreightFlow.CarrierApi.Features.OnboardCarrier;

public sealed record OnboardCarrierCommand(
    string   DotNumber,
    string   Name,
    DateOnly InsuranceExpiry,
    string[] EquipmentTypes,
    string[] Certifications,
    string?  Notes);
