using FreightFlow.RfpApi.Domain;
using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.RfpApi.Features.GetRfp;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record LanePriceDto(Guid LaneId, decimal Amount, string Currency);

public sealed record BidDto(
    Guid                        Id,
    Guid                        CarrierId,
    int                         Round,
    DateTimeOffset              SubmittedAt,
    IReadOnlyList<LanePriceDto> LanePrices);

public sealed record LaneDto(
    Guid         Id,
    string       OriginZip,
    string       DestZip,
    FreightClass FreightClass,
    int          Volume);

public sealed record AwardDto(Guid BidId, Guid CarrierId, DateTimeOffset AwardedAt);

public sealed record RfpDto(
    Guid                    Id,
    Guid                    ShipperId,
    RfpStatus               Status,
    DateTimeOffset          OpenAt,
    DateTimeOffset          CloseAt,
    int                     MaxBidRounds,
    DateTimeOffset          CreatedAt,
    DateTimeOffset          UpdatedAt,
    IReadOnlyList<LaneDto>  Lanes,
    IReadOnlyList<BidDto>   Bids,
    AwardDto?               Award);

// ── Handler ──────────────────────────────────────────────────────────────────

public sealed class GetRfpHandler
{
    private readonly RfpDbContext _db;

    public GetRfpHandler(RfpDbContext db)
    {
        _db = db;
    }

    // Compiled query — LINQ expression tree parsed once at startup, not per call.
    // Includes nested owned collections (Lanes, Bids with LanePrices, Award).
    private static readonly Func<RfpDbContext, RfpId, Task<RfpDto?>> GetQuery =
        EF.CompileAsyncQuery((RfpDbContext db, RfpId rfpId) =>
            db.Rfps
              .AsNoTracking()
              .Where(r => r.Id == rfpId)
              .Select(r => new RfpDto(
                  r.Id.Value,
                  r.ShipperId,
                  r.Status,
                  r.OpenAt,
                  r.CloseAt,
                  r.MaxBidRounds,
                  r.CreatedAt,
                  r.UpdatedAt,
                  r.Lanes.Select(l => new LaneDto(
                      l.Id.Value,
                      l.OriginZip.Value,
                      l.DestinationZip.Value,
                      l.FreightClass,
                      l.Volume)).ToList(),
                  r.Bids.Select(b => new BidDto(
                      b.Id.Value,
                      b.CarrierId.Value,
                      b.Round,
                      b.SubmittedAt,
                      b.LanePrices.Select(lp => new LanePriceDto(
                          lp.LaneId.Value,
                          lp.Price.Amount,
                          lp.Price.Currency)).ToList())).ToList(),
                  r.Award == null ? null : new AwardDto(
                      r.Award.BidId.Value,
                      r.Award.CarrierId.Value,
                      r.Award.AwardedAt)))
              .FirstOrDefault());

    public async Task<RfpDto> HandleAsync(Guid rfpId, CancellationToken ct = default)
    {
        var dto = await GetQuery(_db, RfpId.From(rfpId));
        if (dto is null) throw new RfpNotFoundException(rfpId);
        return dto;
    }
}
