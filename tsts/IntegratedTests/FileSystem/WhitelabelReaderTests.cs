using Application.UseCases;
using Application.UseCases.Base.Interfaces;
using Application.UseCases.GetProducerWhitelabel;
using Application.UseCases.GetProducerWhitelabel.Inputs;
using Application.UseCases.GetProducerWhitelabel.Outputs;
using Domain.ValueObjects;
using Infra.FileSystem.Base.Configuration;
using Infra.FileSystem.Whitelabels;
using Infra.FileSystem.Whitelabels.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntegratedTests.FileSystem;

/// <summary>A dependência real deste requisito é o disco: cada teste usa uma pasta temporária própria.</summary>
public sealed class WhitelabelReaderTests : IDisposable
{
    private const string VALID_JSON = """
        {
          "version": 1,
          "brand": { "name": "Palco Sul", "logoUrl": "", "faviconUrl": "/favicon.svg" },
          "colors": { "primary": { "500": "#0044ff" }, "action": { "base": "#0044ff" } },
          "radius": { "md": "6px" },
          "internalNotes": "contrato de 2026, desconto de 30%"
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ntix-whitelabel-" + Guid.NewGuid().ToString("N"));
    private readonly WhitelabelReader _reader;

    public WhitelabelReaderTests()
    {
        Directory.CreateDirectory(_directory);
        _reader = new WhitelabelReader(new WhitelabelConfiguration(_directory));
    }

    public void Dispose()
        => Directory.Delete(_directory, recursive: true);

    private Task WriteFileAsync(string fileName, string content)
        => File.WriteAllTextAsync(Path.Combine(_directory, fileName), content);

    [Fact]
    public async Task Given_ValidFile_When_Reading_Should_ReturnTheModelWithoutUnknownProperties()
    {
        // Arrange
        await WriteFileAsync("palco-sul.json", VALID_JSON);

        // Act
        var result = await _reader.ReadAsync("palco-sul");

        // Assert
        Assert.Equal(WhitelabelReadStatus.Found, result.Status);
        Assert.Equal("Palco Sul", result.Model!.Brand!.Name);
        Assert.Equal("#0044ff", result.Model.Colors!.Primary!["500"]);
        Assert.Equal("6px", result.Model.Radius!["md"]);
    }

    [Fact]
    public async Task Given_NoFile_When_Reading_Should_ReturnNotFound()
    {
        // Act
        var result = await _reader.ReadAsync("sem-arquivo");

        // Assert
        Assert.Equal(WhitelabelReadStatus.NotFound, result.Status);
        Assert.Null(result.Model);
    }

    [Fact]
    public async Task Given_FileAboveLimit_When_Reading_Should_ReturnTooLarge()
    {
        // Arrange
        var padding = new string(' ', (int)WhitelabelReader.MAX_FILE_BYTES);
        await WriteFileAsync("grande.json", "{" + padding + "}");

        // Act
        var result = await _reader.ReadAsync("grande");

        // Assert
        Assert.Equal(WhitelabelReadStatus.TooLarge, result.Status);
    }

    [Theory]
    [InlineData("{ \"colors\": ")]
    [InlineData("{ \"version\": \"1\" }")]
    [InlineData("{ \"colors\": { \"primary\": { \"500\": 42 } } }")]
    [InlineData("{ \"colors\": { \"primary\": { \"500\": null } } }")]
    [InlineData("{ \"typography\": { \"stylesheets\": [null] } }")]
    [InlineData("// comentário\n{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task Given_MalformedFile_When_Reading_Should_ReturnMalformed(string content)
    {
        // Arrange
        await WriteFileAsync("malformado.json", content);

        // Act
        var result = await _reader.ReadAsync("malformado");

        // Assert
        Assert.Equal(WhitelabelReadStatus.Malformed, result.Status);
    }

    [Fact]
    public async Task Given_JsonDeeperThanLimit_When_Reading_Should_ReturnMalformed()
    {
        // Arrange
        var depth = WhitelabelReader.MAX_DEPTH + 2;
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);
        await WriteFileAsync("profundo.json", deep);

        // Act
        var result = await _reader.ReadAsync("profundo");

        // Assert
        Assert.Equal(WhitelabelReadStatus.Malformed, result.Status);
    }

    [Fact]
    public async Task Given_SlugThatEscapesTheDirectory_When_Reading_Should_NotReadOutsideTheFolder()
    {
        // Arrange: um arquivo válido fora da pasta configurada.
        var outside = Path.Combine(Path.GetDirectoryName(_directory)!, "fora-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(outside + ".json", VALID_JSON);

        try
        {
            // Act
            var result = await _reader.ReadAsync("../" + Path.GetFileName(outside));

            // Assert
            Assert.Equal(WhitelabelReadStatus.NotFound, result.Status);
        }
        finally
        {
            File.Delete(outside + ".json");
        }
    }

    [Fact]
    public async Task Given_ValidFileWithUnknownProperty_When_ExecutingTheUseCase_Should_ServeOnlyTheContract()
    {
        // Arrange
        await WriteFileAsync("palco-sul.json", VALID_JSON);
        using var services = BuildServices();
        using var scope = services.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<GetProducerWhitelabelUseCaseInput, GetProducerWhitelabelUseCaseOutput>>();

        // Act
        var result = await useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create("palco-sul"), NewAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Palco Sul", result.Output!.Brand!.Name);
        Assert.Null(result.Output.Brand.LogoUrl);
        Assert.DoesNotContain("contrato", System.Text.Json.JsonSerializer.Serialize(result.Output));
    }

    [Fact]
    public async Task Given_FileWithInsecureValue_When_ExecutingTheUseCase_Should_NotServeIt()
    {
        // Arrange
        await WriteFileAsync("palco-sul.json", """{ "brand": { "logoUrl": "javascript:alert(1)" } }""");
        using var services = BuildServices();
        using var scope = services.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<GetProducerWhitelabelUseCaseInput, GetProducerWhitelabelUseCaseOutput>>();

        // Act
        var result = await useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create("palco-sul"), NewAuditableInfo());

        // Assert
        Assert.True(result.IsError);
        Assert.Contains(result.Notifications, notification => notification.Code == GetProducerWhitelabelUseCase.WHITELABEL_INVALID_FILE_NOTIFICATION_CODE);
    }

    [Fact]
    public async Task Given_DefaultNtixFile_When_ExecutingTheUseCase_Should_BeServed()
    {
        // Arrange: o arquivo implantado no servidor precisa passar na validação.
        var defaultFile = Path.Combine(AppContext.BaseDirectory, "whitelabel", "ntix.json");
        File.Copy(defaultFile, Path.Combine(_directory, "ntix.json"));
        using var services = BuildServices();
        using var scope = services.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IUseCase<GetProducerWhitelabelUseCaseInput, GetProducerWhitelabelUseCaseOutput>>();

        // Act
        var result = await useCase.ExecuteAsync(GetProducerWhitelabelUseCaseInput.Create("ntix"), NewAuditableInfo());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("#c99b14", result.Output!.Colors!.Primary!["500"]);
    }

    private ServiceProvider BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhitelabelConfiguration:DirectoryPath"] = _directory,
                ["PasswordConfiguration:PrivateKey"] = "chave-de-teste",
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddWhitelabelReaderConfiguration(configuration)
            .AddUseCasesConfiguration(configuration)
            .BuildServiceProvider();
    }

    private static AuditableInfoValueObject NewAuditableInfo()
        => AuditableInfoValueObject.Build(
            correlationId: Guid.NewGuid().ToString(),
            originService: "integrated-tests",
            executionUser: null,
            requestedAt: DateTime.UtcNow.ToString(AuditableInfoValueObject.REQUESTED_AT_FORMAT, System.Globalization.CultureInfo.InvariantCulture));
}
