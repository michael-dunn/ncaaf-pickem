using FluentValidation;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Validation;

/// <summary>Shape validation for <see cref="TransferRequest"/>.</summary>
public sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    /// <summary>Creates the validator.</summary>
    public TransferRequestValidator()
    {
        RuleFor(request => request.ToMembershipId)
            .NotEqual(Guid.Empty).WithMessage("ToMembershipId is required.");
    }
}
