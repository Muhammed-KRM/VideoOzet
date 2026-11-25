using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VideoOzet.Business.DTOs.Video;

namespace VideoOzet.Business.Interfaces;

public interface IRemoteMediaService
{
    Task<RemoteVideoMetadataDto> GetMetadataAsync(string url, CancellationToken ct = default);
    Task<IReadOnlyList<RemoteVideoMetadataDto>> ExpandPlaylistOrManifestAsync(string url, string? bearerToken, CancellationToken ct = default);
    Task<RemoteCaptionResult?> TryFetchCaptionsAsync(string url, string preferredLang, CancellationToken ct = default);
    Task<RemoteCaptionResult?> TryFetchTranscriptFromUrlAsync(string transcriptUrl, string? bearerToken, string preferredLang, CancellationToken ct = default);
    Task<string> DownloadAudioAsync(string url, string outputDir, string? bearerToken, Action<int, string>? onProgress, CancellationToken ct = default);
    bool IsYtDlpAvailable();
}
