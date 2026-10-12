using Penelope.Modules.Ollama;
using Xunit;

namespace Penelope.Host.Tests;

public class OllamaReleaseTests
{
    [Fact]
    public void The_release_json_selects_the_windows_installer_and_checksum()
    {
        const string json = """
            {
              "tag_name": "v0.36.0",
              "assets": [
                { "name": "ollama-linux-amd64.tgz", "browser_download_url": "https://example.test/linux.tgz", "size": 10 },
                { "name": "OllamaSetup.exe", "browser_download_url": "https://github.com/ollama/ollama/releases/download/v0.36.0/OllamaSetup.exe", "size": 42, "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                { "name": "sha256sum.txt", "browser_download_url": "http://example.test/sha256sum.txt" },
                { "name": "notes.txt", "browser_download_url": "javascript:alert(1)" }
              ]
            }
            """;

        var release = OllamaRelease.ParseLatest(json);

        Assert.NotNull(release);
        Assert.Equal("0.36.0", release.Version);
        Assert.Equal("https://github.com/ollama/ollama/releases/download/v0.36.0/OllamaSetup.exe", release.InstallerUrl);
        Assert.Null(release.ChecksumUrl);
        Assert.Equal(42, release.SizeBytes);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", release.AssetDigest);
    }

    [Fact]
    public void The_current_checksum_file_names_the_installer_with_a_directory_prefix()
    {
        const string text = "ce6f8b671a699bf1ebafb28d60dbbabf4f37b30b499145fa308a8ba72de021d6  ./ollama-linux-amd64.tar.zst\r\nab81db6c6f4f65547242d1b2912e854da660ab3bc1dbd5ae3483831cf7668c80  ./OllamaSetup.exe\r\n";

        Assert.Equal(
            "ab81db6c6f4f65547242d1b2912e854da660ab3bc1dbd5ae3483831cf7668c80",
            OllamaRelease.ChecksumForInstaller(text));
        Assert.Equal(
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            OllamaRelease.ChecksumForInstaller("SHA256 (./OllamaSetup.exe) = bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
    }

    [Fact]
    public void Disagreeing_published_checksums_are_rejected()
    {
        var agreed = OllamaRelease.TryAgree(
            "sha256:ab81db6c6f4f65547242d1b2912e854da660ab3bc1dbd5ae3483831cf7668c80",
            "ab81db6c6f4f65547242d1b2912e854da660ab3bc1dbd5ae3483831cf7668c80  ./OllamaSetup.exe",
            out var expected,
            out var disagreement);

        Assert.True(agreed);
        Assert.Equal("ab81db6c6f4f65547242d1b2912e854da660ab3bc1dbd5ae3483831cf7668c80", expected);
        Assert.Null(disagreement);

        var conflict = OllamaRelease.TryAgree(
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  ./OllamaSetup.exe",
            out var rejected,
            out var why);

        Assert.False(conflict);
        Assert.Null(rejected);
        Assert.Contains("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", why);
        Assert.Contains("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", why);
    }

    [Fact]
    public void The_checksum_file_matches_only_the_windows_installer()
    {
        const string text = """
            aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  ollama-linux-amd64.tgz
            bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb *OllamaSetup.exe
            """;

        Assert.Equal(
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            OllamaRelease.ChecksumForInstaller(text));
    }

    [Fact]
    public void A_matching_hash_is_accepted_and_a_newer_version_is_detected()
    {
        var bytes = "penelope"u8.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

        Assert.True(OllamaRelease.SameHash(hash, System.Security.Cryptography.SHA256.HashData(bytes)));
        Assert.False(OllamaRelease.SameHash(hash, [1, 2, 3]));
        Assert.True(OllamaRelease.IsNewer("v0.36.0", "0.35.1"));
        Assert.False(OllamaRelease.IsNewer("0.35.1", "v0.35.1"));
        Assert.False(OllamaRelease.IsNewer("0.35.0", "0.35.1"));
    }

    [Fact]
    public void The_installer_uses_the_per_user_silent_flags()
    {
        Assert.Equal("/VERYSILENT /NORESTART /SUPPRESSMSGBOXES", OllamaRelease.InstallArguments);
        Assert.Equal(SetupProblem.None, OllamaRelease.FromInstallerExit(0));
        Assert.Equal(SetupProblem.Cancelled, OllamaRelease.FromInstallerExit(2));
        Assert.Equal(SetupProblem.Cancelled, OllamaRelease.FromInstallerExit(5));
        Assert.Equal(SetupProblem.Failed, OllamaRelease.FromInstallerExit(1));
        Assert.Contains("Ollama.Ollama", OllamaRelease.WingetArguments, StringComparison.Ordinal);
    }
}
