namespace Metro.Application.Abstractions.Storage;

/// <summary>
/// S3-compatible object storage bound to the current environment branch.
/// Credentials and endpoint are supplied through the AWS_* variables injected by Neon.
/// </summary>
public interface IObjectStorage
{
    string Bucket { get; }

    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);

    Task PutAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<Uri> CreateDownloadUrlAsync(
        string key,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
