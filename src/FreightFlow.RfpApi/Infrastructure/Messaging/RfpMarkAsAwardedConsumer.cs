using FreightFlow.RfpApi.Infrastructure.Persistence;
using FreightFlow.SharedKernel;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace FreightFlow.RfpApi.Infrastructure.Messaging;

/// <summary>
/// Handles the saga's final step: WorkflowWorker confirms the award workflow completed.
/// Loads the RFP and calls Rfp.AttachContract so the ContractId is persisted against the award.
/// </summary>
public sealed class RfpMarkAsAwardedConsumer : IConsumer<RfpMarkAsAwarded>
{
    private readonly RfpDbContext                      _db;
    private readonly ILogger<RfpMarkAsAwardedConsumer> _logger;

    public RfpMarkAsAwardedConsumer(RfpDbContext db, ILogger<RfpMarkAsAwardedConsumer> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<RfpMarkAsAwarded> context)
    {
        var msg = context.Message;

        var rfp = await _db.Rfps
            .FirstOrDefaultAsync(r => r.Id == RfpId.From(msg.RfpId));

        if (rfp is null)
        {
            _logger.LogWarning(
                "RfpMarkAsAwarded: RFP {RfpId} not found — saga may have used a stale id.",
                msg.RfpId);
            return;
        }

        rfp.AttachContract(ContractId.From(msg.ContractId));
        await _db.SaveChangesAsync();

        // Acknowledge back to the saga so it can advance from RfpAwarding → Completed.
        await context.Publish(new RfpAwardAcknowledged(
            RfpId:      msg.RfpId,
            ContractId: msg.ContractId,
            OccurredAt: DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "Award workflow completed for RFP {RfpId}. ContractId {ContractId} attached.",
            msg.RfpId, msg.ContractId);
    }
}

