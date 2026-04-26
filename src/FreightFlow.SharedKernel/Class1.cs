// SharedKernel is populated in Milestone 1.
// Only things that genuinely cross service boundaries go here:
//   - Value objects (Money, ZipCode, FreightClass, DotNumber)
//   - Strongly-typed IDs (RfpId, CarrierId, BidId, LaneId, ContractId)
//   - Message contracts (domain events published via RabbitMQ)
//
// Domain logic stays inside each service. No infrastructure dependencies.

namespace FreightFlow.SharedKernel;

