using FluentValidation;

namespace FreightFlow.RfpApi.Features.CreateRfp;

public sealed class CreateRfpValidator : AbstractValidator<CreateRfpCommand>
{
    public CreateRfpValidator()
    {
        RuleFor(x => x.ShipperId)
            .NotEmpty().WithMessage("ShipperId is required.");

        RuleFor(x => x.OpenAt)
            .LessThan(x => x.CloseAt).WithMessage("OpenAt must be before CloseAt.");

        RuleFor(x => x.CloseAt)
            .GreaterThan(DateTimeOffset.UtcNow).WithMessage("CloseAt must be in the future.");

        RuleFor(x => x.MaxBidRounds)
            .InclusiveBetween(1, 10).WithMessage("MaxBidRounds must be between 1 and 10.");
    }
}
