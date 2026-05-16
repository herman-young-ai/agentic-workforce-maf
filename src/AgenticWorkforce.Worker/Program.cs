using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// -- Database --
builder.Services.AddDbContext<AppDbContext>((sp, opts) =>
    opts.UseNpgsql(
        builder.Configuration.GetConnectionString("agenticworkforce"),
        npgsql => npgsql.EnableRetryOnFailure(3))
        .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

builder.Services.AddScoped<AuditInterceptor>();

// -- Durable Task, Agent Runtime, etc. registered here during implementation --

var host = builder.Build();
await host.RunAsync();
