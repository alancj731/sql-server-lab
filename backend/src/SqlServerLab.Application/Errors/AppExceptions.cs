using SqlServerLab.Domain.Jobs;

namespace SqlServerLab.Application.Errors;

/// <summary>Base for errors mapped to RFC 9457 problem details by the API.</summary>
public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string resource) : AppException($"{resource} was not found.");

public sealed class ConflictException(string message) : AppException(message);

public sealed class QuotaExceededException(string message) : AppException(message);

public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationFailedException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Raised by job handlers and infrastructure adapters; drives retry classification.</summary>
public sealed class JobExecutionException(ErrorCategory category, string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ErrorCategory Category { get; } = category;
    public string Code { get; } = code;
}
