using MRIImport.Core.DAL;
using MRIImport.Core.Interfaces;
using MRIImport.Core.Models;
using MRIImport.Core.Services;
using Serilog;

namespace MRIImport.Core.Handlers;

/// <summary>
/// Handles import of GL journal entries into the MRI JOURNAL table.
/// Validates: entity, GL account, department, job code, basis,
///            period open/closed/max, entry date, amount, status, REF,
///            and debit/credit balance per REF group.
/// </summary>
public class JournalHandler : IImportHandler
{
    private readonly ImportFileReader _reader;

    public JournalHandler(ImportFileReader reader)
    {
        _reader = reader;
    }

    public string ImportName => "Import Journal Entries";
    public string? TemplateFileName => "JOURNAL_UPLOAD_TEMPLATE.xlsx";

    public IEnumerable<string> InfoText =>
    [
        "Upload an XLSX or CSV file to import General Ledger journal entries into MRI.",
        "The file must contain a worksheet named 'IMPORT' (XLSX) or be a standard CSV.",
        "The first row must be a header row with column names matching the JOURNAL table fields.",
        "Required fields: PERIOD, ENTITYID, ACCTNUM, DEPARTMENT, BASIS, AMT, ENTRDATE, STATUS, REF, SOURCE.",
        "PERIOD format: YYYYMM (e.g. 202401 for January 2024).",
        "STATUS must be 'P' (posted) or 'U' (unposted).",
        "All entries with the same REF must balance to zero (debits = credits).",
        "The import will be rejected if any validation error is found."
    ];

    public async Task<ImportResult> ExecuteAsync(ImportContext context)
    {
        Log.Information("JournalHandler.ExecuteAsync started for {File}", context.OriginalFileName);

        // ── 1. Parse file ────────────────────────────────────────────────────
        List<MRIJournal> rows;
        try
        {
            rows = _reader.Read<MRIJournal>(context.FilePath);
        }
        catch (ImportFileException ex)
        {
            Log.Error(ex, "File parse failure");
            return ImportResult.Failed(ex.Message);
        }

        if (rows.Count == 0)
            return ImportResult.Failed("The import file contained no data rows.");

        // ── 2. Normalise fields ──────────────────────────────────────────────
        foreach (var r in rows)
        {
            r.ENTITYID  = (r.ENTITYID  ?? string.Empty).PadLeft(5, '0').ToUpper();
            r.ACCTNUM   = (r.ACCTNUM   ?? string.Empty).ToUpper();
            r.JOBCODE   = string.IsNullOrWhiteSpace(r.JOBCODE) ? r.JOBCODE : r.JOBCODE.ToUpper();
            r.BASIS     = (r.BASIS     ?? string.Empty).ToUpper();
            r.SOURCE    = (r.SOURCE    ?? string.Empty).ToUpper();
            r.REF       = (r.REF       ?? string.Empty).ToUpper();
        }

        // ── 3. Load reference data ────────────────────────────────────────────
        var db = new MriDb(context.ConnectionString);

        List<MRIEntity>    entities;
        List<MRIGLAccount> accounts;
        List<MRIDepartment> departments;
        List<MRIJob>       jobs;
        List<MRIBasis>     basisList;

        try
        {
            entities    = await db.GetEntityListAsync();
            accounts    = await db.GetAccountListAsync();
            departments = await db.GetDepartmentListAsync();
            jobs        = await db.GetJobListAsync();
            basisList   = await db.GetBasisListAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load reference data");
            return ImportResult.Failed("Could not load reference data from the database.", ex.Message);
        }

        // ── 4. Validate ───────────────────────────────────────────────────────
        var errors = new List<ImportError>();

        foreach (var j in rows)
        {
            // GL Account
            if (!accounts.Any(a => a.ACCTNUM == j.ACCTNUM))
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"Account number '{j.ACCTNUM}' is invalid or inactive." });

