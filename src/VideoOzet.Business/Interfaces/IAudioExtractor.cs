using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface IAudioExtractor
{
    /// <summary>
    /// Extracts audio from a video file and saves it to a specified output path.
    /// </summary>
    /// <param name="videoFilePath">The local path to the input video file.</param>
    /// <param name="outputAudioPath">The local path where the extracted audio (e.g. .mp3) will be saved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if extraction is successful, otherwise false.</returns>
    Task<bool> ExtractAudioAsync(string videoFilePath, string outputAudioPath, CancellationToken cancellationToken = default);
}
