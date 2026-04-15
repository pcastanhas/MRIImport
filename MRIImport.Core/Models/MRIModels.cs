namespace MRIImport.Core.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Reference / lookup models  (populated from DB, not from import files)
// ─────────────────────────────────────────────────────────────────────────────

public class MRIEntity
{
    public string ENTITYID { get; set; } = string.Empty;
    public string NAME { get; set; } = string.Empty;
    public string CURPED { get; set; } = string.Empty;
    public string MAXPD { get; set; } = string.Empty;
}

public class MRIGLAccount
{
    public string ACCTNUM { get; set; } = string.Empty;
    public string ACCTNAME { get; set; } = string.Empty;
    public string ACTIVE { get; set; } = "Y";
    public string JCREQ { get; set; } = "N";
}

public class MRIDepartment
{
    public string DEPARTMENT { get; set; } = string.Empty;
    public string DESCRPN { get; set; } = string.Empty;
}

public class MRIBudgetType
{
    public string BUDTYPE { get; set; } = string.Empty;
    public string DESCRPTN { get; set; } = string.Empty;
}

public class MRIBasis
{
    public string BASIS { get; set; } = string.Empty;
    public string DESCRPTN { get; set; } = string.Empty;
}

public class MRIJob
{
    public string JOBCODE { get; set; } = string.Empty;
    public string DESCRPTN { get; set; } = string.Empty;
    public string JACTIVE { get; set; } = "N";
}

public class MRIBuilding
{
    public string BLDGID { get; set; } = string.Empty;
    public string BLDGNAME { get; set; } = string.Empty;
}

public class MRIIncomeCategory
{
    public string INCCAT { get; set; } = string.Empty;
    public string DESCRPTN { get; set; } = string.Empty;
}

