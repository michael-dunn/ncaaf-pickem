using FluentValidation;
using NcaafPickEm.Domain.Points;
using NcaafPickEm.Shared.Contracts.Points;

namespace NcaafPickEm.Api.Validation;

/// <summary>Shape validation for <see cref="SetPointOverrideRequest"/>. Null clears the override.</summary>
public sealed class SetPointOverrideRequestValidator : AbstractValidator<SetPointOverrideRequest>
{
    /// <summary>Creates the validator.</summary>
    public SetPointOverrideRequestValidator()
    {
        RuleFor(request => request.PointValue!.Value)
            .InclusiveBetween(PointValueLimits.Min, PointValueLimits.Max)
            .When(request => request.PointValue is not null)
            .WithMessage($"Point value must be between {PointValueLimits.Min} and {PointValueLimits.Max}.");
    }
}