            // Job code required check
            if (accounts.Any(a => a.ACCTNUM == j.ACCTNUM && a.JCREQ == "Y") && j.JOBCODE.IsNullOrEmpty())
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"A job code is required for account '{j.ACCTNUM}'." });

            // Entity
            if (!entities.Any(e => e.ENTITYID == j.ENTITYID))
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"Entity '{j.ENTITYID}' is invalid." });

            // Job code validity (if provided)
            if (!j.JOBCODE.IsNullOrEmpty() && !jobs.Any(jb => jb.JOBCODE == j.JOBCODE))
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"Job code '{j.JOBCODE}' is invalid or inactive." });

            // Department
            if (!departments.Any(d => d.DEPARTMENT == j.DEPARTMENT))
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"Department '{j.DEPARTMENT}' is invalid." });

            // Basis
            if (!basisList.Any(b => b.BASIS == j.BASIS))
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"Basis '{j.BASIS}' is invalid." });

            // Amount
            if (j.AMT.IsNullOrEmpty() || !j.AMT.IsNumeric())
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = "Amount is missing or not a valid number." });

            // Period format YYYYMM
            var periodDate = j.PERIOD?.Right(2) + "/01/" + j.PERIOD?.Left(4);
            if (j.PERIOD.IsNullOrEmpty() || !periodDate.IsDate())
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"Period '{j.PERIOD}' is invalid (expected YYYYMM)." });

            // Entry date
            if (!j.ENTRDATE.IsDate())
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"Entry date '{j.ENTRDATE}' is not a valid date." });

            // Status
            if (j.STATUS != "P" && j.STATUS != "U")
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = $"Status '{j.STATUS}' is invalid. Must be 'P' or 'U'." });

            // REF required
            if (j.REF.IsNullOrEmpty())
                errors.Add(new ImportError { LineNumber = j.LineNumber, Message = "REF is required." });

            // Period closed / max period checks (only if entity and period are valid)
            var entity = entities.FirstOrDefault(e => e.ENTITYID == j.ENTITYID);
            if (entity is not null && !j.PERIOD.IsNullOrEmpty() && periodDate.IsDate())
            {
                if (int.TryParse(entity.CURPED, out var curPed) &&
                    int.TryParse(j.PERIOD, out var jPeriod) && curPed > jPeriod)
                {
                    errors.Add(new ImportError
                    {
                        LineNumber = j.LineNumber,
                        Message = $"Period {j.PERIOD!.Right(2)}/{j.PERIOD.Left(4)} is closed."
                    });
                }

                if (int.TryParse(entity.MAXPD, out var maxPd) &&
                    int.TryParse(j.PERIOD, out jPeriod) && maxPd < jPeriod)
                {
                    errors.Add(new ImportError
                    {
                        LineNumber = j.LineNumber,
                        Message = $"Period {j.PERIOD!.Right(2)}/{j.PERIOD.Left(4)} is after the maximum open period."
                    });
                }

                // Must not be more than 4 months beyond current period
                if (int.TryParse(entity.CURPED, out curPed))
                {
                    var curPedStr = curPed.ToString();
                    if (DateTime.TryParse(curPedStr.Right(2) + "-01-" + curPedStr.Left(4), out var curDate))
                    {
                        var maxAllowed = int.Parse(curDate.AddMonths(4).ToString("yyyyMM"));
                        if (int.TryParse(j.PERIOD, out jPeriod) && jPeriod > maxAllowed)
                            errors.Add(new ImportError
                            {
                                LineNumber = j.LineNumber,
                                Message = "Posting period is more than 4 months from the current period."
                            });
                    }
                }
            }
        }

        // Balance check per REF
        foreach (var refGroup in rows.GroupBy(r => r.REF))
        {
            var total = refGroup
                .Where(r => r.AMT.IsNumeric())
                .Sum(r => Math.Round(decimal.Parse(r.AMT!), 2));

            if (Math.Round(total, 2) != 0)
                errors.Add(new ImportError
                {
                    LineNumber = 0,
                    Message = $"REF '{refGroup.Key}' is out of balance by {total:C}."
                });
        }

        if (errors.Count > 0)
        {
            Log.Warning("Journal validation failed with {Count} errors.", errors.Count);
            return ImportResult.ValidationFailed("File not imported due to validation errors.", errors);
        }

        // ── 5. Upload ─────────────────────────────────────────────────────────
        try
        {
            var (rowCount, entryCount, firstRef) =
                await db.UploadJournalAsync(rows, context.UserId, keepGlRefs: false);

            var msg = $"{rowCount} rows imported into {entryCount} journal {(entryCount == 1 ? "entry" : "entries")}. " +
                      $"The {(entryCount == 1 ? "" : "first ")}reference number is {firstRef}.";

            Log.Information("Journal import succeeded. {Message}", msg);
            return ImportResult.Succeeded(msg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Journal upload failed");
            return ImportResult.Failed("Import failed while writing to the database.", ex.Message);
        }
    }
}
