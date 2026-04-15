namespace MRIImport.Core.Models;

/// <summary>
/// Passed into IImportHandler.ExecuteAsync() carrying everything the handler needs.
/// </summary>
public class ImportContext
{
    /// <summary>Full path to the saved import file on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>Original filename as uploaded by the user.</summary>
    public required string OriginalFileName { get; init; }

    /// <summary>MRI User ID passed in from the query string.</summary>
    public required string UserId { get; init; }

    /// <summary>Database connection string key resolved from the db query parameter.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Any additional parameters specific to an import type (e.g. batch date for CMMisc).</summary>
    public Dictionary<string, string> ExtraParameters { get; init; } = new();
}

/// <summary>
/// Returned by IImportHandler.ExecuteAsync() to describe the outcome.
/// </summary>
public class ImportResult
{
    /// <summary>True if the import completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// The handler-specific success or failure summary message.
    /// e.g. "47 rows imported into 3 journal entries. First reference: 000123"
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Per-row validation errors. Empty on success.</summary>
    public List<ImportError> ValidationErrors { get; init; } = new();

    /// <summary>Technical detail for non-validation failures (shown in expandable panel).</summary>
    public string? TechnicalDetail { get; init; }

    public static ImportResult Succeeded(string message) =>
        new() { Success = true, Message = message };

    public static ImportResult Failed(string message, string? technicalDetail = null) =>
        new() { Success = false, Message = message, TechnicalDetail = technicalDetail };

    public static ImportResult ValidationFailed(string message, List<ImportError> errors) =>
        new() { Success = false, Message = message, ValidationErrors = errors };
}

/// <summary>
/// A single per-row validation error, mirroring the existing ErrorLog model.
/// </summary>
public class ImportError
{
    public int LineNumber { get; init; }
    public string Message { get; init; } = string.Empty;
}
