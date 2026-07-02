using Erp.Api.Endpoints.Orders;
using Erp.Infrastructure.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMessaging();

var app = builder.Build();

app.MapPingEndpoint();

app.Run();
