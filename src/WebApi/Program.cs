using Application.UseCases;
using Infra.Jwt.Base.TokenGenerator;
using Infra.SqlServer.Base.UnitOfWork;
using Infra.SqlServer.Repositories.Customers;
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
            .AddSqlServerConfiguration(builder.Configuration)
            .AddCustomerRepositoryConfiguration(builder.Configuration)
            .AddJwtConfiguration(builder.Configuration);

        builder.Services
            .AddUseCasesConfiguration(builder.Configuration);

        var app = builder.Build();

        app.MapOpenApi();
        app.MapControllers();

        app.Run();
    }
}
