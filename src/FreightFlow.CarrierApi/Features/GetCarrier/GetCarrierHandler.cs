using FreightFlow.CarrierApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.CarrierApi.Features.GetCarrier;

public sealed class GetCarrierHandler
{
    private readonly CarrierDbContext _db;

    public GetCarrierHandler(CarrierDbContext db)
    {
        _db = db;
    }

    public async Task<CarrierDto> HandleAsync(CarrierId id, CancellationToken ct = default)
    {
        // AsNoTracking — read-only path; load then project in memory because CarrierProfile
        // uses a value converter that doesn't translate inside a LINQ Select to SQL.
        var carrier = await _db.Carriers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (carrier is null)
            throw new CarrierNotFoundException(id.Value);

        return new CarrierDto(
            carrier.Id.Value,
            carrier.DotNumber.Value,
            carrier.Name,
            carrier.AuthorityStatus.ToString(),
            carrier.InsuranceExpiry.ToString("yyyy-MM-dd"),
            new CarrierProfileDto(
                carrier.Profile.EquipmentTypes,
                carrier.Profile.Certifications,
                carrier.Profile.Notes),
            carrier.CreatedAt);
    }
}
