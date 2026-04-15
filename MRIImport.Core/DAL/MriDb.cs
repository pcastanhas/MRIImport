using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using MRIImport.Core.Models;
using Serilog;

namespace MRIImport.Core.DAL;

/// <summary>
/// Data Access Layer for MRI database operations.
/// Uses Dapper with raw SQL exclusively. No ORM.
/// Each method opens/closes its own connection, keeping usage simple
/// and compatible with Blazor Server's async lifecycle.
/// </summary>
public class MriDb
{
    private readonly string _connectionString;

    public MriDb(string connectionString)
    {
        _connectionString = connectionString;
        Log.Debug("MriDb created with connection: {ConnStart}", connectionString[..Math.Min(50, connectionString.Length)]);
    }

    private SqlConnection NewConnection() => new(_connectionString);

    // ── Reference / Lookup queries ────────────────────────────────────────────

    public async Task<List<MRIEntity>> GetEntityListAsync()
    {
        Log.Debug("GetEntityListAsync");
        const string sql = @"
            SELECT RTRIM(ENTITYID) ENTITYID
                  ,RTRIM(NAME)     NAME
                  ,CURPED
                  ,dbo.udf_NewPeriod(curped, ISNULL(maxopen, 4)) MAXPD
            FROM   ENTITY (NOLOCK)
            ORDER  BY ENTITYID";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIEntity>(sql)).ToList();
    }

    public async Task<List<MRIGLAccount>> GetAccountListAsync()
    {
        Log.Debug("GetAccountListAsync");
        const string sql = @"
            SELECT UPPER(RTRIM(ACCTNUM)) ACCTNUM
                  ,RTRIM(ACCTNAME)       ACCTNAME
                  ,ISNULL(ACTIVE,'Y')    ACTIVE
                  ,ISNULL(JCREQ,'N')     JCREQ
            FROM   GACC (NOLOCK)
            WHERE  TYPE   IN ('B','C','I')
            AND    ACTIVE = 'Y'
            ORDER  BY ACCTNUM";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIGLAccount>(sql)).ToList();
    }

    public async Task<List<MRIDepartment>> GetDepartmentListAsync()
    {
        Log.Debug("GetDepartmentListAsync");
        const string sql = @"
            SELECT RTRIM(DEPARTMENT) DEPARTMENT
                  ,RTRIM(DESCRPN)    DESCRPN
            FROM   GDEP
            ORDER  BY DEPARTMENT";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIDepartment>(sql)).ToList();
    }

    public async Task<List<MRIBudgetType>> GetBudgetTypeListAsync()
    {
        Log.Debug("GetBudgetTypeListAsync");
        const string sql = @"
            SELECT RTRIM(BUDTYPE)   BUDTYPE
                  ,RTRIM(DESCRPTN) DESCRPTN
            FROM   GBTY (NOLOCK)
            ORDER  BY BUDTYPE";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIBudgetType>(sql)).ToList();
    }

    public async Task<List<MRIBasis>> GetBasisListAsync()
    {
        Log.Debug("GetBasisListAsync");
        const string sql = @"
            SELECT RTRIM(BASIS)    BASIS
                  ,RTRIM(DESCRPTN) DESCRPTN
            FROM   BTYP (NOLOCK)
            ORDER  BY BASIS";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIBasis>(sql)).ToList();
    }

    public async Task<List<MRIJob>> GetJobListAsync()
    {
        Log.Debug("GetJobListAsync");
        const string sql = @"
            SELECT RTRIM(JOBCODE)      JOBCODE
                  ,RTRIM(DESCRPTN)     DESCRPTN
                  ,ISNULL(JACTIVE,'N') JACTIVE
            FROM   GJOB (NOLOCK)
            WHERE  JACTIVE = 'Y'
            ORDER  BY JOBCODE";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIJob>(sql)).ToList();
    }

    public async Task<List<MRIBuilding>> GetBuildingListAsync()
    {
        Log.Debug("GetBuildingListAsync");
        const string sql = @"
            SELECT RTRIM(BLDGID)   BLDGID
                  ,RTRIM(BLDGNAME) BLDGNAME
            FROM   BLDG (NOLOCK)
            ORDER  BY BLDGID";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIBuilding>(sql)).ToList();
    }

