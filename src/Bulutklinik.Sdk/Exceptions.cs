using System.Text.Json;

namespace Bulutklinik.Sdk;

/// <summary>Base class for every exception thrown by the SDK.</summary>
public class BulutklinikException : Exception
{
    public BulutklinikException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Network failure, timeout, DNS or TLS error — no usable HTTP response.</summary>
public sealed class TransportException : BulutklinikException
{
    public TransportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Structured context attached to every <see cref="ApiException"/>. ErrorType may
/// be a string label or a numeric code; ResultType and RetryAfter may be null.
/// </summary>
public sealed record ApiErrorContext(
    int HttpStatus,
    int? ResultType,
    object? ErrorType,
    JsonElement Data,
    string Method,
    string Path,
    int? RetryAfter);

/// <summary>An HTTP response was received but the call was not successful.</summary>
public class ApiException : BulutklinikException
{
    public ApiErrorContext Context { get; }

    public ApiException(string message, ApiErrorContext context)
        : base(message)
    {
        Context = context;
    }

    internal static ApiException Create(string message, ApiErrorContext context)
    {
        if (context.ResultType == 2)
        {
            return new AuthenticationException(message, context);
        }

        // resultType 4 used to trigger a silent refresh. On the partner surface
        // there is nothing to refresh, so say what the caller actually has to do.
        if (context.ResultType == 4)
        {
            return new AuthenticationException(
                message + " The partner token is expired or invalid — install a newly issued token;"
                + " the SDK cannot refresh it.",
                context);
        }

        bool validation =
            (context.ErrorType is string s && string.Equals(s, "validation", StringComparison.OrdinalIgnoreCase))
            || context.HttpStatus == 422;
        if (validation)
        {
            return new ValidationException(message, context);
        }

        return context.HttpStatus switch
        {
            401 => new AuthenticationException(message, context),
            403 => new AuthorizationException(message, context),
            404 => new NotFoundException(message, context),
            429 => new RateLimitException(message, context),
            _ => new ApiException(message, context),
        };
    }
}

/// <summary>422, or an envelope with a string errorType of "validation".</summary>
public sealed class ValidationException : ApiException
{
    public ValidationException(string message, ApiErrorContext context) : base(message, context)
    {
    }
}

/// <summary>401, a revoked token (resultType 2), or an expired one (resultType 4).</summary>
public sealed class AuthenticationException : ApiException
{
    public AuthenticationException(string message, ApiErrorContext context) : base(message, context)
    {
    }
}

/// <summary>
/// 403 — the token authenticated but is not permitted. Either it lacks the
/// <c>apiouther</c> scope or it resolves to a user with no company. The company
/// boundary comes from the token, never from request input, so retrying with
/// different body parameters will not help.
/// </summary>
public sealed class AuthorizationException : ApiException
{
    public AuthorizationException(string message, ApiErrorContext context) : base(message, context)
    {
    }
}

/// <summary>404.</summary>
public sealed class NotFoundException : ApiException
{
    public NotFoundException(string message, ApiErrorContext context) : base(message, context)
    {
    }
}

/// <summary>429 — throttled. The seconds-to-wait are in <c>Context.RetryAfter</c>.</summary>
public sealed class RateLimitException : ApiException
{
    public RateLimitException(string message, ApiErrorContext context) : base(message, context)
    {
    }
}
