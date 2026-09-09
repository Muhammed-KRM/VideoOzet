using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VideoOzet.Business.Interfaces;

public interface ISttProvider
{
    /// <summary>
    /// Converts an audio file stream to text using a Speech-to-Text service.
    /// </summary>
    /// <param name="audioStream">Stream of the audio file.</param>
    /// <param name="fileName">Name of the audio file (e.g. video123.mp3)</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Transcribed text.</returns>
    Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default);
}
