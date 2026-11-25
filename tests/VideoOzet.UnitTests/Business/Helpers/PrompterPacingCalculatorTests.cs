using System.Linq;
using FluentAssertions;
using VideoOzet.Business.Helpers;
using Xunit;

namespace VideoOzet.UnitTests.Business.Helpers;

public class PrompterPacingCalculatorTests
{
    [Theory]
    [InlineData(1, 90, 105, 115)]
    [InlineData(2, 180, 210, 230)]
    [InlineData(3, 270, 315, 345)]
    [InlineData(12, 1080, 1260, 1380)]
    [InlineData(15, 1350, 1575, 1725)]
    [InlineData(20, 1800, 2100, 2300)]
    public void CalculateBudget_ShouldScaleByTurkishStudioPacing_WithoutArtificiallyInflatedFloor(
        int minutes,
        int expectedMin,
        int expectedTarget,
        int expectedMax)
    {
        var budget = PrompterPacingCalculator.CalculateBudget(minutes);

        budget.Minutes.Should().Be(minutes);
        budget.MinWords.Should().Be(expectedMin);
        budget.TargetWords.Should().Be(expectedTarget);
        budget.MaxWords.Should().Be(expectedMax);
        budget.ParagraphGuidance.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(6, 4)]
    [InlineData(10, 4)]
    [InlineData(12, 5)]
    [InlineData(15, 5)]
    [InlineData(20, 7)]
    [InlineData(30, 9)]
    public void CalculateTargetSceneCount_ShouldMatchTargetDuration(int targetMinutes, int expectedScenes)
    {
        var scenes = PrompterPacingCalculator.CalculateTargetSceneCount(targetMinutes);

        scenes.Should().Be(expectedScenes);
    }

    [Fact]
    public void CountPrompterWords_ShouldCountOnlyQuoteLines_AndIgnoreStageDirectionsAndEvidenceTags()
    {
        const string markdown = """
            # Bölüm 1: Özgür İrade
            **Hedef Süre:** 12 Dakika

            ## Sahne 1: Açılış
            **Zaman Aralığı:** 00:00 - 02:00

            ### 1. [Görsel & Edit Yönergesi]
            - **Kamera Açısı & Kadraj:** Geniş açı stüdyo çekimi.
            - **Görsel, B-Roll ve Kurgu:** Arşiv görüntüleri ve animasyon.

            ### 2. [Prompter Metni (Aynen Okunacak)]
            > "Bugün insan zihninin karar alma mekanizmasını inceliyoruz." [K1]
            >
            > "İlk bakışta her seçimi özgürce yaptığımızı düşünürüz." [DURAKLAMA]
            """;

        var wordCount = PrompterPacingCalculator.CountPrompterWords(markdown);

        // "Bugün insan zihninin karar alma mekanizmasını inceliyoruz." -> 7
        // "İlk bakışta her seçimi özgürce yaptığımızı düşünürüz." -> 7
        wordCount.Should().Be(14);
    }

    [Fact]
    public void IsOverBudget_ShouldAllowSmallTolerance_AndTriggerAboveFifteenPercentOvershoot()
    {
        var budget = PrompterPacingCalculator.CalculateBudget(12); // MaxWords = 1380, %15 tavan = 1587

        PrompterPacingCalculator.IsOverBudget(1380, budget).Should().BeFalse();
        PrompterPacingCalculator.IsOverBudget(1587, budget).Should().BeFalse();
        PrompterPacingCalculator.IsOverBudget(1588, budget).Should().BeTrue();
        PrompterPacingCalculator.IsOverBudget(2500, budget).Should().BeTrue();
    }

    [Theory]
    [InlineData("12 Dakika", 12)]
    [InlineData("10-12 dk", 11)]
    [InlineData("~15 Dakika (Detaylı)", 15)]
    [InlineData("kısa", 6)]
    [InlineData("orta", 10)]
    [InlineData("uzun", 16)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void TryParseTargetMinutes_ShouldParseNumericRangesAndLabels(string input, int? expected)
    {
        PrompterPacingCalculator.TryParseTargetMinutes(input).Should().Be(expected);
    }

    [Fact]
    public void SavedVideoPlanMetadataExtractor_ShouldEstimateTwelveMinutes_From1260PrompterWords()
    {
        var words1260 = string.Join(" ", Enumerable.Repeat("kelime", 1260));
        var markdown = $"""
            # Test Video Planı

            ## Sahne 1: İçerik
            ### 2. [Prompter Metni (Aynen Okunacak)]
            > {words1260}
            """;

        var metadata = SavedVideoPlanMetadataExtractor.Extract(markdown);

        metadata.Should().NotBeNull();
        metadata!.HedefSureDk.Should().Be(12);
    }
}
