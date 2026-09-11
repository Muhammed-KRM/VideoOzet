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
        
        var possiblePaths = new[]
        {
            @"D:\Hoca\ffmpeg\bin",
            @"D:\Hoca\VideoOzet\ffmpeg\bin",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(Path.Combine(path, "ffmpeg.exe")))
            {
                GlobalFFOptions.Configure(new FFOptions { BinaryFolder = path });
                _logger.LogInformation("Configured FFmpeg BinaryFolder to {Path}", path);
                break;
            }
        }
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
