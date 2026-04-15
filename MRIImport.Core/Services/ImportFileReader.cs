using ClosedXML.Excel;
using MRIImport.Core.Interfaces;
using Serilog;

namespace MRIImport.Core.Services;

/// <summary>
/// Shared, generic file reader that parses an XLSX or CSV import file
/// into a List&lt;T&gt; where T implements IMRITable.
///
/// Rules:
///  - XLSX: reads the worksheet named "IMPORT". If not found, rejects the file.
///  - CSV:  reads all lines.
///  - Row 1 is always the header row — column names must match T's property names
///    (case-insensitive). Unknown columns are silently skipped.
///  - Returns the typed list to the calling handler for validation and upload.
/// </summary>
public class ImportFileReader
{
    /// <summary>
    /// Reads the file at <paramref name="filePath"/> and returns a typed list.
    /// Throws <see cref="ImportFileException"/> on any read/parse failure.
    /// </summary>
    public List<T> Read<T>(string filePath) where T : IMRITable, new()
    {
        Log.Information("ImportFileReader.Read<{Type}>({FilePath})", typeof(T).Name, filePath);

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".xlsx" or ".xls" => ReadXlsx<T>(filePath),
            ".csv" => ReadCsv<T>(filePath),
            _ => throw new ImportFileException(
                $"Unsupported file type '{ext}'. Only .xlsx, .xls, and .csv are accepted.")
        };
    }

    // ── XLSX ─────────────────────────────────────────────────────────────────

    private static List<T> ReadXlsx<T>(string filePath) where T : IMRITable, new()
    {
        Log.Information("Reading XLSX file: {FilePath}", filePath);
        var list = new List<T>();

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // LoadOptions.RecalculateAllFormulas = false — we read the cached values
            // that Excel already computed and stored in the file. This avoids
            // ClosedXML formula-engine crashes on complex/unsupported Excel functions
            // (ROUND, SUMIF, IF with range references, etc.).
            var loadOptions = new LoadOptions { RecalculateAllFormulas = false };
            using var workbook = new XLWorkbook(stream, loadOptions);

            // Strict: must have a sheet named "IMPORT"
            var sheet = workbook.Worksheets
                .FirstOrDefault(ws => ws.Name.Equals("IMPORT", StringComparison.OrdinalIgnoreCase))
                ?? throw new ImportFileException(
                    "The uploaded workbook does not contain a worksheet named 'IMPORT'. " +
                    "Please use the provided template.");

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow < 2)
                throw new ImportFileException("The IMPORT worksheet contains no data rows.");

            // Build column-index -> property-name map from header row.
            // Header cells are always plain text so GetString() is safe here.
            var headers = new Dictionary<int, string>();
            var lastCol = sheet.LastColumnUsed()!.ColumnNumber();
            for (int col = 1; col <= lastCol; col++)
            {
                var header = sheet.Row(1).Cell(col).GetString().Trim().ToUpper();
                if (!string.IsNullOrWhiteSpace(header))
                    headers[col] = header;
            }

            if (headers.Count == 0)
                throw new ImportFileException("The IMPORT worksheet header row appears to be empty.");

            // Read data rows using cached values to avoid formula re-evaluation
            for (int row = 2; row <= lastRow; row++)
            {
                var record = new T { LineNumber = row };
                bool hasData = false;

                foreach (var (col, propName) in headers)
                {
                    var cellValue = GetCellValueSafe(sheet.Row(row).Cell(col)).Trim();
                    if (!string.IsNullOrWhiteSpace(cellValue))
                    {
                        record.SetProperty(propName, cellValue);
                        hasData = true;
                    }
                }

                // Skip completely empty rows
                if (hasData)
                    list.Add(record);
            }

            Log.Information("XLSX read complete. {Count} data rows loaded.", list.Count);
        }
        catch (ImportFileException)
        {
            throw; // re-throw our own exceptions unchanged
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error reading XLSX file {FilePath}", filePath);
            throw new ImportFileException($"Could not read the uploaded file: {ex.Message}", ex);
        }

        return list;
    }

    /// <summary>
    /// Safely reads a cell value without triggering formula recalculation.
    /// For formula cells, returns the cached value Excel stored in the file.
    /// Falls back gracefully to empty string rather than throwing.
    /// </summary>
    private static string GetCellValueSafe(IXLCell cell)
    {
        try
        {
            // For formula cells, CachedValue holds what Excel last calculated.
            // This is exactly what we want and avoids invoking ClosedXML's
            // formula engine, which can crash on unsupported/complex functions.
            if (cell.HasFormula)
            {
                var cached = cell.CachedValue;
                return cached.IsText    ? cached.GetText()
                     : cached.IsNumber  ? cached.GetNumber().ToString()
                     : cached.IsBoolean ? cached.GetBoolean().ToString()
                     : cached.IsBlank   ? string.Empty
                     : cached.ToString() ?? string.Empty;
            }

            // Plain value cell — safe to call GetString() directly
            return cell.GetString();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read cell {Address} — treating as empty", cell.Address);
            return string.Empty;
        }
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    private static List<T> ReadCsv<T>(string filePath) where T : IMRITable, new()
    {
        Log.Information("Reading CSV file: {FilePath}", filePath);
        var list = new List<T>();

        try
        {
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                throw new ImportFileException("The CSV file contains no data rows.");

            var headers = ParseCsvLine(lines[0])
                .Select((h, i) => (Index: i, Name: h.Trim().ToUpper()))
                .Where(h => !string.IsNullOrWhiteSpace(h.Name))
                .ToDictionary(h => h.Index, h => h.Name);

            for (int i = 1; i < lines.Length; i++)
            {
                var fields = ParseCsvLine(lines[i]);
                var record = new T { LineNumber = i + 1 };
                bool hasData = false;

                for (int col = 0; col < fields.Length; col++)
                {
                    if (!headers.TryGetValue(col, out var propName)) continue;
                    var value = fields[col].Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        record.SetProperty(propName, value);
                        hasData = true;
                    }
                }

                if (hasData)
                    list.Add(record);
            }

            Log.Information("CSV read complete. {Count} data rows loaded.", list.Count);
        }
        catch (ImportFileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error reading CSV file {FilePath}", filePath);
            throw new ImportFileException($"Could not read the uploaded file: {ex.Message}", ex);
        }

        return list;
    }

    /// <summary>
    /// RFC-4180 compliant CSV parser that handles quoted fields containing commas.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                // Handle escaped quote ("")
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}

/// <summary>
/// Thrown when the import file cannot be read or is structurally invalid.
/// Distinct from validation errors (which are per-row business rule failures).
/// </summary>
public class ImportFileException : Exception
{
    public ImportFileException(string message) : base(message) { }
    public ImportFileException(string message, Exception inner) : base(message, inner) { }
}
