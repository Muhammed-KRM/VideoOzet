using System.Collections.Generic;

namespace VideoOzet.Business.Interfaces;

public interface ITextChunker
{
    List<string> ChunkText(string text, int maxTokens = 500);
}
