using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Integration.Tests;

// Boots the whole API in memory against a SEPARATE test database (never the dev DB). Program's
// DatabaseInitializer creates Erp_Test and applies each module's SQL scripts on boot (Phase 12 §5).
public sealed class CustomWebAppFactory : WebApplicationFactory<Program>
{
    public const string TestConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=Erp_Test;Trusted_Connection=True;TrustServerCertificate=True";

    // The ApiKeyMiddleware (Phase 4c §11) gates EVERY request, including /auth/login. Tests are a client
    // app like any other, so they get their own key rather than an exemption — a test that skips the gate
    // wouldn't be exercising the real pipeline.
    public const string TestApiKey = "test-suite-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development so appsettings.Development.json (the JWT signing key) loads; override the DB to Erp_Test.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ErpDb", TestConnectionString);
        builder.UseSetting("ApiKeys:tests", TestApiKey);
    }

    // Every client this factory hands out already carries the API key.
    protected override void ConfigureClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        base.ConfigureClient(client);
    }
}
