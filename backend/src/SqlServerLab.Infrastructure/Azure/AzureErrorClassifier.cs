using SqlServerLab.Domain.Jobs;

namespace SqlServerLab.Infrastructure.Azure;

/// <summary>Maps ARM/HTTP failures to job error categories, which drive retry behaviour.</summary>
public static class AzureErrorClassifier
{
    private static readonly HashSet<string> TransientCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AllocationFailed", "ZonalAllocationFailed", "OverconstrainedAllocationRequest", "OverconstrainedZonalAllocationRequest",
        "RetryableError", "InternalServerError", "ServiceUnavailable", "TooManyRequests", "GatewayTimeout",
        "AnotherOperationInProgress", "OperationPreempted", "Conflict",
    };

    private static readonly HashSet<string> PermanentCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SkuNotAvailable", "InvalidTemplate", "InvalidTemplateDeployment", "InvalidParameter", "InvalidRequestContent",
        "ImageNotFound", "PlatformImageNotFound", "MarketplacePurchaseEligibilityFailed", "LocationNotAvailableForResourceType",
        "InvalidResourceName", "BadRequest", "PasswordTooLong", "InvalidSubnet",
    };

    private static readonly HashSet<string> AuthorizationCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AuthorizationFailed", "LinkedAuthorizationFailed", "Forbidden", "InvalidAuthenticationToken", "AuthenticationFailed",
        "RequestDisallowedByPolicy",
    };

    public static ErrorCategory Classify(int status, string? code)
    {
        if (!string.IsNullOrEmpty(code))
        {
            if (code.Contains("Quota", StringComparison.OrdinalIgnoreCase) || code.Equals("OperationNotAllowed", StringComparison.OrdinalIgnoreCase))
            {
                return ErrorCategory.Quota;
            }

            if (AuthorizationCodes.Contains(code))
            {
                return ErrorCategory.Authorization;
            }

            if (TransientCodes.Contains(code))
            {
                return ErrorCategory.Transient;
            }

            if (PermanentCodes.Contains(code))
            {
                return ErrorCategory.Permanent;
            }
        }

        return status switch
        {
            401 or 403 => ErrorCategory.Authorization,
            408 or 409 or 429 or >= 500 => ErrorCategory.Transient,
            400 or 404 or 422 => ErrorCategory.Permanent,
            _ => ErrorCategory.Transient,
        };
    }
}
