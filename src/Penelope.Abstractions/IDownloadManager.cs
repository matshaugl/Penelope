namespace Penelope.Abstractions;

public interface IDownloadManager
{
    Task<string> GetStringAsync(DownloadRequest request, IJobLog? log, CancellationToken cancellationToken);

    Task<DownloadResult> DownloadFileAsync(DownloadRequest request, IJobLog log, CancellationToken cancellationToken);
}

public sealed record DownloadRequest(
    string Url,
    string? DestinationPath = null,
    long? ExpectedSizeBytes = null,
    string? ExpectedSha256Hex = null,
    int MaxAttempts = 3,
    bool AcceptGitHubJson = false,
    string? ProgressLabel = null);

public sealed record DownloadResult(bool Succeeded, long BytesWritten, string? FinalUrl, string? Error);
