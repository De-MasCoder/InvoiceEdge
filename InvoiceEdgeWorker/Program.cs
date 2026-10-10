using InvoiceEdgeWorker;
using InvoiceEdgeWorker.Configuration;
using InvoiceEdgeWorker.Data;
using InvoiceEdgeWorker.Repositories;
using InvoiceEdgeWorker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// 1. Extract and register the Connection String
var connectionString = builder.Configuration.GetConnectionString("PostgresConnection");

builder.Services.AddDbContextFactory<InvoiceDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Register Scoped Repositories and Unit of Work Pattern
// Because our background service is a long-running Singleton, we inject factories or use scope blocks 
builder.Services.AddScoped<IUnitofWork, UnitOfWork>();

var processingOptions = new InvoiceProcessingOptions();
builder.Configuration.GetSection("InvoiceProcessing").Bind(processingOptions);
builder.Services.AddSingleton(processingOptions);
builder.Services.AddSingleton<IInvoiceExtractor>(_ =>
    new OllamaInvoiceExtractor(
        new Uri(processingOptions.OllamaEndpoint),
        processingOptions.OllamaModel));
builder.Services.AddSingleton<InvoiceResponseParser>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