    public async Task<List<MRIIncomeCategory>> GetIncomeCategoryListAsync()
    {
        Log.Debug("GetIncomeCategoryListAsync");
        const string sql = @"
            SELECT RTRIM(INCCAT)   INCCAT
                  ,RTRIM(DESCRPTN) DESCRPTN
            FROM   INCH (NOLOCK)
            ORDER  BY INCCAT";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIIncomeCategory>(sql)).ToList();
    }

    public async Task<List<MRILease>> GetLeaseListAsync()
    {
        Log.Debug("GetLeaseListAsync");
        const string sql = @"
            SELECT RTRIM(BLDGID)   BLDGID
                  ,RTRIM(LEASID)   LEASID
                  ,RTRIM(OCCPNAME) OCCPNAME
                  ,RTRIM(MOCCPID)  MOCCPID
                  ,RTRIM(OCCPSTAT) OCCPSTAT
            FROM   LEAS (NOLOCK)
            ORDER  BY BLDGID, LEASID";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRILease>(sql)).ToList();
    }

    // ── Sequence helpers ──────────────────────────────────────────────────────

    public async Task<int> GetLastUsedSeqAsync(SqlConnection cn, SqlTransaction tx, string seqId)
    {
        return await cn.QueryFirstOrDefaultAsync<int>(
            "SELECT lastused FROM mriseq WHERE seqid = @SeqId",
            new { SeqId = seqId },
            tx);
    }

    public async Task<int> GetFieldLengthAsync(SqlConnection cn, SqlTransaction tx, string tableName, string fieldName)
    {
        return await cn.QueryFirstOrDefaultAsync<int>(
            "SELECT FLDLENGTH FROM MRIField WHERE tablename = @TableName AND FIELDNAME = @FieldName",
            new { TableName = tableName, FieldName = fieldName },
            tx);
    }

