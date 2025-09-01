using DeepDrftWeb.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

Startup.ConfigureDomainServices(builder.Services, builder.HostEnvironment.BaseAddress);

await builder.Build().RunAsync();
