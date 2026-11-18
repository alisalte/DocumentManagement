namespace Dms.Storage.Domain;

/// <summary>
/// A setting an administrator changes from the application while it runs, such as switching the
/// malware scan off. Values in appsettings are the deployment's ceiling; these only narrow them.
/// </summary>
public sealed class StorageSetting
{
    /// <summary>"off" while an administrator has switched the malware scan off.</summary>
    public const string MalwareScanning = "malware_scanning";

    private StorageSetting()
    {
    }

    public StorageSetting(string key, string value, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Value = value;
        UpdatedAt = now;
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Change(string value, DateTimeOffset now)
    {
        Value = value;
        UpdatedAt = now;
    }
}
