using AgenticWorkforce.Infrastructure;
using AgenticWorkforce.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("agenticworkforce")
    ?? throw new InvalidOperationException("Connection string 'agenticworkforce' is required.");

builder.Services.AddInfrastructure(connectionString);

// Durable Task, Agent Runtime registered in Phase 6+

var host = builder.Build();
await host.RunAsync();
