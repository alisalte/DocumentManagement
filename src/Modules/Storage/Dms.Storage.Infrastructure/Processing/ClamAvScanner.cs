using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Dms.Storage.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dms.Storage.Infrastructure.Processing;

/// <summary>
/// ClamAV through clamd's INSTREAM command over TCP: the file is streamed in chunks, so clamd
/// needs no access to our storage. Any answer other than a clean "OK" or a clear "FOUND" is a
/// failure, and a failure keeps the file blocked (decision D9).
/// </summary>
public sealed class ClamAvScanner(
    IFileStorage files,
    IOptions<StorageOptions> options,
    ILogger<ClamAvScanner> logger) : IMalwareScanner
{
    private const int ChunkSize = 64 * 1024;

    public bool IsEnabled => options.Value.Scanning.Enabled;

    public async Task<ScanVerdict> ScanAsync(ObjectLocation location, CancellationToken cancellationToken)
    {
        var settings = options.Value.Scanning;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(settings.ClamAvHost, settings.ClamAvPort, cancellationToken);
            await using var network = client.GetStream();

            await network.WriteAsync("zINSTREAM\0"u8.ToArray(), cancellationToken);

            await using (var content = await files.OpenAsync(location, null, cancellationToken))
            {
                var buffer = new byte[ChunkSize];
                var length = new byte[4];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(length, (uint)read);
                    await network.WriteAsync(length, cancellationToken);
                    await network.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            await network.WriteAsync(new byte[4], cancellationToken);

            using var reply = new MemoryStream();
            var chunk = new byte[256];
            int got;
            while ((got = await network.ReadAsync(chunk, cancellationToken)) > 0)
            {
                reply.Write(chunk, 0, got);
                if (chunk[got - 1] == 0)
                {
                    break;
                }
            }

            var answer = Encoding.ASCII.GetString(reply.ToArray()).TrimEnd('\0', '\n');
            if (answer.EndsWith("OK", StringComparison.Ordinal))
            {
                return ScanVerdict.Clean;
            }

            if (answer.EndsWith("FOUND", StringComparison.Ordinal))
            {
                logger.LogWarning("ClamAV reported {Answer} for {Key}.", answer, location.Key);
                return ScanVerdict.Infected;
            }

            logger.LogError("Unexpected ClamAV answer {Answer} for {Key}.", answer, location.Key);
            return ScanVerdict.Failed;
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            logger.LogError(exception, "ClamAV is not reachable at {Host}:{Port}.", settings.ClamAvHost, settings.ClamAvPort);
            return ScanVerdict.Failed;
        }
    }
}
