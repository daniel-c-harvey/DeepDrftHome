using DeepDrftWeb.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

Console.WriteLine(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped<HttpClient>(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

Startup.ConfigureDomainServices(builder.Services);

await builder.Build().RunAsync();
