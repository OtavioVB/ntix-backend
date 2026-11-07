using System.Text.Json.Serialization;
using Application.UseCases;
using Infra.FileSystem.Whitelabels;
using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Customers;
using Infra.PostgreSql.Repositories.Outbox;
using Infra.PostgreSql.Repositories.Producers;
using WebApi.Cors;
using WebApi.Observability;

namespace WebApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddTelemetry();

        builder.Services.AddControllers(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        }).AddJsonOptions(options =>
        {
            // Campo opcional ausente fica fora da resposta, em vez de sair como null (ex.: whitelabel parcial).
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        builder.Services.AddOpenApi();
        builder.Services.AddCorsConfiguration(builder.Configuration);

        builder.Services
            .AddPostgreSqlConfiguration(builder.Configuration)
            .AddCustomerRepositoryConfiguration(builder.Configuration)
            .AddProducerRepositoryConfiguration(builder.Configuration)
            .AddOutboxRepositoryConfiguration(builder.Configuration)
            .AddWhitelabelReaderConfiguration(builder.Configuration);

        builder.Services
            .AddUseCasesConfiguration(builder.Configuration);

        var app = builder.Build();

        app.UseCors(CorsExtensions.WEB_POLICY_NAME);

        app.MapOpenApi();
        app.MapControllers();

        app.Run();
    }
}
