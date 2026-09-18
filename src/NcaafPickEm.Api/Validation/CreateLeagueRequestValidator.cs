using FluentValidation;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Validation;

/// <summary>
/// Shape validation for <see cref="CreateLeagueRequest"/>. Whether <c>FirstWeek</c>/<c>LastWeek</c>
/// are actually regular-season weeks depends on the season calendar and is checked by
/// <c>LeagueService</c> (<see cref="Domain.Leagues.LeagueRules.ValidateWeekRange"/>), not here.
/// </summary>
public sealed class CreateLeagueRequestValidator : AbstractValidator<CreateLeagueRequest>
{
    /// <summary>Creates the validator.</summary>
    public CreateLeagueRequestValidator()
    {
        // Cascade(Stop): the record's Name is non-nullable but a JSON body that omits it binds
        // null, and without Stop the Must below would dereference it and 500 instead of 400.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("League name is required.")
            .Must(name => name.Trim().Length is > 0 and <= League.NameMaxLength)
            .WithMessage($"League name must be 1 to {League.NameMaxLength} characters.");

        RuleFor(request => request.SeasonYear)
            .InclusiveBetween(2000, 2100).WithMessage("Season year must be a real calendar year.");

        RuleFor(request => request.LastWeek)
            .GreaterThanOrEqualTo(request => request.FirstWeek!.Value)
            .When(request => request.FirstWeek is not null && request.LastWeek is not null)
            .WithMessage("First week must not be after last week.");
    }
}
