using MRIImport.Core.DAL;
using MRIImport.Core.Interfaces;
using MRIImport.Core.Models;
using MRIImport.Core.Services;
using Serilog;

namespace MRIImport.Core.Handlers;

/// <summary>
/// Handles import of recurring charges into the MRI CMRECC table.
///
/// Beyond a simple INSERT this handler also:
///   - Validates all FK constraints with clear English error messages
///   - Validates EFFDATE >= BLDG.BILLDATE
///   - Detects duplicate PK combinations within the import file itself
///   - Before inserting each new row, ends any currently-active CMRECC charge
///     for the same BLDGID + LEASID + INCCAT by setting its ENDDATE to EFFDATE - 1
///   - All UPDATEs and INSERTs run in a single transaction
/// </summary>
public class CmReccHandler : IImportHandler
{
    private readonly ImportFileReader _reader;

    public CmReccHandler(ImportFileReader reader)
    {
        _reader = reader;
    }

    public string ImportName => "Import Recurring Charges";
    public string? TemplateFileName => "CMRECC_UPLOAD_TEMPLATE.xlsx";

    public IEnumerable<string> InfoText =>
    [
        "Upload an XLSX or CSV file to import recurring charges into MRI.",
        "The file must contain a worksheet named 'IMPORT' (XLSX) or be a standard CSV.",
        "Required fields: BLDGID, LEASID, INCCAT, EFFDATE, FRQUENCY, MFEXEMPT, CHARGEDAY, ADVANCE, INEFFECT, PROPOSED.",
        "EFFDATE must be on or after the building's current billing date.",
        "If an active recurring charge already exists for the same building, lease, and income category, " +
            "its end date will automatically be set to one day before the new effective date.",
        "All changes are applied in a single transaction — if any row fails, nothing is saved."
    ];

    public async Task<ImportResult> ExecuteAsync(ImportContext context)
    {
        Log.Information("CmReccHandler.ExecuteAsync started for {File}", context.OriginalFileName);

        // ── 1. Parse file ────────────────────────────────────────────────────
        List<MRICmRecc> rows;
        try
        {
            rows = _reader.Read<MRICmRecc>(context.FilePath);
        }
        catch (ImportFileException ex)
        {
            return ImportResult.Failed(ex.Message);
        }

        if (rows.Count == 0)
            return ImportResult.Failed("The import file contained no data rows.");

        // ── 2. Normalise fields ──────────────────────────────────────────────
        foreach (var r in rows)
        {
            r.BLDGID     = (r.BLDGID     ?? string.Empty).Trim().ToUpper();
            r.LEASID     = (r.LEASID     ?? string.Empty).Trim().ToUpper();
            r.INCCAT     = (r.INCCAT     ?? string.Empty).Trim().ToUpper();
            r.FRQUENCY   = (r.FRQUENCY   ?? string.Empty).Trim().ToUpper();
            r.MFEXEMPT   = (r.MFEXEMPT   ?? string.Empty).Trim().ToUpper();
            r.CHARGEDAY  = (r.CHARGEDAY  ?? string.Empty).Trim().ToUpper();
            r.ADVANCE    = (r.ADVANCE    ?? string.Empty).Trim().ToUpper();
            r.INEFFECT   = (r.INEFFECT   ?? string.Empty).Trim().ToUpper();
            r.PROPOSED   = (r.PROPOSED   ?? string.Empty).Trim().ToUpper();
            r.DEPARTMENT = string.IsNullOrWhiteSpace(r.DEPARTMENT) ? null : r.DEPARTMENT.Trim().ToUpper();
            r.RTAXGRPID  = string.IsNullOrWhiteSpace(r.RTAXGRPID)  ? null : r.RTAXGRPID.Trim().ToUpper();
            r.SQFTTYPE   = string.IsNullOrWhiteSpace(r.SQFTTYPE)   ? null : r.SQFTTYPE.Trim().ToUpper();
        }

        // ── 3. Load reference data ────────────────────────────────────────────
        var db = new MriDb(context.ConnectionString);

        List<MRIBuildingWithBillDate> buildings;
        List<MRILease>               leases;
        List<MRIIncomeCategory>      incomeCategories;
        List<MRIDepartment>          departments;
        List<MRIRealTaxGroup>        realTaxGroups;
        List<MRISqftType>            sqftTypes;

        try
        {
            buildings        = await db.GetBuildingWithBillDateListAsync();
            leases           = await db.GetLeaseListAsync();
            incomeCategories = await db.GetIncomeCategoryListAsync();
            departments      = await db.GetDepartmentListAsync();
            realTaxGroups    = await db.GetRealTaxGroupListAsync();
            sqftTypes        = await db.GetSqftTypeListAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load reference data");
            return ImportResult.Failed("Could not load reference data from the database.", ex.Message);
        }

        // ── 4. Validate ───────────────────────────────────────────────────────
        var errors  = new List<ImportError>();
        var pkSeen  = new HashSet<string>(); // duplicate PK detection within the file

        foreach (var r in rows)
        {
            // ── Required fields ───────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(r.BLDGID))
                errors.Add(Err(r, "Building ID (BLDGID) is required."));

            if (string.IsNullOrWhiteSpace(r.LEASID))
                errors.Add(Err(r, "Lease ID (LEASID) is required."));

            if (string.IsNullOrWhiteSpace(r.INCCAT))
                errors.Add(Err(r, "Income category (INCCAT) is required."));

            if (string.IsNullOrWhiteSpace(r.EFFDATE))
                errors.Add(Err(r, "Effective date (EFFDATE) is required."));

            if (string.IsNullOrWhiteSpace(r.FRQUENCY))
                errors.Add(Err(r, "Frequency (FRQUENCY) is required."));

            if (string.IsNullOrWhiteSpace(r.MFEXEMPT))
                errors.Add(Err(r, "Management fee exempt flag (MFEXEMPT) is required."));

            if (string.IsNullOrWhiteSpace(r.CHARGEDAY))
                errors.Add(Err(r, "Charge day (CHARGEDAY) is required."));

            if (string.IsNullOrWhiteSpace(r.ADVANCE))
                errors.Add(Err(r, "Advance flag (ADVANCE) is required."));

            if (string.IsNullOrWhiteSpace(r.INEFFECT))
                errors.Add(Err(r, "In effect flag (INEFFECT) is required."));

            if (string.IsNullOrWhiteSpace(r.PROPOSED))
                errors.Add(Err(r, "Proposed flag (PROPOSED) is required."));

            // ── EFFDATE must be a valid date ───────────────────────────────────
            DateTime? effDate = null;
            if (!string.IsNullOrWhiteSpace(r.EFFDATE))
            {
                if (!DateTime.TryParse(r.EFFDATE, out var parsedEff))
                    errors.Add(Err(r, $"Effective date '{r.EFFDATE}' is not a valid date."));
                else
                    effDate = parsedEff;
            }

            // ── FK: Building (BLDG) ───────────────────────────────────────────
            var building = buildings.FirstOrDefault(b => b.BLDGID == r.BLDGID);
            if (!string.IsNullOrWhiteSpace(r.BLDGID) && building is null)
                errors.Add(Err(r, $"Building '{r.BLDGID}' does not exist."));

            // ── Business rule: EFFDATE >= BLDG.BILLDATE ───────────────────────
            if (effDate.HasValue && building is not null && building.BILLDATE.HasValue)
            {
                if (effDate.Value.Date < building.BILLDATE.Value.Date)
                    errors.Add(Err(r,
                        $"Effective date cannot be before {building.BILLDATE.Value:MM/dd/yyyy} " +
                        $"(building '{r.BLDGID}' billing date)."));
            }

            // ── FK: Lease (LEAS — composite BLDGID + LEASID) ─────────────────
            if (!string.IsNullOrWhiteSpace(r.BLDGID) && !string.IsNullOrWhiteSpace(r.LEASID))
            {
                if (!leases.Any(l => l.BLDGID == r.BLDGID && l.LEASID == r.LEASID))
                    errors.Add(Err(r, $"Lease '{r.LEASID}' does not exist for building '{r.BLDGID}'."));
            }

            // ── FK: Income category (INCH) ────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.INCCAT) &&
                !incomeCategories.Any(i => i.INCCAT == r.INCCAT))
                errors.Add(Err(r, $"Income category '{r.INCCAT}' does not exist."));

            // ── FK: Department (GDEP) — optional ─────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.DEPARTMENT) &&
                !departments.Any(d => d.DEPARTMENT == r.DEPARTMENT))
                errors.Add(Err(r, $"Department '{r.DEPARTMENT}' does not exist."));

