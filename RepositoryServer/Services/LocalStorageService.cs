using System.Runtime.CompilerServices;
using OpenShock.RepositoryServer.Config;

namespace OpenShock.RepositoryServer.Services;

public sealed class LocalStorageService : IStorageService
{
    private readonly string _basePath;

    public LocalStorageService(LocalStorageConfig config)
    {
        _basePath = Path.GetFullPath(config.BasePath);
        Directory.CreateDirectory(_basePath);
    }

    /// <inheritdoc />
    public async Task UploadFileAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(path);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteDirectoryAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(prefix);
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListFilesAsync(string prefix = "", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(prefix);
        if (!Directory.Exists(fullPath)) yield break;

        await Task.CompletedTask; // async enumerable requires at least one await
        foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
        {
            yield return Path.GetRelativePath(_basePath, file).Replace('\\', '/');
        }
    }

    private string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, path));
        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
            throw new ArgumentException("Path traversal is not allowed.", nameof(path));
        return fullPath;
    }
}
