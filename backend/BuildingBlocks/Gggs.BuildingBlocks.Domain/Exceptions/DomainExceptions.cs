namespace Gggs.BuildingBlocks.Domain.Exceptions;

/// <summary>Base for every exception this platform raises deliberately.</summary>
public abstract class GggsException(string errorCode, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Stable, machine-readable code, for example <c>LEAVE_INSUFFICIENT_BALANCE</c>.</summary>
    public string ErrorCode { get; } = errorCode;
}

/// <summary>
/// A business rule was violated. Maps to HTTP 422 carrying the error code, so the client can
/// localize the message instead of displaying server text.
/// </summary>
public class DomainRuleException(string errorCode, string message)
    : GggsException(errorCode, message)
{
    /// <summary>Values that help the client explain the failure, such as the available balance.</summary>
    public IReadOnlyDictionary<string, object?> Context { get; init; } = new Dictionary<string, object?>();
}

/// <summary>
/// The entity does not exist, or sits outside the caller's tenant or data scope. All three map to
/// HTTP 404 so the API never reveals that someone else's record exists.
/// </summary>
public sealed class NotFoundException(string entityType, object key)
    : GggsException("NOT_FOUND", $"{entityType} was not found.")
{
    public string EntityType { get; } = entityType;

    public object Key { get; } = key;
}

/// <summary>The caller is authenticated but is not allowed to perform this action.</summary>
public sealed class ForbiddenException(string errorCode, string message)
    : GggsException(errorCode, message);
