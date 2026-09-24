using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Dms.Audit.Domain;

/// <summary>
/// A seal over one period of the audit log (phase 7, tamper evidence). It records how many rows
/// the period held and a digest of them, and it is chained to the seal before it: each seal's
/// hash covers the previous seal's hash. Changing, deleting or back-dating a row in a sealed
/// period changes that period's digest; removing or rewriting a seal breaks the chain after it.
///
/// With a sealing key configured the hash is an HMAC, so someone who can write to the database
/// but does not hold the key cannot forge a matching chain either. Seals are append-only, like
/// the log itself.
/// </summary>
public sealed class AuditSeal
{
    public const string KeyedAlgorithm = "HMAC-SHA256";
    public const string UnkeyedAlgorithm = "SHA-256";

    private AuditSeal()
    {
    }

    public long Sequence { get; private set; }

    /// <summary>Inclusive.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    /// <summary>Exclusive, and the next seal's start.</summary>
    public DateTimeOffset PeriodEnd { get; private set; }

    public long RowCount { get; private set; }

    public byte[] RowsDigest { get; private set; } = [];

    /// <summary>Null only for the first seal.</summary>
    public byte[]? PreviousHash { get; private set; }

    public byte[] SealHash { get; private set; } = [];

    public string Algorithm { get; private set; } = UnkeyedAlgorithm;

    /// <summary>Which key made the HMAC (a short fingerprint, never the key); null when unkeyed.</summary>
    public string? KeyId { get; private set; }

    public DateTimeOffset SealedAt { get; private set; }

    public static AuditSeal Create(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        long rowCount,
        byte[] rowsDigest,
        AuditSeal? previous,
        AuditSealKey? key,
        DateTimeOffset now) => new()
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RowCount = rowCount,
            RowsDigest = rowsDigest,
            PreviousHash = previous?.SealHash,
            SealHash = AuditSealHasher.SealHash(periodStart, periodEnd, rowCount, rowsDigest, previous?.SealHash, key),
            Algorithm = key is null ? UnkeyedAlgorithm : KeyedAlgorithm,
            KeyId = key?.Id,
            SealedAt = now,
        };
}

/// <summary>A sealing key and its fingerprint. The fingerprint is stored on each seal; the key never is.</summary>
public sealed class AuditSealKey
{
    public AuditSealKey(byte[] material)
    {
        if (material.Length < 32)
        {
            throw new ArgumentException("An audit sealing key needs at least 32 bytes.", nameof(material));
        }

        Material = material;
        Id = Convert.ToHexStringLower(SHA256.HashData(material).AsSpan(0, 8));
    }

    public string Id { get; }

    internal byte[] Material { get; }

    public static AuditSealKey FromBase64(string value) => new(Convert.FromBase64String(value));

    /// <summary>Whether a setting holds a usable key: empty (no key) or base64 of at least 32 bytes.</summary>
    public static bool IsValidSetting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out var length) && length >= 32;
    }

    /// <summary>An HMAC with this key, for records that must not be forgeable without it.</summary>
    public byte[] Sign(byte[] data) => HMACSHA256.HashData(Material, data);
}

/// <summary>The audit row fields that are sealed, in their canonical order.</summary>
public readonly record struct SealedRow(
    Guid Id,
    DateTimeOffset OccurredAt,
    string ActorType,
    Guid? UserId,
    Guid? ShareLinkId,
    string Action,
    string Outcome,
    string? EntityType,
    Guid? EntityId,
    Guid? DocumentId,
    Guid? VersionId,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    string Metadata);

/// <summary>
/// The canonical encoding: every field length-prefixed (so no two different rows can encode the
/// same), rows in (occurred_at, id) order, timestamps in UTC with microseconds (PostgreSQL's
/// precision), JSON metadata as PostgreSQL normalises jsonb. Versioned by a domain string, so the
/// format can change later without old seals becoming ambiguous.
/// </summary>
public static class AuditSealHasher
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("dms-audit-seal-v1");

    /// <summary>Feeds rows one by one, so a busy hour is never held in memory.</summary>
    public sealed class RowDigest : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public long Count { get; private set; }

        public void Add(in SealedRow row)
        {
            Field(row.Id.ToString("D"));
            Field(row.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
            Field(row.ActorType);
            Field(row.UserId?.ToString("D"));
            Field(row.ShareLinkId?.ToString("D"));
            Field(row.Action);
            Field(row.Outcome);
            Field(row.EntityType);
            Field(row.EntityId?.ToString("D"));
            Field(row.DocumentId?.ToString("D"));
            Field(row.VersionId?.ToString("D"));
            Field(row.IpAddress);
            Field(row.UserAgent);
            Field(row.CorrelationId);
            Field(row.Metadata);
            Count++;
        }

        public byte[] Finish() => _hash.GetHashAndReset();

        public void Dispose() => _hash.Dispose();

        private void Field(string? value)
        {
            Span<byte> length = stackalloc byte[4];
            if (value is null)
            {
                BinaryPrimitives.WriteInt32BigEndian(length, -1);
                _hash.AppendData(length);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            _hash.AppendData(length);
            _hash.AppendData(bytes);
        }
    }

    public static byte[] SealHash(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        long rowCount,
        byte[] rowsDigest,
        byte[]? previousHash,
        AuditSealKey? key)
    {
        var buffer = new List<byte>(Domain.Length + 128);
        buffer.AddRange(Domain);
        Append(buffer, Int64(periodStart.ToUnixTimeMilliseconds()));
        Append(buffer, Int64(periodEnd.ToUnixTimeMilliseconds()));
        Append(buffer, Int64(rowCount));
        Append(buffer, rowsDigest);
        Append(buffer, previousHash ?? []);

        var data = buffer.ToArray();
        return key is null ? SHA256.HashData(data) : HMACSHA256.HashData(key.Material, data);
    }

    private static byte[] Int64(long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return bytes;
    }

    private static void Append(List<byte> buffer, byte[] part)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, part.Length);
        buffer.AddRange(length);
        buffer.AddRange(part);
    }
}
