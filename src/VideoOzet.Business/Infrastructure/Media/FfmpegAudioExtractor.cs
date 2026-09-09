using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;
using Microsoft.Extensions.Logging;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.Media;

public class FfmpegAudioExtractor : IAudioExtractor
{
    private readonly ILogger<FfmpegAudioExtractor> _logger;

    public FfmpegAudioExtractor(ILogger<FfmpegAudioExtractor> logger)
    {
        _logger = logger;
        // Optionally, configure FFMpegCore here if ffmpeg is in a specific custom path.
        // GlobalFFOptions.Configure(new FFOptions { BinaryFolder = "/usr/bin" });
    }

    public async Task<bool> ExtractAudioAsync(string videoFilePath, string outputAudioPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting audio extraction from {VideoFilePath} to {OutputAudioPath}", videoFilePath, outputAudioPath);

            var result = await FFMpegArguments
                .FromFileInput(videoFilePath)
                .OutputToFile(outputAudioPath, false, options => options
                    .WithAudioCodec(AudioCodec.LibMp3Lame)
                    .WithAudioBitrate(128)
                    .WithCustomArgument("-vn")) // -vn: no video
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously();

            if (result)
            {
                _logger.LogInformation("Successfully extracted audio to {OutputAudioPath}", outputAudioPath);
                return true;
            }

            _logger.LogWarning("FFmpeg process returned false for {VideoFilePath}", videoFilePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract audio from video {VideoFilePath}", videoFilePath);
            return false;
        }
    }
}
