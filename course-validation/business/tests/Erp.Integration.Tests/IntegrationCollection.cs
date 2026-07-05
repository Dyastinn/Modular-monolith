namespace Erp.Integration.Tests;

// All integration tests share ONE test database (Erp_Test), so they must not run in parallel against it.
// A single collection with a shared factory fixture boots the app (and DatabaseInitializer) exactly once
// and serializes the classes.
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<CustomWebAppFactory> { }
