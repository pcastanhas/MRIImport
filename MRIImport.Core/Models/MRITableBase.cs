using System.Reflection;
using MRIImport.Core.Interfaces;

namespace MRIImport.Core.Models;

/// <summary>
/// Abstract base class for all MRI import table models.
/// Provides the shared reflection-based SetProperty / GetProperty implementation
/// so individual model classes stay clean — just properties and a TableName.
/// </summary>
public abstract class MRITableBase : IMRITable
{
    public abstract string TableName { get; }
    public int LineNumber { get; set; }

    /// <summary>
    /// Sets a named property from a string value read out of the import file.
    /// Silently ignores columns in the file that have no matching property —
    /// this allows import files to have extra informational columns without failing.
    /// </summary>
    public void SetProperty(string propertyName, string value)
    {
        var prop = GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (prop is null || !prop.CanWrite)
            return; // Column in file has no matching property — skip silently

        prop.SetValue(this, value);
    }

    /// <summary>
    /// Gets the string value of a named property.
    /// Returns null if the property does not exist.
    /// </summary>
    public string? GetProperty(string propertyName)
    {
        var prop = GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        return prop?.GetValue(this)?.ToString();
    }

    /// <summary>
    /// Returns all public instance properties that are actual data columns
    /// (excludes the interface/base properties).
    /// Used by the DAL to dynamically build INSERT statements.
    /// </summary>
    public IEnumerable<PropertyInfo> GetDataProperties()
    {
        var excluded = new HashSet<string>
        {
            nameof(TableName),
            nameof(LineNumber)
        };

        return GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !excluded.Contains(p.Name) && p.CanRead && p.CanWrite);
    }
}
