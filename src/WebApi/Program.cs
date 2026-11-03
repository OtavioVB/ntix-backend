using Application.UseCases;
using Infra.PostgreSql.Base.UnitOfWork;
using Infra.PostgreSql.Repositories.Customers;
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
        });
        builder.Services.AddOpenApi();

        builder.Services
            .AddPostgreSqlConfiguration(builder.Configuration)
            .AddCustomerRepositoryConfiguration(builder.Configuration);

        builder.Services
            .AddUseCasesConfiguration(builder.Configuration);

        var app = builder.Build();

        app.MapOpenApi();
        app.MapControllers();

        app.Run();
    }
}
