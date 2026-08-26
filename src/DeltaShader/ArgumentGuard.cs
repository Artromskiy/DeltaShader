namespace Delta.Shader;

internal static class ArgumentGuard
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1510",
        Justification = "The abstractions target netstandard2.0, where ArgumentNullException.ThrowIfNull is unavailable.")]
    public static T NotNull<T>(T? value, string parameterName)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        return value;
    }
}
