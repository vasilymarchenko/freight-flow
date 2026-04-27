using FluentValidation;

namespace FreightFlow.RfpApi.Features.SubmitBid;

public sealed class SubmitBidValidator : AbstractValidator<SubmitBidCommand>
{
    public SubmitBidValidator()
    {
        RuleFor(x => x.CarrierId)
            .NotEmpty().WithMessage("CarrierId is required.");

        RuleFor(x => x.LanePrices)
            .NotEmpty().WithMessage("At least one lane price is required.");

        RuleForEach(x => x.LanePrices).ChildRules(lp =>
        {
            lp.RuleFor(x => x.LaneId).NotEmpty().WithMessage("LaneId is required.");
            lp.RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount must be positive.");
            lp.RuleFor(x => x.Currency)
                .NotEmpty()
                .Length(3).WithMessage("Currency must be a 3-character ISO 4217 code.");
        });
    }
}
