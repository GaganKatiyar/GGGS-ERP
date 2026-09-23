using System.Runtime.CompilerServices;
using Gggs.BuildingBlocks.Domain.Exceptions;

namespace Gggs.BuildingBlocks.Domain.Primitives;

/// <summary>Argument and rule checks for domain constructors and factory methods.</summary>
public static class Guard
{
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class
        => value ?? throw new ArgumentNullException(name);

    public static string NotNullOrWhiteSpace(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;

    public static string MaxLength(string value, int maxLength, [CallerArgumentExpression(nameof(value))] string? name = null)
        => value.Length > maxLength
            ? throw new ArgumentException($"Value exceeds the maximum length of {maxLength}.", name)
            : value;

    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null)
        => value == Guid.Empty
            ? throw new ArgumentException("Identifier must not be empty.", name)
            : value;

    /// <summary>Fails a business rule, raising the exception that maps to HTTP 422.</summary>
    public static void Against(bool condition, string errorCode, string message)
    {
        if (condition)
        {
            throw new DomainRuleException(errorCode, message);
        }
    }
}
