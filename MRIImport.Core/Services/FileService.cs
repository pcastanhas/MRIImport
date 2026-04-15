using Microsoft.Extensions.Configuration;
using Serilog;

namespace MRIImport.Core.Services;

/// <summary>
/// Handles saving uploaded import files to the configured UNC path.
/// The file is retained on disk for audit purposes.
/// A timestamp is appended to the filename to prevent collisions.
/// </summary>
public class FileService
{
    private readonly string _savePath;

    public FileService(IConfiguration configuration)
    {
        _savePath = configuration
            .GetSection("ApplicationSettings")
            .GetValue<string>("FileSavePath")
            ?? throw new InvalidOperationException(
                "ApplicationSettings:FileSavePath is not configured in appsettings.json");
    }

    /// <summary>
    /// Saves the uploaded file stream to the configured UNC path.
    /// Returns the full path of the saved file.
    /// Filename is stamped with a UTC timestamp to avoid collisions:
    ///   originalname.xlsx  →  originalname_20240115_143022.xlsx
    /// </summary>
    public async Task<string> SaveAsync(Stream fileStream, string originalFileName)
    {
        var savePath = _savePath.TrimEnd('\\', '/');
        var ext = Path.GetExtension(originalFileName);
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var savedName = $"{baseName}_{stamp}{ext}";
        var fullPath = Path.Combine(savePath, savedName);

        Log.Information("Saving uploaded file {OriginalFileName} to {FullPath}", originalFileName, fullPath);

        try
        {
            Directory.CreateDirectory(savePath); // no-op if it already exists
            await using var dest = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
            await fileStream.CopyToAsync(dest);
            Log.Information("File saved successfully: {FullPath}", fullPath);
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Failed to save file {OriginalFileName} to {FullPath}", originalFileName, fullPath);
            throw;
        }

        return fullPath;
    }
}
