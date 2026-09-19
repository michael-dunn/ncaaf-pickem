using FluentValidation;
using NcaafPickEm.Shared.Contracts.Scoring;

namespace NcaafPickEm.Api.Validation;

/// <summary>Shape validation for <see cref="OverrideResultRequest"/>.</summary>
public sealed class OverrideResultRequestValidator : AbstractValidator<OverrideResultRequest>
{
    /// <summary>Creates the validator.</summary>
    public OverrideResultRequestValidator()
    {
        RuleFor(request => request.WinnerTeamId)
            .NotEqual(Guid.Empty).WithMessage("WinnerTeamId is required.");

        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(200).WithMessage("Reason must be 200 characters or fewer.");
    }
}
