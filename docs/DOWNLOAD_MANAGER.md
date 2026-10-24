# Download manager

Canonical document for HTTPS downloads, retries, and checksum verification. Plugins receive it as `HostInfrastructure.Downloads` ([`PROVIDER_SYSTEM.md`](PROVIDER_SYSTEM.md#host-services-for-plugins)). Security rules for downloads are summarized in [`SECURITY.md`](SECURITY.md#download-integrity).

## Location

- Interface: `Penelope.Abstractions.IDownloadManager`
- Implementation: `Penelope.Infrastructure.Downloads.DownloadManager`

## Responsibilities

- HTTPS GET with manual redirect following (max 8 hops, HTTPS only)
- Retries on transient HTTP and network errors (`MaxAttempts` on `DownloadRequest`)
- Streaming write to disk with progress via `IJobLog.Progress`
- Optional size check against `Content-Length` or `ExpectedSizeBytes`
- Optional SHA-256 verification (`ExpectedSha256Hex`)

## Security

- Redirects must stay on `https://`
- User-Agent: `Penelope`
- Checksums should be verified before executing installers (Ollama uses published `sha256sum.txt` and/or GitHub asset digest)
- Download directory default: `%LOCALAPPDATA%\Penelope\downloads`

## Checksum flow (Ollama)

1. Read GitHub release JSON and optional `sha256sum.txt`.
2. `OllamaRelease.TryAgree` reconciles asset digest and checksum file.
3. `DownloadFileAsync` writes `OllamaSetup.exe` and hashes the file.
4. Mismatch returns a failure card; file may be kept for inspection.

## Retry logic

- Per-request attempt loop in `GetStringAsync` and `DownloadFileAsync`
- Failed partial files are deleted before the next attempt
- Non-network failures (checksum mismatch) do not retry automatically

## Progress and events

- Progress goes to the calling job through `IJobLog.Progress`. The job record raises `download.progress` on the event bus for each update.
- `download.started`, `download.completed`, and `download.failed` are reserved names in [`EVENT_BUS.md`](EVENT_BUS.md#download-events). `DownloadManager` does not publish them yet; when it does, it will need `IEventBus` injected.

## Usage

```csharp
await downloads.DownloadFileAsync(
    new DownloadRequest(url, destinationPath, expectedSize, expectedSha256, maxAttempts: 3, progressLabel: "Downloading"),
    log,
    cancellationToken);
```

Ollama installer code lives in `GitHubOllamaSetup` (`Penelope.Modules.Ollama`).
