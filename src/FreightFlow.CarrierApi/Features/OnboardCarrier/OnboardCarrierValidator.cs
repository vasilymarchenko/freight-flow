using FluentValidation;

namespace FreightFlow.CarrierApi.Features.OnboardCarrier;

public sealed class OnboardCarrierValidator : AbstractValidator<OnboardCarrierCommand>
{
    public OnboardCarrierValidator()
    {
        RuleFor(x => x.DotNumber)
            .NotEmpty()
            .Matches(@"^\d+$").WithMessage("DOT number must be a non-empty numeric string.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.InsuranceExpiry)
            .Must(d => d > DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Insurance expiry must be in the future.");

        RuleFor(x => x.EquipmentTypes)
            .NotNull()
            .Must(t => t.Length > 0).WithMessage("At least one equipment type is required.");
    }
}
