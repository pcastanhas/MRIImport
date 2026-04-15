using MRIImport.Core.Models;

namespace MRIImport.Core.Interfaces;

/// <summary>
/// The core extensibility contract. Every import type (Journal, Budget, CMMisc, etc.)
/// implements this interface. Adding a new import to the application requires only:
///   1. A new IMRITable model
///   2. A new IImportHandler implementation
///   3. A new Blazor page that references the handler
/// No changes to core infrastructure are needed.
/// </summary>
public interface IImportHandler
{
    /// <summary>Display name shown as the page title.</summary>
    string ImportName { get; }

    /// <summary>
    /// The filename of the Excel template available for download.
    /// Return null if no template is provided.
    /// </summary>
    string? TemplateFileName { get; }

    /// <summary>
    /// Help/info text shown in the info dialog on the import page.
    /// Supports plain text. Each string in the array is a paragraph.
    /// </summary>
    IEnumerable<string> InfoText { get; }

    /// <summary>
    /// Parses the saved import file into a list of typed records,
    /// then validates, and uploads to the database.
    /// Returns an ImportResult describing success, errors, and the status message.
    /// </summary>
    Task<ImportResult> ExecuteAsync(ImportContext context);
}
