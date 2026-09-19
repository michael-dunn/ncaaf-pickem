using FluentValidation;
using NcaafPickEm.Shared.Contracts.Picks;

namespace NcaafPickEm.Api.Validation;

/// <summary>
/// Shape validation for <see cref="SetPickRequest"/>. Whether the team is actually playing in that
/// game needs the database, so <c>PickService</c> answers it with a 400 <c>TeamNotInGame</c>.
/// </summary>
public sealed class SetPickRequestValidator : AbstractValidator<SetPickRequest>
{
    /// <summary>Creates the validator.</summary>
    public SetPickRequestValidator()
    {
        RuleFor(request => request.TeamId)
            .NotEqual(Guid.Empty).WithMessage("TeamId is required.");
    }
}
