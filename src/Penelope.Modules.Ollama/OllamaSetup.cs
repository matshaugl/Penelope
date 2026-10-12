using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Penelope.Modules.Ollama;

internal enum SetupProblem
{
    None,
    Unreachable,
    Checksum,
    Disk,
    Cancelled,
    Failed,
    Locked,
}

internal sealed record SetupRelease(string Version, string InstallerUrl, string? ChecksumUrl, long? SizeBytes, string? AssetDigest);

internal sealed record SetupOutcome(SetupProblem Problem, string? Version, string? Detail = null);

internal static class OllamaRelease
{
    public const string InstallArguments = "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES";

    public const string WingetArguments = "install --exact --id Ollama.Ollama --accept-package-agreements --accept-source-agreements --disable-interactivity";

    public const string DownloadPage = "https://ollama.com/download";

    public const string ReleasesApi = "https://api.github.com/repos/ollama/ollama/releases/latest";

    public static SetupRelease? ParseLatest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tag))
        {
            return null;
        }

        var version = tag.GetString()?.Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        string? installer = null;
        string? checksum = null;
        string? digest = null;
        long? size = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
                if (name is null || url is null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.Equals("OllamaSetup.exe", StringComparison.OrdinalIgnoreCase))
                {
                    installer = url;
                    digest = asset.TryGetProperty("digest", out var digestElement) ? DigestHash(digestElement.GetString()) : null;
                    if (asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes))
                    {
                        size = bytes;
                    }
                }
                else if (name.Equals("sha256sum.txt", StringComparison.OrdinalIgnoreCase))
                {
                    checksum = url;
                }
            }
        }

        return installer is null ? null : new SetupRelease(version, installer, checksum, size, digest);
    }

    public static string? DigestHash(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var value = digest.Trim();
        const string prefix = "sha256:";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[prefix.Length..].Trim();
        }

        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
    }

    public static string? ChecksumForInstaller(string sha256Text)
    {
        var text = sha256Text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (TryCoreutils(line, out var hash, out var name) && IsInstallerFile(name))
            {
                return hash.ToLowerInvariant();
            }

            if (TryBsd(line, out hash, out name) && IsInstallerFile(name))
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }

    public static bool TryAgree(string? assetDigest, string? checksumText, out string? expected, out string? disagreement)
    {
        var fromAsset = DigestHash(assetDigest);
        var fromFile = checksumText is null ? null : ChecksumForInstaller(checksumText);
        if (fromAsset is not null && fromFile is not null && !fromAsset.Equals(fromFile, StringComparison.OrdinalIgnoreCase))
        {
            expected = null;
            disagreement = $"GitHub lists {fromAsset} for OllamaSetup.exe. The checksum file lists {fromFile}.";
            return false;
        }

        expected = fromAsset ?? fromFile;
        disagreement = null;
        return true;
    }

    private static bool TryCoreutils(string line, out string hash, out string name)
    {
        hash = "";
        name = "";
        var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit))
        {
            return false;
        }

        hash = parts[0];
        name = parts[1].Trim();
        return true;
    }

    private static bool TryBsd(string line, out string hash, out string name)
    {
        hash = "";
        name = "";
        const string marker = "SHA256";
        if (!line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var open = line.IndexOf('(');
        var close = line.IndexOf(')');
        var equals = line.IndexOf('=', close > 0 ? close : 0);
        if (open < 0 || close <= open || equals < 0)
        {
            return false;
        }

        name = line[(open + 1)..close].Trim();
        hash = line[(equals + 1)..].Trim();
        return hash.Length == 64 && hash.All(Uri.IsHexDigit);
    }

    private static bool IsInstallerFile(string name)
    {
        var file = name.Trim().TrimStart('*').Replace('\\', '/');
        var slash = file.LastIndexOf('/');
        if (slash >= 0)
        {
            file = file[(slash + 1)..];
        }

        return file.Equals("OllamaSetup.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SameHash(string expectedHex, byte[] actual)
    {
        try
        {
            var expected = Convert.FromHexString(expectedHex);
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsNewer(string latest, string installed)
    {
        if (!Version.TryParse(Normalize(latest), out var left) || !Version.TryParse(Normalize(installed), out var right))
        {
            return !string.Equals(Normalize(latest), Normalize(installed), StringComparison.OrdinalIgnoreCase);
        }

        return left > right;
    }

    public static SetupProblem FromInstallerExit(int code)
    {
        return code switch
        {
            0 => SetupProblem.None,
            2 or 5 => SetupProblem.Cancelled,
            _ => SetupProblem.Failed,
        };
    }

    public static string Normalize(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }
}

internal abstract class OllamaSetup
{
    public virtual Task<string?> LatestVersionAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }

    public abstract Task<SetupOutcome> InstallAsync(Penelope.Abstractions.IJobLog log, CancellationToken cancellationToken);
}

internal sealed class OfflineOllamaSetup : OllamaSetup
{
    public override Task<SetupOutcome> InstallAsync(Penelope.Abstractions.IJobLog log, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SetupOutcome(SetupProblem.Unreachable, null));
    }
}

internal sealed class GitHubOllamaSetup : OllamaSetup
{
    private const int Attempts = 3;

    private readonly HttpClient _http;
    private readonly string _folder;

    public GitHubOllamaSetup()
        : this(handler: null, downloadFolder: null)
    {
    }

    internal GitHubOllamaSetup(HttpMessageHandler? handler, string? downloadFolder)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: handler is null)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Penelope");
        _folder = downloadFolder ?? DownloadFolder();
    }

    public override async Task<string?> LatestVersionAsync(CancellationToken cancellationToken)
    {
        return (await FindAsync(log: null, cancellationToken))?.Version;
    }

    public override async Task<SetupOutcome> InstallAsync(Penelope.Abstractions.IJobLog log, CancellationToken cancellationToken)
    {
        var ready = await DownloadAndVerifyAsync(log, cancellationToken);
        if (ready.Problem != SetupProblem.None)
        {
            return ready;
        }

        var file = InstallerPath();
        log.AllowCancel(false);
        log.Progress(0, 1, "Installing Ollama");
        log.Info("Windows may ask for permission.");
        var problem = await RunInstaller(file, cancellationToken);
        if (problem == SetupProblem.None)
        {
            TryDelete(file);
        }

        return new SetupOutcome(problem, problem == SetupProblem.None ? ready.Version : null, ready.Detail);
    }

    internal async Task<SetupOutcome> DownloadAndVerifyAsync(Penelope.Abstractions.IJobLog log, CancellationToken cancellationToken)
    {
        SetupRelease? release;
        try
        {
            release = await FindAsync(log, cancellationToken);
        }
        catch (Exception ex) when (IsNetwork(ex) && !cancellationToken.IsCancellationRequested)
        {
            return await TryWinget(log, cancellationToken);
        }

        if (release is null)
        {
            return new SetupOutcome(SetupProblem.Unreachable, null, "Penelope couldn't find the Windows installer in the Ollama release.");
        }

        Directory.CreateDirectory(_folder);
        var needed = release.SizeBytes is long size && size > 0 ? size + (512L * 1024 * 1024) : 2L * 1024 * 1024 * 1024;
        var root = Path.GetPathRoot(_folder);
        if (root is not null)
        {
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.AvailableFreeSpace < needed)
            {
                log.Error("There is not enough free space for the Ollama installer.");
                return new SetupOutcome(SetupProblem.Disk, null);
            }
        }

        string? checksumText = null;
        if (release.ChecksumUrl is not null)
        {
            log.Info($"Checksum URL {release.ChecksumUrl}");
            try
            {
                checksumText = await ReadText(release.ChecksumUrl, log, cancellationToken);
            }
            catch (Exception ex) when (IsNetwork(ex) && !cancellationToken.IsCancellationRequested)
            {
                return new SetupOutcome(SetupProblem.Unreachable, null, "Penelope couldn't read the published checksum.");
            }
        }

        if (!OllamaRelease.TryAgree(release.AssetDigest is null ? null : "sha256:" + release.AssetDigest, checksumText, out var expected, out var disagreement))
        {
            log.Error(disagreement!);
            return new SetupOutcome(
                SetupProblem.Checksum,
                null,
                "The published checksums disagree, so Penelope did not install Ollama. " + disagreement);
        }

        if (expected is null && (release.ChecksumUrl is not null || release.AssetDigest is not null))
        {
            return new SetupOutcome(
                SetupProblem.Checksum,
                null,
                "Penelope couldn't find OllamaSetup.exe in the published checksum, so the installer was not started.");
        }

        if (expected is null)
        {
            log.Info("This release did not publish a checksum. Penelope will run the official installer.");
        }

        var file = InstallerPath();
        log.Info($"Download URL {release.InstallerUrl}");
        string? incomplete = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                log.Progress(0, release.SizeBytes ?? 0, "Downloading Ollama");
                await DownloadFile(release.InstallerUrl, file, release.SizeBytes, log, cancellationToken);
                var length = new FileInfo(file).Length;
                log.Info($"File size {length}");
                if (release.SizeBytes is long expectedSize && length != expectedSize)
                {
                    throw new IOException($"Download stopped at {length} of {expectedSize} bytes.");
                }

                if (expected is null)
                {
                    return new SetupOutcome(SetupProblem.None, release.Version);
                }

                var actual = await HashFile(file, cancellationToken);
                log.Info($"Expected checksum {expected}");
                log.Info($"Calculated checksum {actual}");
                if (!expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                {
                    var detail = $"The downloaded installer didn't match the published checksum. Expected {expected}. Calculated {actual}. File size {length} bytes. The file is still at {file}.";
                    log.Error(detail);
                    return new SetupOutcome(SetupProblem.Checksum, null, detail);
                }

                log.Info("The installer matches the published checksum.");
                return new SetupOutcome(SetupProblem.None, release.Version);
            }
            catch (OperationCanceledException)
            {
                TryDelete(file);
                throw;
            }
            catch (Exception ex) when (IsNetwork(ex) && !cancellationToken.IsCancellationRequested)
            {
                incomplete = ex.Message;
                log.Warning($"Download attempt {attempt} of {Attempts} didn't finish. {ex.Message}");
                if (attempt == Attempts)
                {
                    break;
                }

                log.Info("Penelope will try the download again.");
            }
        }

        var kept = File.Exists(file) ? $" The file is still at {file}." : "";
        return new SetupOutcome(
            SetupProblem.Unreachable,
            null,
            $"The download stopped before the file was complete. {incomplete}{kept}");
    }

    private async Task<SetupRelease?> FindAsync(Penelope.Abstractions.IJobLog? log, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            using var response = await Send(OllamaRelease.ReleasesApi, log, acceptGitHub: true, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return OllamaRelease.ParseLatest(await response.Content.ReadAsStringAsync(cancellationToken));
            }

            if (attempt < Attempts && IsRetryStatus(response.StatusCode))
            {
                log?.Warning($"Download attempt {attempt} of {Attempts} returned HTTP {(int)response.StatusCode}.");
                continue;
            }

            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        }

        throw new HttpRequestException("The release lookup didn't finish.");
    }

    private async Task<string> ReadText(string url, Penelope.Abstractions.IJobLog log, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            try
            {
                using var response = await Send(url, log, acceptGitHub: false, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < Attempts && IsRetryStatus(response.StatusCode))
                    {
                        log.Warning($"Download attempt {attempt} of {Attempts} returned HTTP {(int)response.StatusCode}.");
                        continue;
                    }

                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
                }

                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (IsNetwork(ex) && attempt < Attempts && !cancellationToken.IsCancellationRequested)
            {
                log.Warning($"Download attempt {attempt} of {Attempts} didn't finish. Penelope will try again.");
            }
        }

        throw new HttpRequestException("The checksum download didn't finish.");
    }

    private async Task<long> DownloadFile(
        string url,
        string destination,
        long? expectedSize,
        Penelope.Abstractions.IJobLog log,
        CancellationToken cancellationToken)
    {
        using var response = await Send(url, log, acceptGitHub: false, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        }

        var total = response.Content.Headers.ContentLength ?? expectedSize ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long done = 0;
        long reported = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            done += read;
            if (done - reported >= 256 * 1024 || (total > 0 && done >= total))
            {
                reported = done;
                log.Progress(done, total, "Downloading Ollama");
            }
        }

        if (response.Content.Headers.ContentLength is long declared && done != declared)
        {
            throw new IOException($"Download stopped at {done} of {declared} bytes.");
        }

        return done;
    }

    private async Task<HttpResponseMessage> Send(
        string url,
        Penelope.Abstractions.IJobLog? log,
        bool acceptGitHub,
        CancellationToken cancellationToken)
    {
        var current = url;
        for (var hop = 0; hop < 8; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (acceptGitHub)
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
            }

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            log?.Info($"HTTP {(int)response.StatusCode} {current}");
            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location ?? throw new InvalidOperationException("The redirect had no destination.");
                var next = location.IsAbsoluteUri ? location : new Uri(new Uri(current), location);
                response.Dispose();
                if (!string.Equals(next.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The download redirected away from HTTPS.");
                }

                current = next.AbsoluteUri;
                log?.Info($"Redirected to {current}");
                continue;
            }

            log?.Info($"Final URL {current}");
            return response;
        }

        throw new InvalidOperationException("The download redirected too many times.");
    }

    private static async Task<string> HashFile(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string InstallerPath()
    {
        return Path.Combine(_folder, "OllamaSetup.exe");
    }

    private static bool IsRedirect(HttpStatusCode status)
    {
        return status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static bool IsRetryStatus(HttpStatusCode status)
    {
        var code = (int)status;
        return code is 408 or 429 || code >= 500;
    }

    private async Task<SetupOutcome> TryWinget(Penelope.Abstractions.IJobLog log, CancellationToken cancellationToken)
    {
        var winget = WingetPath();
        if (winget is null)
        {
            log.Error("Penelope couldn't reach the Ollama release.");
            return new SetupOutcome(SetupProblem.Unreachable, null);
        }

        log.Warning("The download didn't finish. Penelope is asking Windows to install Ollama.");
        log.AllowCancel(false);
        var code = await RunWinget(winget, log, cancellationToken);
        return new SetupOutcome(code == 0 ? SetupProblem.None : SetupProblem.Failed, null);
    }

    private string? WingetPath()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "winget.exe");
        return File.Exists(path) ? path : null;
    }

    private async Task<SetupProblem> RunInstaller(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await RunProcess(path, elevate: false, OllamaRelease.InstallArguments, log: null, cancellationToken);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
        {
            try
            {
                return await RunProcess(path, elevate: true, OllamaRelease.InstallArguments, log: null, cancellationToken);
            }
            catch (Win32Exception elevated) when (elevated.NativeErrorCode == 1223)
            {
                return SetupProblem.Cancelled;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return SetupProblem.Cancelled;
        }
    }

    private async Task<int> RunWinget(string path, Penelope.Abstractions.IJobLog log, CancellationToken cancellationToken)
    {
        var problem = await RunProcess(path, elevate: false, OllamaRelease.WingetArguments, log, cancellationToken);
        return problem == SetupProblem.None ? 0 : 1;
    }

    private static async Task<SetupProblem> RunProcess(
        string path,
        bool elevate,
        string arguments,
        Penelope.Abstractions.IJobLog? log,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments,
            UseShellExecute = log is null,
            Verb = elevate ? "runas" : null,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            RedirectStandardOutput = log is not null,
            RedirectStandardError = log is not null,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The installer did not start.");
        if (log is not null)
        {
            process.OutputDataReceived += (_, eventArgs) => Note(log, eventArgs.Data);
            process.ErrorDataReceived += (_, eventArgs) => Note(log, eventArgs.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        await process.WaitForExitAsync(cancellationToken);
        return log is null ? OllamaRelease.FromInstallerExit(process.ExitCode) : (process.ExitCode == 0 ? SetupProblem.None : SetupProblem.Failed);
    }

    private static void Note(Penelope.Abstractions.IJobLog log, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var text = line.Trim();
        if (text.Length > 200)
        {
            text = text[..200];
        }

        log.Info(text);
    }

    private static string DownloadFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Penelope",
            "downloads");
    }

    private static bool IsNetwork(Exception ex)
    {
        return ex is HttpRequestException or IOException or InvalidOperationException or JsonException or TaskCanceledException;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
