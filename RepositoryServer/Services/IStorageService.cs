namespace OpenShock.RepositoryServer.Services;

public interface IStorageService
{
    /// <summary>
    /// Uploads a file to storage.
    /// </summary>
    /// <param name="path">Path relative to storage root, e.g. "1.0.0/board-name/app.bin"</param>
    /// <param name="content">File content stream</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UploadFileAsync(string path, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from storage.
    /// </summary>
    /// <param name="path">Path relative to storage root, e.g. "1.0.0/board-name/app.bin"</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all files under a directory prefix.
    /// </summary>
    /// <param name="prefix">Directory prefix, e.g. "1.0.0/" to delete an entire version</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteDirectoryAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all file paths under a prefix.
    /// </summary>
    /// <param name="prefix">Directory prefix to list, e.g. "1.0.0/". Use empty string to list all.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Relative file paths under the prefix.</returns>
    IAsyncEnumerable<string> ListFilesAsync(string prefix = "", CancellationToken cancellationToken = default);
}
