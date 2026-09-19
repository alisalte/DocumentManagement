using System.Security.Cryptography;

namespace Dms.Storage.Application;

/// <summary>
/// Wraps the upload stream so the file is hashed, measured and sampled **as it flows to storage**,
/// in one pass. Buffering a 2 GB upload to hash it afterwards is not an option, and re-reading it
/// from storage would double the I/O.
/// </summary>
public sealed class HashingReadStream(Stream inner, long maxBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _sample = new byte[MimeSniffer.SampleSize];
    private int _sampleLength;
    private long _bytesRead;
    private byte[]? _digest;

    public long BytesRead => _bytesRead;

    public ReadOnlySpan<byte> Sample => _sample.AsSpan(0, _sampleLength);

    /// <summary>Valid once the stream has been read to the end.</summary>
    public byte[] Digest => _digest ??= _hash.GetHashAndReset();

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        Observe(buffer[..read]);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        Observe(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private void Observe(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
        {
            return;
        }

        _bytesRead += chunk.Length;
        if (_bytesRead > maxBytes)
        {
            // Stop as soon as the limit is passed, so an oversized upload cannot fill the disk.
            throw new UploadTooLargeException(maxBytes);
        }

        _hash.AppendData(chunk);

        if (_sampleLength < _sample.Length)
        {
            var take = Math.Min(_sample.Length - _sampleLength, chunk.Length);
            chunk[..take].CopyTo(_sample.AsSpan(_sampleLength));
            _sampleLength += take;
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed class UploadTooLargeException(long maxBytes)
    : Exception($"The upload exceeds the maximum of {maxBytes} bytes.")
{
    public long MaxBytes { get; } = maxBytes;
}
