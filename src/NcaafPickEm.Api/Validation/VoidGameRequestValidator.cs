using FluentValidation;
using NcaafPickEm.Shared.Contracts.Scoring;

namespace NcaafPickEm.Api.Validation;

/// <summary>Shape validation for <see cref="VoidGameRequest"/>.</summary>
public sealed class VoidGameRequestValidator : AbstractValidator<VoidGameRequest>
{
    /// <summary>Creates the validator.</summary>
    public VoidGameRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(200).WithMessage("Reason must be 200 characters or fewer.");
    }
}
