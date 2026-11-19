var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Yasda_Api>("yasda-api");

builder.Build().Run();
