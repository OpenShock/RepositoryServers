using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using OpenShock.RepositoryServer.Config;

namespace OpenShock.RepositoryServer.Services;

public sealed class S3StorageService : IStorageService, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string? _keyPrefix;

    public S3StorageService(S3StorageConfig config)
    {
        _bucketName = config.BucketName;
        _keyPrefix = config.KeyPrefix?.TrimEnd('/');

        var s3Config = new AmazonS3Config();

        if (config.ServiceUrl != null)
        {
            s3Config.ServiceURL = config.ServiceUrl;
        }

        if (config.Region != null)
        {
            s3Config.AuthenticationRegion = config.Region;
        }

        _s3Client = new AmazonS3Client(config.AccessKey, config.SecretKey, s3Config);
    }

    /// <inheritdoc />
    public async Task UploadFileAsync(string path, Stream content, CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = ResolveKey(path),
            InputStream = content,
            ContentType = "application/octet-stream",
        };

        await _s3Client.PutObjectAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await _s3Client.DeleteObjectAsync(_bucketName, ResolveKey(path), cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteDirectoryAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var resolvedPrefix = ResolveKey(prefix.TrimEnd('/') + "/");

        var listRequest = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = resolvedPrefix,
        };

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client.ListObjectsV2Async(listRequest, cancellationToken);

            if (response.S3Objects.Count > 0)
            {
                var deleteRequest = new DeleteObjectsRequest
                {
                    BucketName = _bucketName,
                    Objects = response.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList(),
                };
                await _s3Client.DeleteObjectsAsync(deleteRequest, cancellationToken);
            }

            listRequest.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListFilesAsync(string prefix = "", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resolvedPrefix = ResolveKey(prefix);

        var listRequest = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = resolvedPrefix,
        };

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client.ListObjectsV2Async(listRequest, cancellationToken);

            foreach (var obj in response.S3Objects)
            {
                // Strip key prefix to return paths relative to storage root
                var relativePath = _keyPrefix != null
                    ? obj.Key[(_keyPrefix.Length + 1)..]
                    : obj.Key;
                yield return relativePath;
            }

            listRequest.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
    }

    private string ResolveKey(string path) => _keyPrefix != null ? $"{_keyPrefix}/{path}" : path;

    public void Dispose() => _s3Client.Dispose();
}
