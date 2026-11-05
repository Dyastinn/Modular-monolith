using System.Security.Claims;
using System.Text;
using Carter;
using Erp.Api;
using Erp.Api.Security;
using Erp.Modules.Identity;
using Erp.Modules.Inventory;
using Erp.Modules.Sales;
using Erp.Shared.BuildingBlocks;
using Erp.Shared.Infrastructure;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ── Platform ─────────────────────────────────────────────────────────────────────────────────────────────
builder.Services.AddSharedInfrastructure(builder.Configuration);   // connections, ICurrentUser, IClock

// The mediator, generated at compile time. Scoped, because handlers use the scoped module DbContexts.
// Behaviours run in this order: Logging OUTERMOST (so it sees validation failures too), then Validation.
builder.Services.AddMediator((MediatorOptions options) =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.Assemblies = [typeof(IdentityModule), typeof(SalesModule), typeof(InventoryModule)];
    options.PipelineBehaviors = [typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>)];
});

// ── Modules (registration order = migration and seed order) ─────────────────────────────────────────────
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddSalesModule(builder.Configuration);
builder.Services.AddInventoryModule(builder.Configuration);
builder.Services.AddCarter();   // Carter's own services; each module registered its (internal) endpoints itself

// ── Authentication: validate the JWT on every request ───────────────────────────────────────────────────
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;   // keep claim names as issued, so "sub" stays "sub"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,   ValidIssuer = jwt["Issuer"],
            ValidateAudience = true, ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ── API docs (development) ──────────────────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Erp API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        In = ParameterLocation.Header, Description = "Paste the accessToken from POST /auth/login."
    });
    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", doc, null), new List<string>() }
    });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Api-Key", Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header,
        Description = "The client application's key (ApiKeys in appsettings.Development.json)."
    });
    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("ApiKey", doc, null), new List<string>() }
    });
});

// CORS so the standalone test console (test-console/index.html, opened from disk) can call the API.
// Bearer tokens travel in a header, not a cookie, so AllowAnyOrigin is safe here.
builder.Services.AddCors(o => o.AddPolicy("test-console", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await app.InitializeDatabaseAsync();   // migrations (+ dev seed) when enabled in config

app.UseCors("test-console");
app.UseMiddleware<ApiKeyMiddleware>();   // 0) is this one of our client apps? (before auth: guards /auth/login too)
app.UseAuthentication();                 // 1) who are you?
app.UseAuthorization();                  // 2) are you allowed?

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapCarter();

app.Run();

public partial class Program { }   // lets the integration tests boot the app
