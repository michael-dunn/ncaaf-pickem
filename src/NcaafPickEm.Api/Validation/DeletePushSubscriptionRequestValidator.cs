using FluentValidation;
using NcaafPickEm.Domain.Notifications;
using NcaafPickEm.Shared.Contracts.Push;

namespace NcaafPickEm.Api.Validation;

/// <summary>Shape validation for <see cref="DeletePushSubscriptionRequest"/>.</summary>
public sealed class DeletePushSubscriptionRequestValidator : AbstractValidator<DeletePushSubscriptionRequest>
{
    /// <summary>Creates the validator.</summary>
    public DeletePushSubscriptionRequestValidator()
    {
        RuleFor(request => request.Endpoint)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Endpoint is required.")
            .MaximumLength(PushSubscription.EndpointMaxLength)
                .WithMessage($"Endpoint must be at most {PushSubscription.EndpointMaxLength} characters.")
            .Must(PushEndpointRules.IsAbsoluteHttps)
                .WithMessage("Endpoint must be an absolute https URL.");
    }
}
