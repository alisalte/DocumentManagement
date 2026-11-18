using Dms.Application;
using Dms.Storage.Application;
using Dms.Storage.Contracts;
using Dms.Storage.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Dms.Storage.UnitTests;

public sealed class ScanPolicyTests
{
    [Fact]
    public async Task Scanning_is_off_when_no_scanner_is_configured()
    {
        var policy = new ScanPolicy(new FakeScanner(enabled: false), new FakeSettings());
        (await policy.IsScanningOnAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Scanning_follows_the_administrator_switch()
    {
        var settings = new FakeSettings();
        var policy = new ScanPolicy(new FakeScanner(enabled: true), settings);
        (await policy.IsScanningOnAsync(CancellationToken.None)).ShouldBeTrue();

        settings.Add(new StorageSetting(StorageSetting.MalwareScanning, ScanPolicy.Off, DateTimeOffset.UtcNow));
        (await policy.IsScanningOnAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Switching_off_releases_waiting_files_and_queues_their_processing()
    {
        var settings = new FakeSettings();
        settings.Add(new StorageSetting(StorageSetting.MalwareScanning, ScanPolicy.Off, DateTimeOffset.UtcNow));
        var waiting = Stage(ScanStatus.Pending);
        var objects = new FakeObjects([waiting]);
        var jobs = new FakeJobs();
        var job = new ReleaseScanWaitingJob(
            objects,
            new ScanPolicy(new FakeScanner(enabled: true), settings),
            jobs,
            TimeProvider.System,
            NullLogger<ReleaseScanWaitingJob>.Instance);

        await job.HandleAsync("{}", CancellationToken.None);

        waiting.ScanStatus.ShouldBe(ScanStatus.Skipped);
        waiting.ToInfo().IsContentAvailable.ShouldBeTrue();
        jobs.Queued.ShouldContain(request => request.Type == ProcessObjectJob.Type);
    }

    [Fact]
    public async Task Nothing_is_released_while_the_scan_is_on()
    {
        var waiting = Stage(ScanStatus.Pending);
        var job = new ReleaseScanWaitingJob(
            new FakeObjects([waiting]),
            new ScanPolicy(new FakeScanner(enabled: true), new FakeSettings()),
            new FakeJobs(),
            TimeProvider.System,
            NullLogger<ReleaseScanWaitingJob>.Instance);

        await job.HandleAsync("{}", CancellationToken.None);

        waiting.ScanStatus.ShouldBe(ScanStatus.Pending);
    }

    private static StorageObject Stage(ScanStatus status)
    {
        var file = StorageObject.Stage(
            StorageObjectId.New(), "filesystem", "bucket", $"objects/{Guid.NewGuid():N}", StorageObjectPurpose.Original,
            "old.pdf", "application/pdf", null, 10, new byte[32], status, null, DateTimeOffset.UtcNow);
        file.Commit(DateTimeOffset.UtcNow);
        return file;
    }

    private sealed class FakeScanner(bool enabled) : IMalwareScanner
    {
        public bool IsEnabled => enabled;

        public Task<ScanVerdict> ScanAsync(ObjectLocation location, CancellationToken cancellationToken) =>
            Task.FromResult(ScanVerdict.Clean);
    }

    private sealed class FakeSettings : IStorageSettingRepository
    {
        private readonly Dictionary<string, StorageSetting> items = [];

        public Task<StorageSetting?> FindAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(items.GetValueOrDefault(key));

        public void Add(StorageSetting setting) => items[setting.Key] = setting;
    }

    private sealed class FakeJobs : IJobQueue
    {
        public List<JobRequest> Queued { get; } = [];

        public Task EnqueueAsync(JobRequest request, CancellationToken cancellationToken)
        {
            Queued.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeObjects(List<StorageObject> items) : IStorageObjectRepository
    {
        public Task<IReadOnlyList<StorageObject>> ListWaitingForScanAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StorageObject>>(items
                .Where(item => item.ScanStatus is ScanStatus.Pending or ScanStatus.Failed)
                .Take(limit)
                .ToList());

        public Task<int> CountWaitingForScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(items.Count(item => item.ScanStatus is ScanStatus.Pending or ScanStatus.Failed));

        public Task<StorageObject?> FindAsync(StorageObjectId id, CancellationToken cancellationToken) =>
            Task.FromResult(items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<StorageObject>> FindManyAsync(IReadOnlyCollection<StorageObjectId> ids, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StorageObject>> FindByHashAsync(byte[] sha256, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StorageObject>> ListStagedBeforeAsync(DateTimeOffset cutoff, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StorageObject>> ListPendingDeletionAsync(int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StorageObject>> ListDerivedFromAsync(StorageObjectId source, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<long> SumLiveOriginalBytesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StorageObjectId>> ListAvailableOriginalsAsync(StorageObjectId? after, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(StorageObject storageObject) => items.Add(storageObject);
    }
}
