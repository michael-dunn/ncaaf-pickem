using FluentValidation;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Shared.Contracts.Push;

namespace NcaafPickEm.Api.Validation;

/// <summary>
/// Shape validation for <see cref="PushSubscriptionRequest"/>. A push endpoint is always an
/// absolute https URL issued by the browser's push service, and both key material fields are
/// required — a subscription missing either can never be encrypted for.
/// </summary>
public sealed class PushSubscriptionRequestValidator : AbstractValidator<PushSubscriptionRequest>
{
    /// <summary>Creates the validator.</summary>
    public PushSubscriptionRequestValidator()
    {
        RuleFor(request => request.Endpoint)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Endpoint is required.")
            .MaximumLength(PushSubscription.EndpointMaxLength)
                .WithMessage($"Endpoint must be at most {PushSubscription.EndpointMaxLength} characters.")
            .Must(PushEndpointRules.IsAbsoluteHttps)
                .WithMessage("Endpoint must be an absolute https URL.");

        RuleFor(request => request.P256dh)
            .NotEmpty().WithMessage("P256dh is required.")
            .MaximumLength(200).WithMessage("P256dh is too long to be a subscription key.");

        RuleFor(request => request.Auth)
            .NotEmpty().WithMessage("Auth is required.")
            .MaximumLength(100).WithMessage("Auth is too long to be a subscription key.");

        RuleFor(request => request.UserAgent)
            .MaximumLength(300).WithMessage("UserAgent is too long.");
    }
}
