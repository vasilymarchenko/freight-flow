using FluentValidation;

namespace FreightFlow.RfpApi.Features.AddLane;

public sealed class AddLaneValidator : AbstractValidator<AddLaneCommand>
{
    public AddLaneValidator()
    {
        RuleFor(x => x.OriginZip)
            .NotEmpty()
            .Matches(@"^\d{5}$").WithMessage("OriginZip must be a 5-digit US ZIP code.");

        RuleFor(x => x.DestZip)
            .NotEmpty()
            .Matches(@"^\d{5}$").WithMessage("DestZip must be a 5-digit US ZIP code.");

        RuleFor(x => x.Volume)
            .GreaterThan(0).WithMessage("Volume must be greater than zero.");
    }
}
