namespace MRIImport.Core.Services;

/// <summary>
/// Native .NET replacements for the CTSCore.dll string extension methods
/// used throughout the original application.
/// </summary>
public static class StringExtensions
{
    /// <summary>Returns the leftmost n characters of a string.</summary>
    public static string Left(this string value, int length)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= length ? value : value[..length];
    }

    /// <summary>Returns the rightmost n characters of a string.</summary>
    public static string Right(this string value, int length)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= length ? value : value[^length..];
    }

    /// <summary>Returns true if the string is null or whitespace.</summary>
    public static bool IsNullOrEmpty(this string? value) =>
        string.IsNullOrWhiteSpace(value);

    /// <summary>Returns true if the string can be parsed as a decimal number.</summary>
    public static bool IsNumeric(this string? value) =>
        decimal.TryParse(value, out _);

    /// <summary>Returns true if the string can be parsed as a DateTime.</summary>
    public static bool IsDate(this string? value) =>
        DateTime.TryParse(value, out _);
}
