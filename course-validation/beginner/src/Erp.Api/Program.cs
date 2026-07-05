using Carter;
using Erp.Modules.Inventory;
using Erp.Modules.Sales;
using Erp.Shared.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedInfrastructure(builder.Configuration);  // mediator, db, Carter services
builder.Services.AddSalesModule(builder.Configuration);           // the Sales module plugs itself in
builder.Services.AddInventoryModule(builder.Configuration);       // the Inventory module plugs itself in

var app = builder.Build();
app.MapCarter();   // Carter finds every ICarterModule and maps its routes
app.Run();
