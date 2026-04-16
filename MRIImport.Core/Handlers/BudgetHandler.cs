using MRIImport.Core.DAL;
using MRIImport.Core.Interfaces;
using MRIImport.Core.Models;
using MRIImport.Core.Services;
using Serilog;

namespace MRIImport.Core.Handlers;

/// <summary>
/// Handles import of budget data into the MRI BUDGETS table.
///
/// Validations (derived from DDL constraints):
///   - All required NOT NULL fields present (PK: PERIOD, ENTITYID, DEPARTMENT,
///     ACCTNUM, BASIS, BUDTYPE)
///   - PERIOD is a valid YYYYMM value
///   - ENTITYID exists in ENTITY            (FK_BUDGETS_ENTITYID_ENTITY — WITH CHECK)
///   - ACCTNUM exists in GACC               (FK_BUDGETS_ACCTNUM_GACC   — WITH CHECK)
///   - DEPARTMENT exists in GDEP            (FK_BUDGETS_DEPARTMENT_GDEP — WITH CHECK)
///   - BASIS exists in BTYP                 (FK_BUDGETS_BASIS_BTYP      — WITH CHECK)
///   - BUDTYPE exists in GBTY               (FK_BUDGETS_BUDTYPE_GBTY    — WITH CHECK)
///   - Duplicate PK within the import file
///
/// Upload modes:
///   - Insert only (default): plain INSERT; fails on duplicate PK in DB.
///   - Upsert (updateExisting=true): SQL MERGE — updates existing rows,
///     inserts new ones. No rows are deleted.
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
        "Required fields: PERIOD, ENTITYID, DEPARTMENT, ACCTNUM, BASIS, BUDTYPE.",
        "PERIOD format: YYYYMM (e.g. 202401 for January 2024).",
        "Check 'Update existing records' to update rows that already exist in the database " +
            "(matched on ACCTNUM + ENTITYID + DEPARTMENT + BASIS + BUDTYPE + PERIOD). " +
            "Without this option, the import will fail if any row already exists.",
        "All rows are saved in a single transaction — if any row fails, nothing is saved."
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
        context.ExtraParameters.TryGetValue("updateExisting", out var updateStr);
        bool updateExisting = bool.TryParse(updateStr, out var b) && b;

        // ── 3. Normalise ──────────────────────────────────────────────────────
        foreach (var r in rows)
        {
            r.ENTITYID   = (r.ENTITYID   ?? string.Empty).Trim().ToUpper();
            r.ACCTNUM    = (r.ACCTNUM    ?? string.Empty).Trim().ToUpper();
            r.DEPARTMENT = (r.DEPARTMENT ?? string.Empty).Trim().ToUpper();
            r.BASIS      = (r.BASIS      ?? string.Empty).Trim().ToUpper();
            r.BUDTYPE    = (r.BUDTYPE    ?? string.Empty).Trim().ToUpper();
            r.PERIOD     = (r.PERIOD     ?? string.Empty).Trim();
        }

        // ── 4. Load reference data ────────────────────────────────────────────
        var db = new MriDb(context.ConnectionString);

        List<MRIEntity>     entities;
        List<MRIGLAccount>  accounts;
        List<MRIDepartment> departments;
        List<MRIBasis>      basisList;
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

        // ── 5. Validate ───────────────────────────────────────────────────────
        var errors = new List<ImportError>();
        var pkSeen = new HashSet<string>();

        foreach (var r in rows)
        {
            // ── Required / PK fields ──────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(r.PERIOD))
                errors.Add(Err(r, "Period (PERIOD) is required."));

            if (string.IsNullOrWhiteSpace(r.ENTITYID))
                errors.Add(Err(r, "Entity ID (ENTITYID) is required."));

            if (string.IsNullOrWhiteSpace(r.DEPARTMENT))
                errors.Add(Err(r, "Department (DEPARTMENT) is required."));

            if (string.IsNullOrWhiteSpace(r.ACCTNUM))
                errors.Add(Err(r, "Account number (ACCTNUM) is required."));

            if (string.IsNullOrWhiteSpace(r.BASIS))
                errors.Add(Err(r, "Basis (BASIS) is required."));

            if (string.IsNullOrWhiteSpace(r.BUDTYPE))
                errors.Add(Err(r, "Budget type (BUDTYPE) is required."));

            // ── PERIOD format: YYYYMM ─────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.PERIOD))
            {
                var periodDate = r.PERIOD.Right(2) + "/01/" + r.PERIOD.Left(4);
                if (r.PERIOD.Length != 6 || !periodDate.IsDate())
                    errors.Add(Err(r, $"Period '{r.PERIOD}' is not a valid period. Expected format is YYYYMM (e.g. 202401)."));
            }

            // ── FK: ENTITYID → ENTITY ─────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.ENTITYID) &&
                !entities.Any(e => e.ENTITYID == r.ENTITYID))
                errors.Add(Err(r, $"Entity '{r.ENTITYID}' does not exist."));

            // ── FK: ACCTNUM → GACC ────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.ACCTNUM) &&
                !accounts.Any(a => a.ACCTNUM == r.ACCTNUM))
                errors.Add(Err(r, $"Account number '{r.ACCTNUM}' does not exist or is not active."));

            // ── FK: DEPARTMENT → GDEP ─────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.DEPARTMENT) &&
                !departments.Any(d => d.DEPARTMENT == r.DEPARTMENT))
                errors.Add(Err(r, $"Department '{r.DEPARTMENT}' does not exist."));

            // ── FK: BASIS → BTYP ─────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.BASIS) &&
                !basisList.Any(bs => bs.BASIS == r.BASIS))
                errors.Add(Err(r, $"Basis '{r.BASIS}' does not exist."));

            // ── FK: BUDTYPE → GBTY ────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.BUDTYPE) &&
                !budgetTypes.Any(bt => bt.BUDTYPE == r.BUDTYPE))
                errors.Add(Err(r, $"Budget type '{r.BUDTYPE}' does not exist."));

            // ── PK duplicate within file ──────────────────────────────────────
            // PK: ACCTNUM + ENTITYID + DEPARTMENT + BASIS + BUDTYPE + PERIOD
            if (!string.IsNullOrWhiteSpace(r.ACCTNUM)    &&
                !string.IsNullOrWhiteSpace(r.ENTITYID)   &&
                !string.IsNullOrWhiteSpace(r.DEPARTMENT) &&
                !string.IsNullOrWhiteSpace(r.BASIS)      &&
                !string.IsNullOrWhiteSpace(r.BUDTYPE)    &&
                !string.IsNullOrWhiteSpace(r.PERIOD))
            {
                var pk = $"{r.ACCTNUM}|{r.ENTITYID}|{r.DEPARTMENT}|{r.BASIS}|{r.BUDTYPE}|{r.PERIOD}";
                if (!pkSeen.Add(pk))
                    errors.Add(Err(r,
                        $"Duplicate row in file: account '{r.ACCTNUM}', entity '{r.ENTITYID}', " +
                        $"department '{r.DEPARTMENT}', basis '{r.BASIS}', " +
                        $"budget type '{r.BUDTYPE}', period '{r.PERIOD}' appears more than once."));
            }
        }

        if (errors.Count > 0)
        {
            Log.Warning("Budget validation failed with {Count} errors.", errors.Count);
            return ImportResult.ValidationFailed("File not imported due to validation errors.", errors);
        }

        // ── 6. Upload ─────────────────────────────────────────────────────────
        try
        {
            var (inserted, updated) = await db.UploadBudgetAsync(rows, context.UserId, updateExisting);

            var msg = updateExisting
                ? $"{inserted} budget row{(inserted == 1 ? "" : "s")} inserted, " +
                  $"{updated} row{(updated == 1 ? "" : "s")} updated."
                : $"{inserted} budget row{(inserted == 1 ? "" : "s")} imported successfully.";

            Log.Information("Budget import succeeded. {Message}", msg);
            return ImportResult.Succeeded(msg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Budget upload failed");
            return ImportResult.Failed("Import failed while writing to the database.", ex.Message);
        }
    }

    private static ImportError Err(MRIBudget row, string message) =>
        new() { LineNumber = row.LineNumber, Message = message };
}
