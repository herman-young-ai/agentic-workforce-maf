using Serilog;
using Serilog.Enrichers.Sensitive;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace AgenticWorkforce.Api.Core.Observability;

/// <summary>
/// Structured logging via Serilog with PII masking.
/// Adopted from SecurityBff reference, extended for agentic platform.
/// </summary>
public static class TelemetryExtensions
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{Source}] {CorrelationId,-36} {Message:lj}{NewLine}{Exception}";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        var aiConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        var logPath = Path.Combine(
            builder.Environment.ContentRootPath, "..", "..", "var", "logs", "system.jsonl");

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.WithSensitiveDataMasking(opts =>
            {
                opts.MaskingOperators.Add(new EmailAddressMaskingOperator());
                opts.MaskingOperators.Add(new IbanMaskingOperator());
            })
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "AgenticWorkforce.Api")
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .Enrich.WithProperty("Source", "web")
#pragma warning disable CA1305
            .WriteTo.Console(outputTemplate: ConsoleTemplate)
#pragma warning restore CA1305
            .WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: false);

        if (!string.IsNullOrEmpty(aiConnectionString))
            builder.Services.AddApplicationInsightsTelemetry(opts =>
                opts.ConnectionString = aiConnectionString);

        Log.Logger = logConfig.CreateLogger();
        builder.Host.UseSerilog();

        return builder;
    }
}
