using Dms.Documents.Application;
using Shouldly;

namespace Dms.Documents.UnitTests;

public sealed class ImportPathSafetyTests
{
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("foo/../../bar")]
    [InlineData("foo/%2e%2e/bar")]
    [InlineData("foo\0bar")]
    [InlineData("s3://bucket/key")]
    public void Rejects_traversal_and_absolute_paths(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), "dms-import-root");
        Directory.CreateDirectory(root);

        var result = ImportPathSafety.ResolveSafePath(root, relative);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBeOneOf("import.path_traversal", "import.path_required");
    }

    [Fact]
    public void Resolves_safe_relative_paths_under_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dms-import-{Guid.NewGuid():N}");
        var nested = Path.Combine(root, "contracts", "2023");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "a.pdf");
        File.WriteAllText(file, "x");

        var result = ImportPathSafety.ResolveSafePath(root, "contracts/2023/a.pdf");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Path.GetFullPath(file));
    }
}

public sealed class ManifestSerializerTests
{
    [Fact]
    public void Parses_schema_v1_and_v2()
    {
        var v1 = ManifestSerializer.Parse("""
            {"schemaVersion":1,"source":"legacy","entries":[{"title":"A","categoryPath":"X","documentTypeCode":"GENERAL","file":"a.pdf"}]}
            """);
        v1.IsSuccess.ShouldBeTrue();
        v1.Value.Entries[0].ResolveSourceId("legacy").ShouldNotBeNullOrWhiteSpace();

        var v2 = ManifestSerializer.Parse("""
            {"schemaVersion":2,"source":"legacy","entries":[{"sourceId":"LEG-1","title":"A","categoryPath":"X","documentTypeCode":"GENERAL","path":"a.pdf","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}
            """);
        v2.IsSuccess.ShouldBeTrue();
        v2.Value.Entries[0].ResolveSourceId("legacy").ShouldBe("LEG-1");
    }

    [Fact]
    public void Rejects_malformed_and_empty_manifests()
    {
        ManifestSerializer.Parse("{").IsFailure.ShouldBeTrue();
        ManifestSerializer.Parse("""{"schemaVersion":9,"entries":[{"title":"A"}]}""").IsFailure.ShouldBeTrue();
        ManifestSerializer.Parse("""{"schemaVersion":1,"entries":[]}""").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Hash_is_deterministic()
    {
        var json = """{"schemaVersion":1,"entries":[{"title":"A","categoryPath":"X","documentTypeCode":"G","file":"a.pdf"}]}""";
        ManifestSerializer.Hash(json).ShouldBe(ManifestSerializer.Hash(json));
        ManifestSerializer.Hash(json).Length.ShouldBe(64);
    }
}
