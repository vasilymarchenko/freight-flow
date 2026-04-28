namespace FreightFlow.SharedKernel;

/// <summary>
/// Integration event published by freight-rfp-api via the Transactional Outbox when a bid is awarded.
/// Consumed by freight-workflow-worker to start the AwardWorkflow saga.
/// Carries all data the saga needs so it never has to query rfp-api mid-flight.
/// </summary>
public sealed record AwardIssued(
    RfpId          RfpId,
    BidId          BidId,
    CarrierId      CarrierId,
    LaneId         LaneId,
    Money          AgreedRate,
    int            VolumeToReserve,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published by freight-workflow-worker after the shipper has been notified of the contract.
/// </summary>
public sealed record ShipperNotified(
    RfpId          RfpId,
    ContractId     ContractId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Command published by freight-workflow-worker to freight-rfp-api to confirm the award workflow
/// completed successfully. rfp-api attaches the ContractId and replies with RfpAwardAcknowledged.
/// </summary>
public sealed record RfpMarkAsAwarded(
    Guid           RfpId,
    Guid           ContractId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Published by freight-rfp-api after successfully attaching the ContractId to the RFP award.
/// Consumed by freight-workflow-worker to advance the saga from RfpAwarding → Completed.
/// </summary>
public sealed record RfpAwardAcknowledged(
    Guid           RfpId,
    Guid           ContractId,
    DateTimeOffset OccurredAt);
