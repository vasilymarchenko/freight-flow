namespace FreightFlow.SharedKernel;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception inner) : base(message, inner) { }
}

public sealed class RfpNotOpenException : DomainException
{
    public RfpNotOpenException() : base("RFP is not open for bidding.") { }
}

public sealed class MaxBidRoundsExceededException : DomainException
{
    public MaxBidRoundsExceededException(int max) : base($"Cannot exceed maximum bid rounds of {max}.") { }
}

public sealed class CarrierNotActiveException : DomainException
{
    public CarrierNotActiveException() : base("Carrier authority status is not active.") { }
}

public sealed class InsufficientCapacityException : DomainException
{
    public InsufficientCapacityException() : base("Insufficient available capacity for this reservation.") { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public sealed class RfpNotFoundException : NotFoundException
{
    public RfpNotFoundException(Guid id) : base($"RFP '{id}' was not found.") { }
}

public sealed class CarrierNotFoundException : NotFoundException
{
    public CarrierNotFoundException(Guid id) : base($"Carrier '{id}' was not found.") { }
}
