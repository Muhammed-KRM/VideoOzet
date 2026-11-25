using VideoOzet.Business.DTOs.Video;

namespace VideoOzet.Business.Interfaces;

public interface ITranscriptQualityEvaluator
{
    TranscriptQualityReport Evaluate(string text, int? durationSeconds, string? expectedLang, bool isManualCaption);
}
