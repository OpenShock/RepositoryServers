using OneOf;

namespace OpenShock.RepositoryServer.Tests.Integration;

/// <summary>
/// Assertions over the <see cref="OneOf{T0,T1}"/> results the admin services return.
/// </summary>
/// <remarks>
/// Written against <see cref="IOneOf"/> rather than a specific arity, so the same two helpers cover
/// every result shape and keep working when an operation gains an outcome. Asserting on the outcome
/// type is what makes these tests independent of how any caller words the refusal.
/// </remarks>
public static class AdminResultAssertions
{
    /// <summary>Asserts the operation produced <typeparamref name="T"/>, and returns it.</summary>
    public static T ShouldBe<T>(this IOneOf result)
    {
        if (result.Value is not T expected)
        {
            Assert.Fail($"Expected {typeof(T).Name}, got {result.Value?.GetType().Name ?? "null"}");
            return default!;
        }

        return expected;
    }
}
