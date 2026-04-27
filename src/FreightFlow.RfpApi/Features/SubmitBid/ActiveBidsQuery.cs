using Dapper;
using Npgsql;

namespace FreightFlow.RfpApi.Features.SubmitBid;

public sealed record BidSummary(
    Guid    BidId,
    Guid    CarrierId,
    int     Round,
    Guid    LaneId,
    decimal Amount,
    string  Currency);

/// <summary>
/// Dapper hot-path read — returns all bids for an RFP with prices per lane,
/// ordered so the lowest bid per lane appears first.
/// </summary>
public sealed class ActiveBidsQuery
{
    private readonly NpgsqlDataSource _db;

    public ActiveBidsQuery(NpgsqlDataSource db)
    {
        _db = db;
    }

    private const string Sql = """
        SELECT b.id          AS BidId,
               b.carrier_id  AS CarrierId,
               b.round       AS Round,
               blp.lane_id   AS LaneId,
               blp.amount    AS Amount,
               blp.currency  AS Currency
        FROM   bids b
        JOIN   bid_lane_prices blp ON blp.bid_id = b.id
        WHERE  b.rfp_id = @rfpId
        ORDER  BY blp.lane_id, blp.amount
        """;

    public async Task<IReadOnlyList<BidSummary>> ExecuteAsync(
        Guid rfpId,
        CancellationToken ct = default)
    {
        await using var conn = await _db.OpenConnectionAsync(ct);
        return [.. await conn.QueryAsync<BidSummary>(Sql, new { rfpId })];
    }
}
