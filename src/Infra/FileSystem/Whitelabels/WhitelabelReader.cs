using Infra.FileSystem.Base.Configuration;
using Infra.FileSystem.Whitelabels.Enums;
using Infra.FileSystem.Whitelabels.Interfaces;
using Infra.FileSystem.Whitelabels.Models;
using System.Diagnostics;
using System.Text.Json;

namespace Infra.FileSystem.Whitelabels;

/// <summary>Lê o arquivo <c>{slug}.json</c> da pasta configurada. Não decide regra: só lê, limita e desserializa.</summary>
public sealed class WhitelabelReader : IWhitelabelReader
{
    public const long MAX_FILE_BYTES = 64 * 1024;
    public const int MAX_DEPTH = 8;
    public const string FILE_EXTENSION = ".json";

    private static readonly ActivitySource ACTIVITY_SOURCE = new("Ntix.Producers");

    private static readonly JsonSerializerOptions SERIALIZER_OPTIONS = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = MAX_DEPTH,
        // null em valor de escala ou de lista é arquivo malformado, não um valor vazio.
        RespectNullableAnnotations = true,
        // Número onde se espera texto (e o contrário) é malformado: nada de conversão implícita.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
    };

    private readonly string _directoryPath;

    public WhitelabelReader(WhitelabelConfiguration configuration)
    {
        // O separador no fim impede que "/app/whitelabel-x" passe como se estivesse dentro de "/app/whitelabel".
        _directoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuration.DirectoryPath)) + Path.DirectorySeparatorChar;
    }

    public async Task<WhitelabelReadResult> ReadAsync(string slug, CancellationToken cancellationToken = default)
    {
        using var activity = ACTIVITY_SOURCE.StartActivity("WhitelabelReader.Read");

        var filePath = Path.GetFullPath(Path.Combine(_directoryPath, slug + FILE_EXTENSION));

        if (!filePath.StartsWith(_directoryPath, StringComparison.Ordinal))
            return new(WhitelabelReadStatus.NotFound, Model: null);

        var file = new FileInfo(filePath);

        if (!file.Exists)
            return new(WhitelabelReadStatus.NotFound, Model: null);

        if (file.Length > MAX_FILE_BYTES)
            return new(WhitelabelReadStatus.TooLarge, Model: null);

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

            // O tamanho é conferido de novo na leitura: o arquivo pode ter crescido depois do FileInfo.
            var buffer = new byte[MAX_FILE_BYTES + 1];
            var length = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
            {
                length += read;

                if (length > MAX_FILE_BYTES)
                    return new(WhitelabelReadStatus.TooLarge, Model: null);
            }

            var model = JsonSerializer.Deserialize<WhitelabelModel>(buffer.AsSpan(0, length), SERIALIZER_OPTIONS);

            return model is null
                ? new(WhitelabelReadStatus.Malformed, Model: null)
                : new(WhitelabelReadStatus.Found, model);
        }
        catch (JsonException)
        {
            return new(WhitelabelReadStatus.Malformed, Model: null);
        }
        catch (FileNotFoundException)
        {
            // Removido entre a verificação e a leitura.
            return new(WhitelabelReadStatus.NotFound, Model: null);
        }
    }
}
