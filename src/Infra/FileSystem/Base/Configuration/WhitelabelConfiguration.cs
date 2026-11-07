namespace Infra.FileSystem.Base.Configuration;

/// <summary>Pasta do servidor com um arquivo <c>{slug}.json</c> por produtora.</summary>
public sealed record WhitelabelConfiguration(string DirectoryPath);
