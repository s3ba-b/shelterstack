using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Localization;
using ShelterStack.Web.Auth;
using ShelterStack.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Shared Aspire wiring: OpenTelemetry, health checks, resilience, and service discovery.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddLocalization();

// Authenticated session (token + tenant/role/org claims) for the current circuit. The same
// scoped instance backs Blazor's AuthenticationStateProvider so AuthorizeView/AuthorizeRouteView
// react to sign-in and sign-out.
builder.Services.AddScoped<WebAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<WebAuthenticationStateProvider>()
);
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// All backend calls go through the gateway, never directly to a business service, via the
// GatewayClient typed client (which attaches the session's Bearer token). The base address is a
// logical service name resolved by Aspire service discovery (configured via AddServiceDefaults)
// to the gateway's real endpoint at runtime.
builder.Services.AddHttpClient<GatewayClient>(client =>
    client.BaseAddress = new Uri("https+http://gateway")
);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// English is the default; Polish is opt-in via the EN/PL toggle (stored in a cookie).
app.UseRequestLocalization(options =>
{
    options
        .AddSupportedCultures("en", "pl")
        .AddSupportedUICultures("en", "pl")
        .SetDefaultCulture("en");
    options.RequestCultureProviders.Insert(0, new CookieRequestCultureProvider());
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Aspire health/liveness endpoints (/health, /alive).
app.MapDefaultEndpoints();

app.Run();
