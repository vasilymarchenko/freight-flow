using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.RfpApi.Features.AddLane;

public sealed class AddLaneHandler
{
    private readonly RfpDbContext              _db;
    private readonly ILogger<AddLaneHandler>  _logger;

    public AddLaneHandler(RfpDbContext db, ILogger<AddLaneHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<LaneId> HandleAsync(
        Guid            rfpId,
        AddLaneCommand  command,
        CancellationToken ct = default)
    {
        var rfp = await _db.Rfps
            .FirstOrDefaultAsync(r => r.Id == RfpId.From(rfpId), ct)
            ?? throw new RfpNotFoundException(rfpId);

        rfp.AddLane(
            new ZipCode(command.OriginZip),
            new ZipCode(command.DestZip),
            command.FreightClass,
            command.Volume);

        await _db.SaveChangesAsync(ct);

        var laneId = rfp.Lanes[^1].Id;

        _logger.LogInformation(
            "Lane {LaneId} added to RFP {RfpId}",
            laneId, rfpId);

        return laneId;
    }
}
