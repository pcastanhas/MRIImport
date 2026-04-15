# MRIImport

A .NET 8 Blazor Server application for importing data files (XLSX / CSV) into MRI Software's SQL database.  
Built with MudBlazor for the UI, Dapper for data access, and Serilog for logging.  
Designed to run embedded inside an MRI iframe — no navigation chrome, no authentication layer.

---

## Solution Structure

```
MRIImport.sln
├── MRIImport.Core          Class library — all business logic, UI-agnostic
│   ├── Interfaces/
│   │   ├── IMRITable.cs        Base interface for all import model classes
│   │   └── IImportHandler.cs   Extensibility contract for import types
│   ├── Models/
│   │   ├── MRITableBase.cs     Abstract base with reflection-based property mapping
│   │   ├── MRIModels.cs        All domain models (Journal, Budget, CMMisc, lookups)
│   │   └── ImportModels.cs     ImportResult, ImportContext, ImportError
│   ├── DAL/
│   │   └── MriDb.cs            Dapper DAL — all SQL queries and uploads
│   ├── Handlers/
│   │   ├── JournalHandler.cs   GL journal entry import
│   │   ├── BudgetHandler.cs    Budget import
│   │   └── CmMiscHandler.cs    Tenant charges (CMMisc) import
│   └── Services/
│       ├── ImportFileReader.cs Shared XLSX/CSV → List<T> parser
│       ├── FileService.cs      Saves uploaded files to UNC share
│       └── StringExtensions.cs Native .NET replacements for CTSCore.dll helpers
│
└── MRIImport.Web           Blazor Server app — presentation only
    ├── Shared/
    │   ├── MainLayout.razor    Frameless MudBlazor layout (no nav, no app bar)
    │   ├── ImportPage.razor    Shared component: file picker, alerts, error table
    │   └── InfoDialog.razor    Help/info dialog triggered by the ⓘ icon
    └── Pages/
        ├── Journal.razor       /journal  — wires JournalHandler, no extra fields
        ├── Budget.razor        /budget   — adds "update if exists" checkbox
        └── CMMisc.razor        /cmmisc   — adds batch date + description fields
```

---

## How Pages Are Called from MRI

Each page is invoked as a direct URL with query string parameters:

```
https://yourserver/journal?db=BCP_PROD&UserId=jsmith
https://yourserver/budget?db=MRI_TEST&UserId=jsmith
https://yourserver/cmmisc?db=BCP_PROD&UserId=jsmith
```

- `db` — maps to a named connection string in `appsettings.json`
- `UserId` — the MRI user performing the import (stamped on inserted rows)

---

## Import File Requirements

- **XLSX**: must contain a worksheet named exactly `IMPORT`. Row 1 = column headers.
- **CSV**: standard comma-separated. Row 1 = column headers.
- Column names must match the model's property names (case-insensitive).
- Extra columns in the file are silently ignored.
- Completely empty rows are skipped.

---

## Adding a New Import Type

Only three files need to be created — no core infrastructure changes required:

1. **`MRIImport.Core/Models/MRIModels.cs`** — add a new model class inheriting `MRITableBase`
2. **`MRIImport.Core/Handlers/MyNewHandler.cs`** — implement `IImportHandler`
3. **`MRIImport.Web/Pages/MyNew.razor`** — thin page wiring the handler, with any extra fields in `<ChildContent>`
4. Register the handler in **`Program.cs`**: `builder.Services.AddTransient<MyNewHandler>();`

---

## Configuration (`appsettings.json`)

```json
{
  "ConnectionStrings": {
    "BCP_PROD": "Server=...;Database=BCP_PROD;...",
    "MRI_TEST": "Server=...;Database=MRI_TEST;...",
    "DEFAULT":  "Server=...;Database=MRI_TEST;..."
  },
  "ApplicationSettings": {
    "FileSavePath": "\\\\server\\Uploads\\"
  }
}
```

For local development, override in `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DEFAULT": "Server=localhost;Database=MRI_TEST;..."
  },
  "ApplicationSettings": {
    "FileSavePath": "C:\\Temp\\MRIImportUploads\\"
  }
}
```

---

## Local Development

Hit **F5** in Visual Studio. The launch URL is pre-configured to:
```
https://localhost:7180/journal?db=MRI_TEST&UserId=devuser
```

Change the route to test other pages: `/budget`, `/cmmisc`.

---

## IIS Deployment

1. Publish `MRIImport.Web` (`dotnet publish -c Release`)
2. Create an IIS application pointing to the publish output
3. Ensure the app pool uses **No Managed Code** (ASP.NET Core runs its own runtime)
4. Ensure the UNC path in `ApplicationSettings:FileSavePath` is accessible by the IIS app pool identity
5. The `web.config` in the publish output handles the ASP.NET Core Module configuration automatically

---

## Dependencies

| Package | Purpose |
|---|---|
| MudBlazor 7.x | UI component library |
| Dapper 2.x | Lightweight SQL data access |
| ClosedXML 0.102 | XLSX file reading |
| Microsoft.Data.SqlClient 5.x | SQL Server connectivity |
| Serilog + sinks | Structured logging to console and rolling file |
