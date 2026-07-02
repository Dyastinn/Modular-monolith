using Erp.Api.Endpoints.Orders;
using Erp.Application.Orders.Ping;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<PingHandler>();

var app = builder.Build();

app.MapPingEndpoint();

app.Run();
