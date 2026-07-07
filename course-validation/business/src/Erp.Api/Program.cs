using System.Security.Claims;
using System.Text;
using Carter;
using Erp.Api;
using Erp.Api.Auth;
using Erp.Modules.Inventory;
using Erp.Modules.Sales;
using Erp.Shared.Infrastructure;
using Erp.Shared.Messaging;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Erp Sales API", Version = "v1" });
    // Let testers paste a JWT into the Swagger "Authorize" box.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token from POST /auth/login (no 'Bearer ' prefix needed)."
    });
    options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", doc, null), new List<string>() }
    });
});

builder.Services.AddCarter();   // discovers every feature's ICarterModule endpoint

builder.Services.AddSharedInfrastructure(builder.Configuration);   // mediator, Dapper, JWT, ICurrentUser
builder.Services.AddSalesModule(builder.Configuration);
builder.Services.AddInventoryModule(builder.Configuration);

// The auth slices live in the HOST assembly, so the module scans don't see them — register them explicitly.
builder.Services.AddScoped<IRequestHandler<LoginCommand, LoginResult>, LoginHandler>();
builder.Services.AddScoped<IValidator<LoginCommand>, LoginValidator>();
builder.Services.AddScoped<IRequestHandler<RefreshCommand, LoginResult>, RefreshHandler>();
builder.Services.AddScoped<IRequestHandler<LogoutCommand, Unit>, LogoutHandler>();
builder.Services.AddScoped<IRequestHandler<LogoutAllCommand, Unit>, LogoutAllHandler>();

// JWT token validation (separate from token creation at login).
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // CRITICAL: keep JWT claim names as issued, so "sub" reads back as "sub" (Phase 4 §7 gotcha).
        options.MapInboundClaims = false;
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

// CORS so the standalone test console (test-console/index.html, opened straight from disk) can call the
// API cross-origin. Bearer tokens travel in the Authorization header (no cookies), so AllowAnyOrigin is fine.
builder.Services.AddCors(o => o.AddPolicy("test-console", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Create/patch the database from each module's embedded SQL scripts (dev convenience).
await DatabaseInitializer.RunAsync(app);

app.UseCors("test-console");

app.UseAuthentication();   // 1) read & validate the token → figure out WHO you are
app.UseAuthorization();    // 2) check WHETHER you're allowed

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapCarter();   // discovers every feature's ICarterModule endpoint

app.Run();

public partial class Program { }   // makes Program visible to the integration tests (Phase 12)
