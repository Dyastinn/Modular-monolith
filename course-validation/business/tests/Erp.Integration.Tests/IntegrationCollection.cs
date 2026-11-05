namespace Erp.Integration.Tests;

// One shared test database: one collection boots the app once and runs the classes in series.
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<CustomWebAppFactory> { }
