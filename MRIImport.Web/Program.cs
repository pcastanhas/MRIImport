using MudBlazor.Services;
using MRIImport.Core.Handlers;
using MRIImport.Core.Services;
using Serilog;

// ── Serilog: configured from appsettings.json before the host is built ────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("MRIImport starting up");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog reads full config from appsettings.json
    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext());

    // ── Services ──────────────────────────────────────────────────────────────
    builder.Services.AddRazorPages();
    builder.Services.AddServerSideBlazor();
    builder.Services.AddMudServices();

    // Core services — registered as transient since they are stateless
    builder.Services.AddTransient<ImportFileReader>();
    builder.Services.AddTransient<FileService>();

    // Import handlers — each is transient; new imports just add a line here
    // and a matching .razor page. No other changes required.
    builder.Services.AddTransient<JournalHandler>();
    builder.Services.AddTransient<BudgetHandler>();
    builder.Services.AddTransient<CmMiscHandler>();
    builder.Services.AddTransient<CmReccHandler>();

    // ── Pipeline ──────────────────────────────────────────────────────────────
    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();

    app.MapBlazorHub();
    app.MapFallbackToPage("/_Host");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "MRIImport terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
