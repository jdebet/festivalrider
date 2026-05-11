using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FestivalRider;
using FestivalRider.BundleMigrators;
using FestivalRider.Migrators;
using FestivalRider.PrintStrategies;
using FestivalRider.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<ILocalizationService, LocalizationService>();
builder.Services.AddScoped<IBandService, BandService>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IBundleService, BundleService>();
builder.Services.AddScoped<IPdfExportService, PdfExportService>();

builder.Services.AddScoped<IStateMigrator, V1ToV2Migrator>();
builder.Services.AddScoped<IStateMigrator, V2ToV3Migrator>();

builder.Services.AddScoped<IBundleMigrator, V2ToV3BundleMigrator>();

builder.Services.AddScoped<IPrintStrategy, BandRiderPrintStrategy>();
builder.Services.AddScoped<IPrintStrategy, StagePrintStrategy>();
builder.Services.AddScoped<IPrintStrategy, RolePrintStrategy>();

await builder.Build().RunAsync();