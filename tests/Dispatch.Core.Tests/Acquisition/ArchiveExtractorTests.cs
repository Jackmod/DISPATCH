using System.IO.Compression;
using System.Text;
using Dispatch.Core.Acquisition;
using Dispatch.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Acquisition;

/// <summary>
/// The archive extractor, against zips built in memory. These care about two
/// things above all: that a well-formed archive lands file-for-file in staging,
/// and that a malicious member path cannot escape it.
/// </summary>
public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-extract", Guid.NewGuid().ToString("N"));

    private readonly ArchiveExtractor _extractor = new(NullLogger<ArchiveExtractor>.Instance);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string Zip(params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return path;
    }

    [Fact]
    public async Task Extracts_every_file_preserving_structure()
    {
        var zip = Zip(
            ("readme.txt", "hello"),
            ("plugins/mod.dll", "binary"),
            ("plugins/lspdfr/config.xml", "<x/>"));

        var dest = Path.Combine(_root, "out");
        var written = await _extractor.ExtractAsync(zip, dest);

        written.Should().Be(3);
        File.ReadAllText(Path.Combine(dest, "readme.txt")).Should().Be("hello");
        File.Exists(Path.Combine(dest, "plugins", "mod.dll")).Should().BeTrue();
        File.ReadAllText(Path.Combine(dest, "plugins", "lspdfr", "config.xml")).Should().Be("<x/>");
    }

    [Fact]
    public async Task Directory_entries_are_skipped_not_written_as_files()
    {
        // A trailing-slash entry is a folder marker with no content.
        var zip = Zip(
            ("scripts/", string.Empty),
            ("scripts/plugin.dll", "code"));

        var dest = Path.Combine(_root, "out");
        var written = await _extractor.ExtractAsync(zip, dest);

        written.Should().Be(1);
        File.Exists(Path.Combine(dest, "scripts", "plugin.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task A_member_escaping_the_target_is_refused()
    {
        // The classic zip-slip: a member whose path climbs out of the target.
        var zip = Zip(("../../escaped.txt", "pwned"));
        var dest = Path.Combine(_root, "out");

        var act = () => _extractor.ExtractAsync(zip, dest);

        await act.Should().ThrowAsync<UnsafeArchivePathException>();

        // And nothing was written outside the target.
        File.Exists(Path.Combine(_root, "escaped.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task A_loose_non_archive_file_is_copied_through()
    {
        Directory.CreateDirectory(_root);
        var loose = Path.Combine(_root, "RAGENativeUI.dll");
        File.WriteAllText(loose, "not an archive");

        var dest = Path.Combine(_root, "out");
        var written = await _extractor.ExtractAsync(loose, dest);

        written.Should().Be(1);
        File.ReadAllText(Path.Combine(dest, "RAGENativeUI.dll")).Should().Be("not an archive");
    }

    [Fact]
    public void Format_is_detected_from_the_signature_not_the_extension()
    {
        // A real zip renamed to .7z is still read as a zip: the magic bytes win.
        var zip = Zip(("a.txt", "x"));
        var misnamed = Path.ChangeExtension(zip, ".7z");
        File.Move(zip, misnamed);

        ArchiveExtractor.DetectFormat(misnamed).Should().Be(ArchiveFormat.Zip);
    }

    [Fact]
    public void A_bare_dll_is_detected_as_loose()
    {
        Directory.CreateDirectory(_root);
        var dll = Path.Combine(_root, "x.dll");
        File.WriteAllText(dll, "MZ-ish but not an archive");

        ArchiveExtractor.DetectFormat(dll).Should().Be(ArchiveFormat.Loose);
    }
}
