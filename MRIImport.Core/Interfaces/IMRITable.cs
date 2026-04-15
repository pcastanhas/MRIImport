namespace MRIImport.Core.Interfaces;

/// <summary>
/// Base interface that all MRI import table models must implement.
/// Provides the contract for reflection-based property mapping from
/// spreadsheet columns to model properties.
/// </summary>
public interface IMRITable
{
    /// <summary>The target MRI database table name.</summary>
    string TableName { get; }

    /// <summary>The source file row number, used for error reporting.</summary>
    int LineNumber { get; set; }

    /// <summary>
    /// Sets a property by name using the value read from the import file.
    /// </summary>
    void SetProperty(string propertyName, string value);

    /// <summary>
    /// Gets a property value by name.
    /// </summary>
    string? GetProperty(string propertyName);
}
