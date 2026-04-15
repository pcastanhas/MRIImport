using MRIImport.Core.DAL;
using MRIImport.Core.Interfaces;
using MRIImport.Core.Models;
using MRIImport.Core.Services;
using Serilog;

namespace MRIImport.Core.Handlers;

/// <summary>
/// Handles import of budget data into the MRI BUDGETS table.
/// Validates: entity, GL account, department, basis, budget type, period.
/// Supports optional delete-before-insert (updateIfExists) via ExtraParameters.
/// </summary>
public class BudgetHandler : IImportHandler
{
    private readonly ImportFileReader _reader;

    public BudgetHandler(ImportFileReader reader)
    {
        _reader = reader;
    }

    public string ImportName => "Import Budgets";
    public string? TemplateFileName => "BUDGET_UPLOAD_TEMPLATE.xlsx";

    public IEnumerable<string> InfoText =>
    [
        "Upload an XLSX or CSV file to import budget data into MRI.",
        "The file must contain a worksheet named 'IMPORT' (XLSX) or be a standard CSV.",
        "Required fields: PERIOD, ENTITYID, DEPARTMENT, ACCTNUM, BASIS, BUDTYPE, ACTIVITY.",
        "PERIOD format: YYYYMM (e.g. 202401 for January 2024).",
        "Optionally check 'Update if exists' to replace existing budget rows for the same period/entity/department/account/basis/budtype combination."
    ];

    public async Task<ImportResult> ExecuteAsync(ImportContext context)
    {
        Log.Information("BudgetHandler.ExecuteAsync started for {File}", context.OriginalFileName);

        // ── 1. Parse file ────────────────────────────────────────────────────
        List<MRIBudget> rows;
        try
        {
            rows = _reader.Read<MRIBudget>(context.FilePath);
        }
        catch (ImportFileException ex)
        {
            return ImportResult.Failed(ex.Message);
        }

        if (rows.Count == 0)
            return ImportResult.Failed("The import file contained no data rows.");

        // ── 2. Extra parameters ───────────────────────────────────────────────
        context.ExtraParameters.TryGetValue("updateIfExists", out var updateStr);
        bool updateIfExists = bool.TryParse(updateStr, out var b) && b;

        // ── 3. Load reference data ────────────────────────────────────────────
        var db = new MriDb(context.ConnectionString);

        List<MRIEntity>    entities;
        List<MRIGLAccount> accounts;
        List<MRIDepartment> departments;
        List<MRIBasis>     basisList;
        List<MRIBudgetType> budgetTypes;

        try
        {
            entities    = await db.GetEntityListAsync();
            accounts    = await db.GetAccountListAsync();
            departments = await db.GetDepartmentListAsync();
            basisList   = await db.GetBasisListAsync();
            budgetTypes = await db.GetBudgetTypeListAsync();
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
            r.ENTITYID   = (r.ENTITYID   ?? string.Empty).PadLeft(5, '0').ToUpper();
            r.ACCTNUM    = (r.ACCTNUM    ?? string.Empty).ToUpper();
            r.DEPARTMENT = (r.DEPARTMENT ?? string.Empty).ToUpper();
            r.BASIS      = (r.BASIS      ?? string.Empty).ToUpper();
            r.BUDTYPE    = (r.BUDTYPE    ?? string.Empty).ToUpper();

            if (!entities.Any(e => e.ENTITYID == r.ENTITYID))
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Entity '{r.ENTITYID}' is invalid." });

            if (!accounts.Any(a => a.ACCTNUM == r.ACCTNUM))
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Account '{r.ACCTNUM}' is invalid or inactive." });

            if (!departments.Any(d => d.DEPARTMENT == r.DEPARTMENT))
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Department '{r.DEPARTMENT}' is invalid." });

            if (!basisList.Any(b => b.BASIS == r.BASIS))
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Basis '{r.BASIS}' is invalid." });

            if (!budgetTypes.Any(bt => bt.BUDTYPE == r.BUDTYPE))
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Budget type '{r.BUDTYPE}' is invalid." });

            var periodDate = r.PERIOD?.Right(2) + "/01/" + r.PERIOD?.Left(4);
            if (r.PERIOD.IsNullOrEmpty() || !periodDate.IsDate())
                errors.Add(new ImportError { LineNumber = r.LineNumber, Message = $"Period '{r.PERIOD}' is invalid (expected YYYYMM)." });
        }

        if (errors.Count > 0)
        {
            Log.Warning("Budget validation failed with {Count} errors.", errors.Count);
            return ImportResult.ValidationFailed("File not imported due to validation errors.", errors);
        }

        // ── 5. Upload ─────────────────────────────────────────────────────────
        try
        {
            int count = await db.UploadBudgetAsync(rows, context.UserId, updateIfExists);
            var msg = $"{count} budget rows imported successfully.";
            Log.Information("Budget import succeeded. {Message}", msg);
            return ImportResult.Succeeded(msg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Budget upload failed");
            return ImportResult.Failed("Import failed while writing to the database.", ex.Message);
        }
    }
}
