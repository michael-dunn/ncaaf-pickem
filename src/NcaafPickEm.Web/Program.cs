using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NcaafPickEm.Web;
using NcaafPickEm.Web.Services;

// Composition root for the Blazor client. Keep it to root components plus one registration
// call: client services belong in Services/DependencyInjection.cs (shared hot spot).
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddWebServices(new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();
