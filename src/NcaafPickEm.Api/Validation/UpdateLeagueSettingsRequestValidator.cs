using FluentValidation;
using NcaafPickEm.Domain.Leagues;
using NcaafPickEm.Shared.Contracts.Leagues;

namespace NcaafPickEm.Api.Validation;

/// <summary>Shape validation for <see cref="UpdateLeagueSettingsRequest"/>.</summary>
public sealed class UpdateLeagueSettingsRequestValidator : AbstractValidator<UpdateLeagueSettingsRequest>
{
    /// <summary>Creates the validator.</summary>
    public UpdateLeagueSettingsRequestValidator()
    {
        // Cascade(Stop): a JSON body that omits Name binds null, and without Stop the Must below
        // would dereference it and 500 instead of 400.
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("League name is required.")
            .Must(name => name.Trim().Length is > 0 and <= League.NameMaxLength)
            .WithMessage($"League name must be 1 to {League.NameMaxLength} characters.");

        RuleFor(request => request.FirstWeek)
            .GreaterThanOrEqualTo(0);

        RuleFor(request => request.LastWeek)
            .GreaterThanOrEqualTo(request => request.FirstWeek)
            .WithMessage("First week must not be after last week.");

        RuleFor(request => request.DefaultPointValue)
            .InclusiveBetween(League.MinPointValue, League.MaxPointValue);
    }
}
