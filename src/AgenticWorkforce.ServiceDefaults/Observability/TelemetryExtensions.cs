using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Enrichers.Sensitive;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace AgenticWorkforce.ServiceDefaults.Observability;

/// <summary>
/// Structured logging via Serilog with PII masking. Shared by Api and Worker
/// so log shape, masking rules, and sinks stay identical across hosts.
/// AppInsights wiring is intentionally NOT included here — only the Api needs
/// the AspNetCore telemetry module, and it's wired in <c>Program.cs</c> after
/// <c>AddObservability</c>.
/// </summary>
public static class TelemetryExtensions
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{Source}] {CorrelationId,-36} {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Registers Serilog as the host's logging provider.
    /// </summary>
    /// <param name="applicationName">
    /// Value of the <c>Application</c> log property (e.g. <c>"AgenticWorkforce.Api"</c>).
    /// </param>
    /// <param name="source">
    /// Value of the <c>Source</c> log property — coarse component label used by
    /// downstream filters (e.g. <c>"web"</c>, <c>"worker"</c>).
    /// </param>
    public static IHostApplicationBuilder AddObservability(
        this IHostApplicationBuilder builder,
        string applicationName,
        string source)
    {
        var logPath = builder.Configuration["Observability:LogPath"]
            ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "var", "logs", "system.jsonl");

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
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .Enrich.WithProperty("Source", source)
            .WriteTo.Console(outputTemplate: ConsoleTemplate, formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: false);

        Log.Logger = logConfig.CreateLogger();
        builder.Services.AddSerilog();

        return builder;
    }
}
