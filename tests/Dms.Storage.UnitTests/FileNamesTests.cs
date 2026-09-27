using Dms.Storage.Application;
using Shouldly;

namespace Dms.Storage.UnitTests;

public sealed class FileNamesTests
{
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\secrets\\x.pdf", "x.pdf")]
    [InlineData("/var/lib/dms/objects/../escape.bin", "escape.bin")]
    [InlineData("..\\..\\windows\\system32\\cmd.exe", "cmd.exe")]
    public void Path_segments_are_stripped_to_the_leaf(string input, string expected) =>
        FileNames.Sanitize(input).ShouldBe(expected);

    [Fact]
    public void Null_and_blank_become_unnamed()
    {
        FileNames.Sanitize(null).ShouldBe("unnamed");
        FileNames.Sanitize("   ").ShouldBe("unnamed");
        FileNames.Sanitize("...").ShouldBe("unnamed");
    }

    [Fact]
    public void Bidi_overrides_are_stripped_but_persian_zwnj_stays()
    {
        // U+202E RIGHT-TO-LEFT OVERRIDE would flip "gpj.exe" visually.
        var sneaky = "gpj\u202Eexe";
        FileNames.Sanitize(sneaky).ShouldBe("gpjexe");

        FileNames.Sanitize("می\u200Cشود.pdf").ShouldContain("\u200C");
    }

    [Fact]
    public void Object_keys_never_include_user_input()
    {
        var key = FileNames.BuildObjectKey("objects", Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
        key.ShouldBe("objects/2026/09/11111111111111111111111111111111");
        key.ShouldNotContain("..");
    }
}

public sealed class FileSystemPathGuardTests
{
    [Fact]
    public void ResolvePath_refuses_keys_that_escape_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dms_fs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var storage = new Dms.Storage.Infrastructure.Providers.FileSystemFileStorage(
                Microsoft.Extensions.Options.Options.Create(new StorageOptions
                {
                    FileSystem = new FileSystemOptions { RootPath = root },
                }));

            var evil = new ObjectLocation("bucket", "../../outside.txt");
            Should.Throw<InvalidOperationException>(() =>
                storage.ExistsAsync(evil, CancellationToken.None).GetAwaiter().GetResult());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
