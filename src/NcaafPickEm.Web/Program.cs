using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NcaafPickEm.Web;
using NcaafPickEm.Web.Auth;

using NcaafPickEm.Web.Services;

// Composition root for the Blazor client. Keep it to root components plus one registration
// call: client services belong in Services/DependencyInjection.cs (shared hot spot).
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddWebServices(new Uri(builder.HostEnvironment.BaseAddress));

// Auth state comes from GET /api/me; the session cookie itself is HttpOnly and invisible here.
// AddCascadingAuthenticationState avoids wrapping App.razor in <CascadingAuthenticationState>.
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ApiAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<ApiAuthenticationStateProvider>());

await builder.Build().RunAsync();
