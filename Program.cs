using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FestivalRider;
using FestivalRider.PrintStrategies;
using FestivalRider.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<IBandService, BandService>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<IStorageService, StorageService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IBundleService, BundleService>();
builder.Services.AddScoped<IPdfExportService, PdfExportService>();

builder.Services.AddScoped<IPrintStrategy, BandRiderPrintStrategy>();

await builder.Build().RunAsync();