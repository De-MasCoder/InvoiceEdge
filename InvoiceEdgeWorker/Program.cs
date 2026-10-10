using InvoiceEdgeWorker;
using InvoiceEdgeWorker.Data;
using InvoiceEdgeWorker.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// 1. Extract and register the Connection String
var connectionString = builder.Configuration.GetConnectionString("PostgresConnection");

builder.Services.AddDbContextFactory<InvoiceDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Register Scoped Repositories and Unit of Work Pattern
// Because our background service is a long-running Singleton, we inject factories or use scope blocks 
builder.Services.AddScoped<IUnitofWork, UnitOfWork>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
