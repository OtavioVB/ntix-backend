using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WebApi.Observability;

public static class TelemetryExtensions
{
    private const string DEFAULT_SERVICE_NAME = "ntix-webapi";
    private const string INSTRUMENTATION_NAME_PATTERN = "Ntix.*";
    private const string RUNTIME_METER_NAME = "System.Runtime";

    // O padrão do SDK (60s) deixa uma única amostra por janela de rate() e os painéis do Grafana ficam vazios.
    private const string DEFAULT_METRIC_EXPORT_INTERVAL_IN_MILLISECONDS = "10000";

    public static WebApplicationBuilder AddTelemetry(this WebApplicationBuilder builder)
    {
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? DEFAULT_SERVICE_NAME;

        builder.Configuration["OTEL_METRIC_EXPORT_INTERVAL"] ??= DEFAULT_METRIC_EXPORT_INTERVAL_IN_MILLISECONDS;

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddSource(INSTRUMENTATION_NAME_PATTERN)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/openapi");
                }))
            .WithMetrics(metrics => metrics
                .AddMeter(INSTRUMENTATION_NAME_PATTERN, RUNTIME_METER_NAME)
                .AddAspNetCoreInstrumentation()
                .SetExemplarFilter(ExemplarFilterType.TraceBased))
            .UseOtlpExporter();

        return builder;
    }
}
