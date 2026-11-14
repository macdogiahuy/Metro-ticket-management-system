namespace Metro.Infrastructure.Storage;

/// <summary>
/// Object storage settings. The endpoint, region and credentials come from the AWS_* variables
/// that <c>neon deploy</c> writes to the local env file; the bucket is declared in neon.ts.
/// </summary>
public sealed class NeonStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Bucket { get; set; } = "assets";

    public string? ServiceUrl { get; set; }

    public string? Region { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }
}
