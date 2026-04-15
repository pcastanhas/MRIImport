using MRIImport.Core.DAL;
using MRIImport.Core.Interfaces;
using MRIImport.Core.Models;
using MRIImport.Core.Services;
using Serilog;

namespace MRIImport.Core.Handlers;

/// <summary>
/// Handles import of miscellaneous tenant charges into MRI CMMISC / CMBTCH.
/// Additional processing beyond a direct insert:
///   - Creates a batch header record in CMBTCH
///   - Allocates and updates the CMBATCH sequence in MRISEQ
/// Batch date and description are passed via ImportContext.ExtraParameters.
/// </summary>
public class CmMiscHandler : IImportHandler
{
    private readonly ImportFileReader _reader;

    public CmMiscHandler(ImportFileReader reader)
    {
        _reader = reader;
    }

    public string ImportName => "Import Tenant Charges";
    public string? TemplateFileName => "CMMISC_UPLOAD_TEMPLATE.xlsx";

    public IEnumerable<string> InfoText =>
    [
        "Upload an XLSX or CSV file to import miscellaneous tenant charges into MRI.",
        "The file must contain a worksheet named 'IMPORT' (XLSX) or be a standard CSV.",
        "Required fields: ITEM, BLDGID, LEASID, TRANDATE, INCCAT, SRCCODE, DESCRPTN, TRANAMT.",
        "A batch header record will be created automatically using the batch date and description you provide.",
        "All rows are imported as a single unposted batch."
    ];

    public async Task<ImportResult> ExecuteAsync(ImportContext context)
    {
        Log.Information("CmMiscHandler.ExecuteAsync started for {File}", context.OriginalFileName);

        // ── 1. Extra parameters ───────────────────────────────────────────────
        context.ExtraParameters.TryGetValue("batchDate", out var batchDate);
        context.ExtraParameters.TryGetValue("batchDescription", out var batchDescription);

        if (string.IsNullOrWhiteSpace(batchDate))
            return ImportResult.Failed("A batch date is required for tenant charge imports.");

        if (string.IsNullOrWhiteSpace(batchDescription))
            return ImportResult.Failed("A batch description is required for tenant charge imports.");

        // ── 2. Parse file ────────────────────────────────────────────────────
        List<MRICMMisc> rows;
        try
        {
            rows = _reader.Read<MRICMMisc>(context.FilePath);
        }
        catch (ImportFileException ex)
        {
            return ImportResult.Failed(ex.Message);
        }

        if (rows.Count == 0)
            return ImportResult.Failed("The import file contained no data rows.");

        // ── 3. Load reference data ────────────────────────────────────────────
        var db = new MriDb(context.ConnectionString);

        List<MRIBuilding>       buildings;
        List<MRILease>          leases;
        List<MRIIncomeCategory> incomeCategories;

        try
        {
            buildings        = await db.GetBuildingListAsync();
            leases           = await db.GetLeaseListAsync();
            incomeCategories = await db.GetIncomeCategoryListAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load reference data");
            return ImportResult.Failed("Could not load reference data from the database.", ex.Message);
        }

        // ── 4. Validate ───────────────────────────────────────────────────────
        var errors = new List<ImportError>();

        foreach (var r in rows)
        {
            r.BLDGID = (r.BLDGID ?? string.Empty).ToUpper();
            r.LEASID = (r.LEASID ?? string.Empty).ToUpper();
            r.INCCAT = (r.INCCAT ?? string.Empty).ToUpper();

            if (r.BLDGID.IsNullOrEmpty() || !buildings.Any(b => b.BLDGID == r.BLDGID))
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Building '{r.BLDGID}' is invalid." });

            if (r.LEASID.IsNullOrEmpty() || !leases.Any(l => l.BLDGID == r.BLDGID && l.LEASID == r.LEASID))
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Lease '{r.LEASID}' is invalid for building '{r.BLDGID}'." });

            if (r.INCCAT.IsNullOrEmpty() || !incomeCategories.Any(i => i.INCCAT == r.INCCAT))
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Income category '{r.INCCAT}' is invalid." });

            if (r.TRANAMT.IsNullOrEmpty() || !r.TRANAMT.IsNumeric())
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = "Transaction amount is missing or not a valid number." });

            if (!r.TRANDATE.IsDate())
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Transaction date '{r.TRANDATE}' is not a valid date." });

            if (r.ITEM.IsNullOrEmpty())
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = "ITEM is required." });
        }

        if (errors.Count > 0)
        {
            Log.Warning("CMMisc validation failed with {Count} errors.", errors.Count);
            return ImportResult.ValidationFailed("File not imported due to validation errors.", errors);
        }

        // ── 5. Upload ─────────────────────────────────────────────────────────
        try
        {
            var batchId = await db.UploadCmMiscAsync(rows, context.UserId, batchDate!, batchDescription!);
            var msg = $"{rows.Count} tenant charge rows imported into unposted batch {batchId}.";
            Log.Information("CMMisc import succeeded. {Message}", msg);
            return ImportResult.Succeeded(msg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CMMisc upload failed");
            return ImportResult.Failed("Import failed while writing to the database.", ex.Message);
        }
    }
}
