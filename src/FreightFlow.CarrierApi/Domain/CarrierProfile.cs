namespace FreightFlow.CarrierApi.Domain;

public sealed record CarrierProfile(
    string[]  EquipmentTypes,
    string[]  Certifications,
    string?   Notes);
