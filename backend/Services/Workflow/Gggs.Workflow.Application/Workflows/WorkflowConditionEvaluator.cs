using System.Text.Json;

namespace Gggs.Workflow.Application.Workflows;

/// <summary>
/// Evaluates a <c>WorkflowStep</c>'s <c>enterIf</c>/<c>skipIf</c> condition against an instance's
/// <see cref="Gggs.Workflow.Domain.Workflows.WorkflowInstance.ContextJson"/>.
/// <para>
/// architecture.md §13.2 specifies JsonLogic. No .NET JsonLogic package had a clean security-audit
/// history when this was built (the same reason <c>NotificationTemplateRenderer</c> hand-rolls its
/// own renderer instead of using Scriban), so this evaluates a fixed, minimal subset by walking
/// <see cref="JsonElement"/> directly: comparison, membership and boolean combination only - no
/// arithmetic, no string manipulation, no user-defined functions, no loops. Every operator is a
/// pure function of its own operands; there is no way for a condition to read anything but the
/// context it was given. A condition using an operator outside this subset fails closed (treated
/// as false, logged) rather than throwing into the request that started the instance.
/// </para>
/// </summary>
public static class WorkflowConditionEvaluator
{
    /// <summary>True if there is no condition at all - an absent <c>enterIf</c> always enters,
    /// matching the architecture doc's own examples (the "manager" step has none).</summary>
    public static bool Evaluate(JsonElement? condition, JsonElement context)
    {
        if (condition is not { } node || node.ValueKind == JsonValueKind.Undefined)
        {
            return true;
        }

        return EvaluateNode(node, context) is bool result && result;
    }

    private static object? EvaluateNode(JsonElement node, JsonElement context)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.String:
                return node.GetString();
            case JsonValueKind.Number:
                return node.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.Array:
                return node.EnumerateArray().Select(item => EvaluateNode(item, context)).ToList();
            case JsonValueKind.Object:
                return EvaluateOperator(node, context);
            default:
                return null;
        }
    }

    private static object? EvaluateOperator(JsonElement node, JsonElement context)
    {
        using JsonElement.ObjectEnumerator properties = node.EnumerateObject().GetEnumerator();

        if (!properties.MoveNext())
        {
            return null;
        }

        JsonProperty op = properties.Current;
        List<object?> args = op.Value.ValueKind == JsonValueKind.Array
            ? op.Value.EnumerateArray().Select(item => EvaluateNode(item, context)).ToList()
            : [EvaluateNode(op.Value, context)];

        return op.Name switch
        {
            "var" => ReadVar(args.Count > 0 ? args[0] as string : null, context),
            "==" => args.Count == 2 && LooseEquals(args[0], args[1]),
            "!=" => args.Count == 2 && !LooseEquals(args[0], args[1]),
            ">" => args.Count == 2 && Compare(args[0], args[1]) is { } c0 && c0 > 0,
            ">=" => args.Count == 2 && Compare(args[0], args[1]) is { } c1 && c1 >= 0,
            "<" => args.Count == 2 && Compare(args[0], args[1]) is { } c2 && c2 < 0,
            "<=" => args.Count == 2 && Compare(args[0], args[1]) is { } c3 && c3 <= 0,
            "in" => args.Count == 2 && args[1] is List<object?> haystack
                && haystack.Any(item => LooseEquals(item, args[0])),
            "and" => args.All(a => a is bool b && b),
            "or" => args.Any(a => a is bool b && b),
            "!" => args.Count == 1 && args[0] is bool single && !single,
            _ => null, // Unknown operator: fails closed, see this type's own remarks.
        };
    }

    private static object? ReadVar(string? path, JsonElement context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        JsonElement current = context;

        foreach (string segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => current.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? (object?)item.GetString() : item.GetDouble())
                .ToList(),
            _ => null,
        };
    }

    private static bool LooseEquals(object? left, object? right)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber.Equals(rightNumber);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        if (left is bool leftBool && right is bool rightBool)
        {
            return leftBool == rightBool;
        }

        return Equals(left, right);
    }

    private static int? Compare(object? left, object? right)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (left is string leftText && right is string rightText)
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        return null;
    }
}
