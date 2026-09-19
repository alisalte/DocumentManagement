using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Dms.Storage.Application;
using Dms.Storage.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Storage.Infrastructure.Providers;

/// <summary>
/// S3-compatible storage. Written against the plain S3 API rather than a vendor SDK, so MinIO,
/// SeaweedFS, Ceph RGW or AWS itself all work with only a configuration change.
///
/// Uploads go through TransferUtility, which splits large files into parts and never buffers the
/// whole stream: uploads arrive as non-seekable request bodies and may be gigabytes.
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly TransferUtility _transfer;
    private readonly ILogger<S3FileStorage> _logger;

    public S3FileStorage(IOptions<StorageOptions> options, ILogger<S3FileStorage> logger)
    {
        _logger = logger;
        var settings = options.Value.S3;

        var config = new AmazonS3Config
        {
            ServiceURL = settings.ServiceUrl,
            ForcePathStyle = settings.ForcePathStyle,
            AuthenticationRegion = settings.Region,
        };

        _client = new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
            config);

        _transfer = new TransferUtility(_client);
    }

    public string Provider => "s3";

    public async Task PutAsync(
        ObjectLocation location,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var request = new TransferUtilityUploadRequest
        {
            BucketName = location.Bucket,
            Key = location.Key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            PartSize = 8 * 1024 * 1024,
        };

        await _transfer.UploadAsync(request, cancellationToken);
    }

    public async Task<Stream> OpenAsync(
        ObjectLocation location,
        ByteRange? range,
        CancellationToken cancellationToken)
    {
        var request = new GetObjectRequest
        {
            BucketName = location.Bucket,
            Key = location.Key,
        };

        if (range is { } wanted)
        {
            request.ByteRange = new ByteRange(wanted.From, wanted.To ?? long.MaxValue);
        }

        var response = await _client.GetObjectAsync(request, cancellationToken);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(ObjectLocation location, CancellationToken cancellationToken)
    {
        try
        {
            await _client.DeleteObjectAsync(location.Bucket, location.Key, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone: deletion is idempotent.
        }
    }

    public async Task<bool> ExistsAsync(ObjectLocation location, CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetObjectMetadataAsync(location.Bucket, location.Key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Creates the bucket when missing, so a fresh environment needs no manual setup.</summary>
    public async Task EnsureBucketAsync(string bucket, CancellationToken cancellationToken)
    {
        try
        {
            var buckets = await _client.ListBucketsAsync(cancellationToken);
            if (buckets.Buckets?.Any(existing => existing.BucketName == bucket) == true)
            {
                return;
            }

            await _client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, cancellationToken);
            _logger.LogInformation("Created storage bucket {Bucket}.", bucket);
        }
        catch (AmazonS3Exception exception)
        {
            _logger.LogWarning(exception, "Could not verify or create bucket {Bucket}.", bucket);
        }
    }

    public void Dispose()
    {
        _transfer.Dispose();
        _client.Dispose();
    }
}
