using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Metro.Application.Abstractions.Storage;
using Microsoft.Extensions.Options;

namespace Metro.Infrastructure.Storage;

/// <summary>
/// S3-compatible client for Neon Object Storage. Neon uses path-style addressing and a
/// branch-scoped endpoint, so the bucket name is the only value the app has to choose.
/// </summary>
public sealed class NeonObjectStorage : IObjectStorage, IDisposable
{
    private readonly AmazonS3Client _client;

    public NeonObjectStorage(IOptions<NeonStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _client = CreateClient(options.Value);
        Bucket = options.Value.Bucket;
    }

    public string Bucket { get; }

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        // A one-object listing proves the endpoint, credentials and bucket all resolve.
        await _client.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = Bucket, MaxKeys = 1 },
            cancellationToken);

        return true;
    }

    public async Task PutAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        await _client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = Bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                AutoCloseStream = false,
            },
            cancellationToken);
    }

    public async Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var response = await _client.GetObjectAsync(
            new GetObjectRequest { BucketName = Bucket, Key = key },
            cancellationToken);

        var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    public Task<Uri> CreateDownloadUrlAsync(
        string key,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var url = _client.GetPreSignedURL(
            new GetPreSignedUrlRequest
            {
                BucketName = Bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(lifetime),
            });

        return Task.FromResult(new Uri(url));
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _client.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = Bucket, Key = key },
            cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    private static AmazonS3Client CreateClient(NeonStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            throw new InvalidOperationException(
                "Object storage is not configured. Run 'neon deploy' to pull AWS_ENDPOINT_URL_S3 into the env file.");
        }

        var config = new AmazonS3Config
        {
            // Neon speaks the S3 API with path-style addressing only.
            ForcePathStyle = true,
            ServiceURL = options.ServiceUrl,
            AuthenticationRegion = options.Region,
        };

        var credentials = string.IsNullOrWhiteSpace(options.AccessKeyId)
            || string.IsNullOrWhiteSpace(options.SecretAccessKey)
                ? null
                : new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);

        return credentials is null
            ? new AmazonS3Client(config)
            : new AmazonS3Client(credentials, config);
    }
}
