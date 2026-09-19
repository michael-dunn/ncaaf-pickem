using FluentValidation;
using NcaafPickEm.Shared.Contracts.GameSets;

namespace NcaafPickEm.Api.Validation;

/// <summary>Shape validation for <see cref="AddGameRequest"/>.</summary>
public sealed class AddGameRequestValidator : AbstractValidator<AddGameRequest>
{
    /// <summary>Creates the validator.</summary>
    public AddGameRequestValidator()
    {
        RuleFor(request => request.GameId)
            .NotEqual(Guid.Empty).WithMessage("GameId is required.");
    }
}
