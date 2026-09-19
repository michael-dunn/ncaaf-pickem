using FluentValidation;
using NcaafPickEm.Shared.Contracts.Admin;

namespace NcaafPickEm.Api.Validation;

/// <summary>
/// Shape validation for <see cref="ResolveUnmatchedRequest"/>: the commissioner must name a game
/// to resolve the unmatched row against. Whether that game exists is the endpoint's own 400.
/// </summary>
public sealed class ResolveUnmatchedRequestValidator : AbstractValidator<ResolveUnmatchedRequest>
{
    /// <summary>Creates the validator.</summary>
    public ResolveUnmatchedRequestValidator()
    {
        RuleFor(request => request.GameId)
            .NotEmpty()
            .WithMessage("A game id is required.");
    }
}