public class MRILease
{
    public string BLDGID { get; set; } = string.Empty;
    public string LEASID { get; set; } = string.Empty;
    public string OCCPNAME { get; set; } = string.Empty;
    public string MOCCPID { get; set; } = string.Empty;
    public string OCCPSTAT { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
// Import table models  (populated from import files)
// ─────────────────────────────────────────────────────────────────────────────

public class MRIJournal : MRITableBase
{
    public override string TableName => "JOURNAL";

    public string? PERIOD { get; set; }
    public string? REF { get; set; }
    public string? SOURCE { get; set; }
    public string? SITEID { get; set; }
    public string? ITEM { get; set; }
    public string? ENTITYID { get; set; }
    public string? ACCTNUM { get; set; }
    public string? DEPARTMENT { get; set; }
    public string? JOBCODE { get; set; }
    public string? AMT { get; set; }
    public string? DESCRPN { get; set; }
    public string? ENTRDATE { get; set; }
    public string? REVERSAL { get; set; }
    public string? STATUS { get; set; }
    public string? OEXCHGREF { get; set; }
    public string? BASIS { get; set; }
    public string? LASTDATE { get; set; }
    public string? USERID { get; set; }
    public string? OCURRCODE { get; set; }
    public string? OAMT { get; set; }
    public string? REVREF { get; set; }
    public string? INTENTENTRY { get; set; }
    public string? INTENTTYPE { get; set; }
    public string? CJEGRPID { get; set; }
    public string? CJEID { get; set; }
    public string? DESTITEM { get; set; }
    public string? AUDITFLAG { get; set; }
    public string? REVENTRY { get; set; }
    public string? REVPRD { get; set; }
    public string? REVITEM { get; set; }
    public string? OWNERTAX { get; set; }
    public string? OWNPCTCALC { get; set; }
    public string? JC_PHASECODE { get; set; }
    public string? JC_COSTLIST { get; set; }
    public string? JC_COSTCODE { get; set; }
    public string? CATEGORY { get; set; }
    public string? BRSTATUS { get; set; }
    public string? OWNTBL { get; set; }
    public string? OWNYEAR { get; set; }
    public string? OWNNUM { get; set; }
    public string? CTRYTBL { get; set; }
    public string? CTRYYEAR { get; set; }
    public string? CTRYNUM { get; set; }
    public string? BANKRECID { get; set; }
    public string? ADDLDESC { get; set; }
    public string? INTERFACEID { get; set; }
    public string? INTFMARKER { get; set; }
    public string? LOANSTAT { get; set; }
    public string? USER_DEFINED_ID1 { get; set; }
    public string? USER_DEFINED_VAL1 { get; set; }
    public string? USER_DEFINED_ID2 { get; set; }
    public string? USER_DEFINED_VAL2 { get; set; }
    public string? USER_DEFINED_ID3 { get; set; }
    public string? USER_DEFINED_VAL3 { get; set; }
    public string? USER_DEFINED_ID4 { get; set; }
    public string? USER_DEFINED_VAL4 { get; set; }
    public string? USER_DEFINED_ID5 { get; set; }
    public string? USER_DEFINED_VAL5 { get; set; }
    public string? USER_DEFINED_ID6 { get; set; }
    public string? USER_DEFINED_VAL6 { get; set; }
    public string? USER_DEFINED_ID7 { get; set; }
    public string? USER_DEFINED_VAL7 { get; set; }
    public string? USER_DEFINED_ID8 { get; set; }
    public string? USER_DEFINED_VAL8 { get; set; }
    public string? USER_DEFINED_ID9 { get; set; }
    public string? USER_DEFINED_VAL9 { get; set; }
    public string? USER_DEFINED_ID10 { get; set; }
    public string? USER_DEFINED_VAL10 { get; set; }
    public string? ASSETCLASS { get; set; }
    public string? ASSETCODE { get; set; }
    public string? ASSETSTATUS { get; set; }
    public string? SCDATE { get; set; }
    public string? SCTODATE { get; set; }
    public string? CASHGROUP { get; set; }
    public string? TAXGROUPID { get; set; }
    public string? RPTRUNID { get; set; }
    public string? FUNCURRAMT { get; set; }
    public string? USER_DEFINED_ID11 { get; set; }
    public string? USER_DEFINED_VAL11 { get; set; }
    public string? USER_DEFINED_ID12 { get; set; }
    public string? USER_DEFINED_VAL12 { get; set; }
    public string? USER_DEFINED_ID13 { get; set; }
    public string? USER_DEFINED_VAL13 { get; set; }
    public string? USER_DEFINED_ID14 { get; set; }
    public string? USER_DEFINED_VAL14 { get; set; }
    public string? USER_DEFINED_ID15 { get; set; }
    public string? USER_DEFINED_VAL15 { get; set; }
    public string? USER_DEFINED_ID16 { get; set; }
    public string? USER_DEFINED_VAL16 { get; set; }
    public string? USER_DEFINED_ID17 { get; set; }
    public string? USER_DEFINED_VAL17 { get; set; }
    public string? USER_DEFINED_ID18 { get; set; }
    public string? USER_DEFINED_VAL18 { get; set; }
    public string? USER_DEFINED_ID19 { get; set; }
    public string? USER_DEFINED_VAL19 { get; set; }
    public string? USER_DEFINED_ID20 { get; set; }
    public string? USER_DEFINED_VAL20 { get; set; }
}

public class MRIBudget : MRITableBase
{
    public override string TableName => "BUDGETS";

    public string? PERIOD { get; set; }
    public string? ENTITYID { get; set; }
    public string? DEPARTMENT { get; set; }
    public string? ACCTNUM { get; set; }
    public string? BASIS { get; set; }
    public string? BUDTYPE { get; set; }
    public string? ACTIVITY { get; set; }
    public string? LASTDATE { get; set; }
    public string? USERID { get; set; }
    public string? LOCKED { get; set; }
}

public class MRICMMisc : MRITableBase
{
    public override string TableName => "CMMISC";

    public string? CMBATCHI { get; set; }
    public string? ITEM { get; set; }
    public string? BLDGID { get; set; }
    public string? LEASID { get; set; }
    public string? TRANDATE { get; set; }
    public string? INCCAT { get; set; }
    public string? SRCCODE { get; set; }
    public string? DESCRPTN { get; set; }
    public string? TRANAMT { get; set; }
    public string? REFNMBR { get; set; }
}

// ── New lookup models for CMRECC import ──────────────────────────────────────

public class MRIBuildingWithBillDate
{
    public string BLDGID   { get; set; } = string.Empty;
    public string BLDGNAME { get; set; } = string.Empty;
    public DateTime? BILLDATE { get; set; }
}

public class MRIRealTaxGroup
{
    public string RTAXGRPID { get; set; } = string.Empty;
    public string DESCRPTN  { get; set; } = string.Empty;
}

public class MRISqftType
{
    public string SQFTTYPE  { get; set; } = string.Empty;
    public string DESCRPTN  { get; set; } = string.Empty;
}

// ── CMRECC import model ───────────────────────────────────────────────────────

public class MRICmRecc : MRITableBase
{
    public override string TableName => "CMRECC";

    // PK fields
    public string? BLDGID     { get; set; }
    public string? LEASID     { get; set; }
    public string? INCCAT     { get; set; }
    public string? EFFDATE    { get; set; }

    // Required NOT NULL fields
    public string? FRQUENCY   { get; set; }
    public string? MFEXEMPT   { get; set; }
    public string? CHARGEDAY  { get; set; }
    public string? ADVANCE    { get; set; }
    public string? INEFFECT   { get; set; }
    public string? PROPOSED   { get; set; }

    // Optional fields
    public string? AMOUNT     { get; set; }
    public string? BEGMONTH   { get; set; }
    public string? CATUSAGE   { get; set; }
    public string? LASTBILL   { get; set; }
    public string? ADDRID     { get; set; }
    public string? BYSQFT     { get; set; }
    public string? SQFTTYPE   { get; set; }
    public string? LASTDATE   { get; set; }
    public string? USERID     { get; set; }
    public string? ENDDATE    { get; set; }
    public string? MEMOICAT   { get; set; }
    public string? RTAXGRPID  { get; set; }
    public string? AUTOJE     { get; set; }
    public string? CURRCODE   { get; set; }
    public string? DEPARTMENT { get; set; }
    public string? ACHFLAG    { get; set; }
    public string? AUTOEXCEPTION  { get; set; }
    public string? REVIEWAGREED   { get; set; }
    public string? NOTE       { get; set; }
}