    // ── Journal upload ────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts journal entries in a single transaction.
    /// Assigns new sequential REF numbers from MRISEQ unless keepGlRefs is true.
    /// Returns the number of entries created and the first REF number.
    /// </summary>
    public async Task<(int rowCount, int entryCount, string firstRef)> UploadJournalAsync(
        List<MRIJournal> data, string userId, bool keepGlRefs)
    {
        Log.Information("UploadJournalAsync: {Count} rows, keepGlRefs={Keep}", data.Count, keepGlRefs);

        await using var cn = NewConnection();
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync() as SqlTransaction
            ?? throw new InvalidOperationException("Could not begin transaction.");

        try
        {
            int mriseq = await GetLastUsedSeqAsync(cn, tx, "JOURNAL");
            int refLen = await GetFieldLengthAsync(cn, tx, "journal", "REF");

            // Assign REF numbers per unique REF group
            var distinctRefs = data.Select(r => r.REF).Distinct().ToList();
            string firstRef = keepGlRefs
                ? (distinctRefs.FirstOrDefault() ?? string.Empty)
                : (mriseq + 1).ToString().PadRight(refLen, '0');

            int entryCount = 0;
            foreach (var originalRef in distinctRefs)
            {
                mriseq++;
                entryCount++;
                foreach (var row in data.Where(r => r.REF == originalRef))
                {
                    if (!keepGlRefs)
                        row.REF = mriseq.ToString().PadRight(refLen, '0');
                    row.USERID = userId;
                    row.LASTDATE = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
            }

            // Build and execute INSERT for each row
            var insertSql = BuildInsertSql<MRIJournal>();
            foreach (var row in data)
            {
                Log.Debug("Inserting JOURNAL row {Line}", row.LineNumber);
                await cn.ExecuteAsync(insertSql, row, tx);
            }

            // Update MRISEQ
            if (!keepGlRefs)
                await cn.ExecuteAsync(
                    "UPDATE MRISEQ SET LASTUSED = @x WHERE SEQID = 'JOURNAL'",
                    new { x = mriseq }, tx);

            await tx.CommitAsync();
            Log.Information("Journal upload committed. {Rows} rows, {Entries} entries.", data.Count, entryCount);
            return (data.Count, entryCount, firstRef);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── Budget upload ─────────────────────────────────────────────────────────

    public async Task<int> UploadBudgetAsync(List<MRIBudget> data, string userId, bool updateIfExists)
    {
        Log.Information("UploadBudgetAsync: {Count} rows, update={Update}", data.Count, updateIfExists);

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var row in data)
        {
            row.USERID = userId;
            row.LASTDATE = now;
        }

        await using var cn = NewConnection();
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync() as SqlTransaction
            ?? throw new InvalidOperationException("Could not begin transaction.");

        try
        {
            if (updateIfExists)
            {
                const string deleteSql = @"
                    DELETE BUDGETS
                    WHERE  PERIOD     = @PERIOD
                    AND    ENTITYID   = @ENTITYID
                    AND    DEPARTMENT = @DEPARTMENT
                    AND    ACCTNUM    = @ACCTNUM
                    AND    BASIS      = @BASIS
                    AND    BUDTYPE    = @BUDTYPE";

                foreach (var row in data)
                    await cn.ExecuteAsync(deleteSql, row, tx);
            }

            var insertSql = BuildInsertSql<MRIBudget>();
            foreach (var row in data)
                await cn.ExecuteAsync(insertSql, row, tx);

            await tx.CommitAsync();
            Log.Information("Budget upload committed. {Rows} rows.", data.Count);
            return data.Count;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── CMMisc upload ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a CMBTCH header record then inserts CMMISC rows, all in one transaction.
    /// Returns the new batch ID.
    /// </summary>
    public async Task<string> UploadCmMiscAsync(
        List<MRICMMisc> data, string userId, string batchDate, string description)
    {
        Log.Information("UploadCmMiscAsync: {Count} rows, batchDate={Date}", data.Count, batchDate);

        await using var cn = NewConnection();
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync() as SqlTransaction
            ?? throw new InvalidOperationException("Could not begin transaction.");

        try
        {
            int mriseq = await GetLastUsedSeqAsync(cn, tx, "CMBATCH");
            int batchIdLen = await GetFieldLengthAsync(cn, tx, "CMBTCH", "CMBATCHID");

            string batchId = (mriseq + 1).ToString().PadRight(batchIdLen, '0');

            // Insert batch header
            const string headerSql = @"
                INSERT INTO CMBTCH (CMBATCHID, TYPE, STATUS, BATCHDATE, DESCRPTN, CONTENT, INPROGRESS, JOBCOST)
                VALUES (@P1, @P2, @P3, @P4, @P5, @P6, @P7, @P8)";

            await cn.ExecuteAsync(headerSql, new
            {
                P1 = batchId,
                P2 = "N",
                P3 = "O",
                P4 = batchDate,
                P5 = description,
                P6 = "N",
                P7 = "N",
                P8 = "N"
            }, tx);

            // Update MRISEQ
            await cn.ExecuteAsync(
                "UPDATE MRISEQ SET LASTUSED = @P1 WHERE SEQID = 'CMBATCH'",
                new { P1 = Convert.ToInt32(batchId) }, tx);

            // Insert detail rows
            const string detailSql = @"
                INSERT INTO CMMISC
                    (CMBATCHID, ITEM, BLDGID, LEASID, TRANDATE, INCCAT,
                     SRCCODE,   DESCRPTN, TRANAMT, DEPARTMENT, POSTORDER)
                VALUES
                    (@CMBATCHID, @ITEM, @BLDGID, @LEASID, @TRANDATE, @INCCAT,
                     @SRCCODE,  @DESCRPTN, @TRANAMT, '@', @ITEM)";

            foreach (var row in data)
            {
                await cn.ExecuteAsync(detailSql, new
                {
                    CMBATCHID  = batchId,
                    row.ITEM,
                    row.BLDGID,
                    row.LEASID,
                    row.TRANDATE,
                    row.INCCAT,
                    row.SRCCODE,
                    row.DESCRPTN,
                    row.TRANAMT
                }, tx);
            }

            await tx.CommitAsync();
            Log.Information("CMMisc upload committed. BatchId={BatchId}, {Rows} rows.", batchId, data.Count);
            return batchId;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // ── SQL builder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Dynamically builds a parameterised INSERT statement for any MRITableBase model.
    /// Only includes properties that are actual data columns (via GetDataProperties()).
    /// Parameters are named @PropertyName matching Dapper's convention.
    /// </summary>
    private static string BuildInsertSql<T>() where T : MRITableBase, new()
    {
        var prototype = new T();
        var props = prototype.GetDataProperties().ToList();

        var columns = string.Join(", ", props.Select(p => p.Name));
        var parameters = string.Join(", ", props.Select(p => "@" + p.Name));

        return $"INSERT INTO {prototype.TableName} ({columns}) VALUES ({parameters})";
    }

    // ── CMRECC reference lookups ──────────────────────────────────────────────

    public async Task<List<MRIBuildingWithBillDate>> GetBuildingWithBillDateListAsync()
    {
        Log.Debug("GetBuildingWithBillDateListAsync");
        const string sql = @"
            SELECT RTRIM(BLDGID)   BLDGID
                  ,RTRIM(BLDGNAME) BLDGNAME
                  ,BILLDATE
            FROM   BLDG (NOLOCK)
            ORDER  BY BLDGID";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIBuildingWithBillDate>(sql)).ToList();
    }

    public async Task<List<MRIRealTaxGroup>> GetRealTaxGroupListAsync()
    {
        Log.Debug("GetRealTaxGroupListAsync");
        const string sql = @"
            SELECT RTRIM(RTAXGRPID) RTAXGRPID
                  ,RTRIM(DESCRPTN)  DESCRPTN
            FROM   RTAXGRP (NOLOCK)
            ORDER  BY RTAXGRPID";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRIRealTaxGroup>(sql)).ToList();
    }

    public async Task<List<MRISqftType>> GetSqftTypeListAsync()
    {
        Log.Debug("GetSqftTypeListAsync");
        const string sql = @"
            SELECT RTRIM(SQFTTYPE) SQFTTYPE
                  ,RTRIM(DESCRPTN) DESCRPTN
            FROM   SQTY (NOLOCK)
            ORDER  BY SQFTTYPE";

        await using var cn = NewConnection();
        return (await cn.QueryAsync<MRISqftType>(sql)).ToList();
    }

    // ── CMRECC upload ─────────────────────────────────────────────────────────

    /// <summary>
    /// For each imported row:
    ///   1. UPDATE any currently-active CMRECC charges for the same
    ///      BLDGID + LEASID + INCCAT to ENDDATE = EFFDATE - 1 day
    ///   2. INSERT the new CMRECC row
    /// All rows processed in a single transaction — fully rolls back on any error.
    /// Returns the count of rows inserted and the count of existing rows ended.
    /// </summary>
    public async Task<(int inserted, int ended)> UploadCmReccAsync(
        List<MRICmRecc> data, string userId)
    {
        Log.Information("UploadCmReccAsync: {Count} rows", data.Count);

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var row in data)
        {
            row.USERID   = userId;
            row.LASTDATE = now;
        }

        await using var cn = NewConnection();
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync() as SqlTransaction
            ?? throw new InvalidOperationException("Could not begin transaction.");

        try
        {
            int ended = 0;

            // End any currently-active charges that overlap with each incoming row
            const string endSql = @"
                UPDATE CMRECC
                SET    ENDDATE  = DATEADD(day, -1, @EFFDATE)
                      ,LASTDATE = @LASTDATE
                      ,USERID   = @USERID
                WHERE  BLDGID  = @BLDGID
                AND    LEASID  = @LEASID
                AND    INCCAT  = @INCCAT
                AND    EFFDATE <= @EFFDATE
                AND    (ENDDATE IS NULL OR ENDDATE >= @EFFDATE)";

            foreach (var row in data)
            {
                var affected = await cn.ExecuteAsync(endSql, new
                {
                    row.EFFDATE,
                    row.BLDGID,
                    row.LEASID,
                    row.INCCAT,
                    LASTDATE = now,
                    USERID   = userId
                }, tx);

                ended += affected;
                Log.Debug("Ended {Count} overlapping CMRECC rows for {BLDGID}/{LEASID}/{INCCAT}",
                    affected, row.BLDGID, row.LEASID, row.INCCAT);
            }

            // INSERT the new rows
            var insertSql = BuildInsertSql<MRICmRecc>();
            foreach (var row in data)
            {
                Log.Debug("Inserting CMRECC row {Line}", row.LineNumber);
                await cn.ExecuteAsync(insertSql, row, tx);
            }

            await tx.CommitAsync();
            Log.Information("CMRECC upload committed. {Inserted} inserted, {Ended} existing rows ended.",
                data.Count, ended);
            return (data.Count, ended);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}

