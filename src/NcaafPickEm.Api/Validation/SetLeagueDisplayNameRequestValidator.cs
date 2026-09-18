using FluentValidation;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Validation;

/// <summary>
/// Shape validation for <see cref="SetLeagueDisplayNameRequest"/>. Null or empty is valid (it
/// clears the override); a non-empty name is checked only for length, since the trimming rule
/// lives with <see cref="Domain.Leagues.LeagueRules.ValidateDisplayNameOverride"/>.
/// </summary>
public sealed class SetLeagueDisplayNameRequestValidator : AbstractValidator<SetLeagueDisplayNameRequest>
{
    /// <summary>Creates the validator.</summary>
    public SetLeagueDisplayNameRequestValidator()
    {
        RuleFor(request => request.DisplayName)
            .Must(name => string.IsNullOrWhiteSpace(name) || name.Trim().Length <= Membership.DisplayNameMaxLength)
            .WithMessage($"Display name must be at most {Membership.DisplayNameMaxLength} characters.");
    }
}
