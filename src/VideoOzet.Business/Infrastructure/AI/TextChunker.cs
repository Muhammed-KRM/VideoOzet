using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using VideoOzet.Business.Interfaces;

namespace VideoOzet.Business.Infrastructure.AI;

public class TextChunker : ITextChunker
{
    // Basit bir kelime bazlı chunker.
    // İleride tiktoken vb. ile token bazlı yapılabilir.
    public List<string> ChunkText(string text, int maxTokens = 500)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        // Kabaca kelimelere böl (Türkçe karakterleri ve noktalamaları koruyarak)
        var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        var currentChunk = new List<string>();
        int currentLength = 0;

        foreach (var word in words)
        {
            // Ortalama 1 kelime = 1.3 token varsayımı
            currentChunk.Add(word);
            currentLength++;

            if (currentLength >= maxTokens)
            {
                chunks.Add(string.Join(" ", currentChunk));
                currentChunk.Clear();
                currentLength = 0;
            }
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join(" ", currentChunk));
        }

        return chunks;
    }
}
