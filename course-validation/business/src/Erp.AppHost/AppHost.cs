var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Erp_Api>("erp-api");

builder.Build().Run();