            // ── FK: Real tax group (RTAXGRP) — optional ───────────────────────
            if (!string.IsNullOrWhiteSpace(r.RTAXGRPID) &&
                !realTaxGroups.Any(rg => rg.RTAXGRPID == r.RTAXGRPID))
                errors.Add(Err(r, $"Real estate tax group '{r.RTAXGRPID}' does not exist."));

            // ── FK: Square foot type (SQTY) — optional ────────────────────────
            if (!string.IsNullOrWhiteSpace(r.SQFTTYPE) &&
                !sqftTypes.Any(s => s.SQFTTYPE == r.SQFTTYPE))
                errors.Add(Err(r, $"Square foot type '{r.SQFTTYPE}' does not exist."));

            // ── PK duplicate check within the file ────────────────────────────
            if (!string.IsNullOrWhiteSpace(r.BLDGID) &&
                !string.IsNullOrWhiteSpace(r.LEASID) &&
                !string.IsNullOrWhiteSpace(r.INCCAT) &&
                effDate.HasValue)
            {
                var pk = $"{r.BLDGID}|{r.LEASID}|{r.INCCAT}|{effDate.Value:yyyy-MM-dd}";
                if (!pkSeen.Add(pk))
                    errors.Add(Err(r,
                        $"Duplicate row in file: building '{r.BLDGID}', lease '{r.LEASID}', " +
                        $"income category '{r.INCCAT}', effective date '{r.EFFDATE}' " +
                        $"appears more than once. Each combination must be unique."));
            }
        }

        if (errors.Count > 0)
        {
            Log.Warning("CMRECC validation failed with {Count} errors.", errors.Count);
            return ImportResult.ValidationFailed("File not imported due to validation errors.", errors);
        }

        // ── 5. Upload ─────────────────────────────────────────────────────────
        try
        {
            var (inserted, ended) = await db.UploadCmReccAsync(rows, context.UserId);

            var endedNote = ended > 0
                ? $" {ended} existing charge{(ended == 1 ? "" : "s")} were ended."
                : string.Empty;

            var msg = $"{inserted} recurring charge{(inserted == 1 ? "" : "s")} imported successfully.{endedNote}";
            Log.Information("CMRECC import succeeded. {Message}", msg);
            return ImportResult.Succeeded(msg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CMRECC upload failed");
            return ImportResult.Failed("Import failed while writing to the database.", ex.Message);
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static ImportError Err(MRICmRecc row, string message) =>
        new() { LineNumber = row.LineNumber, Message = message };
}
