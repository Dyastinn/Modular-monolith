namespace Erp.Integration.Tests;

// All integration tests share ONE test database, so they must not run in parallel against it. One collection
// with a shared factory boots the app (and its migrations + seed) exactly once and runs the classes in series.
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<CustomWebAppFactory> { }
