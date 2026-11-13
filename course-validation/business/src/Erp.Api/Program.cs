extern alias ErpWeb;

using System.Diagnostics;
using System.Text.Json.Serialization;
using Erp.Api;
using Erp.Api.Security;
using Erp.Modules.Identity;
using Erp.Modules.Inventory;
using Erp.Modules.Invoicing;
using Erp.Modules.Purchasing;
using Erp.Modules.Sales;
using Erp.Shared.BuildingBlocks;
using Erp.Shared.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

builder.AddObservability();
builder.Services.AddSharedInfrastructure(builder.Configuration);

// Registration order = migration and seed order.
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddSalesModule();
builder.Services.AddInventoryModule();
builder.Services.AddPurchasingModule();
builder.Services.AddInvoicingModule(builder.Configuration);

builder.Services.AddAuthorization();
// The browser app: the server renders its page, the screens run in the browser as WebAssembly.
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddErpRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
// Enums travel as their names ("Cash"), not numbers, in both directions.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Every problem body, ours or the framework's, carries an error code and the trace id that the X-Trace-Id header and
// the logs carry.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    if (!c.ProblemDetails.Extensions.ContainsKey("code"))
        ErrorProblems.Apply(c.ProblemDetails, ErrorProblems.ForStatus(c.ProblemDetails.Status ?? c.HttpContext.Response.StatusCode));
    c.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? c.HttpContext.TraceIdentifier;
});
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (builder.Configuration.GetValue<bool>("Cors:AllowAnyOrigin"))   // development only
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

// The API's description, generated from the endpoints themselves: /openapi/v1.json, browsed at /scalar (development).
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecurityScheme>());

var app = builder.Build();

app.UseUtf8ProblemBodies();
app.UseExceptionHandler();
app.UseStatusCodePages();   // an error with no body (a bare 404, 401, 403) gets a problem+json body too
// An address that is no page of the app shows the app's own "not found" page (with a 404), not a JSON problem.
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api"),
    pages => pages.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseTraceIdHeader();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

await app.InitializeDatabaseAsync();

// Explicit, and after the status-code pages: a re-executed request ("/not-found") must be routed again.
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();   // after authentication, so limits are per user where possible
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });   // the process is up
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
// The browser app is served from the same host, so the API gets a prefix of its own.
// Every request with a body or query record is validated here, once, before any module's endpoint runs.
var api = app.MapGroup("/api").ValidateRequests();
api.MapErrorCatalog();
api.MapIdentityModule();
api.MapSalesEndpoints();
api.MapInventoryEndpoints();
api.MapPurchasingEndpoints();
api.MapInvoicingEndpoints();
api.MapFallback(() => Results.NotFound());   // an unknown API address is a 404, never the app's page

app.UseAntiforgery();
app.MapStaticAssets();
// The pages guard themselves in the browser (AuthorizeRouteView sends a signed-out user to sign in). The server
// only serves the page; every piece of data still comes from the API, which checks the user on each call.
app.MapRazorComponents<ErpWeb::Erp.Web.App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AllowAnonymous();

app.Run();

public partial class Program { }
