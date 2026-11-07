using Infra.FileSystem.Whitelabels.Models;

namespace Infra.FileSystem.Whitelabels.Interfaces;

public interface IWhitelabelReader
{
    /// <param name="slug">Slug já validado pela Application; o leitor ainda confere que o arquivo fica dentro da pasta.</param>
    public Task<WhitelabelReadResult> ReadAsync(string slug, CancellationToken cancellationToken = default);
}
