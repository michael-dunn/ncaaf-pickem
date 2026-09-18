using FluentValidation;
using FluentValidation.Results;

namespace NcaafPickEm.Api.Validation;

/// <summary>
/// Runs the registered FluentValidation validator for <typeparamref name="TRequest"/> against the
/// endpoint's bound request body and returns a 400 <c>ValidationProblem</c> on failure
/// (05-Conventions.md). Reusable across every minimal-API endpoint: <c>.AddEndpointFilter&lt;ValidationFilter&lt;TRequest&gt;&gt;()</c>.
/// </summary>
/// <typeparam name="TRequest">The request DTO to validate.</typeparam>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
{
    private readonly IValidator<TRequest> _validator;

    /// <summary>Creates the filter.</summary>
    public ValidationFilter(IValidator<TRequest> validator)
    {
        _validator = validator;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        TRequest? request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return await next(context);
        }

        ValidationResult result = await _validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).ToArray());

        return TypedResults.ValidationProblem(errors);
    }
}
