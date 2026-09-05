using Ocrx.Contracts;
using Xunit;

namespace RefineryPlugin.Tests;

public class RefineryParserYieldRowsTests
{
    private static OcrWordInfo Word(string text, double x, double y, double width = 60, double height = 20)
        => new(text, new RectF(x, y, width, height));

    private static OcrRegionResult Region(IReadOnlyList<OcrWordInfo> words, double effectiveScale = 1.0,
        uint roiWidth = 1000, uint roiHeight = 1000)
        => new(string.Join(' ', words.Select(w => w.Text)),
            [new OcrLineInfo(string.Join(' ', words.Select(w => w.Text)), words)],
            effectiveScale, RoiX: 0, RoiY: 0, roiWidth, roiHeight);

    [Fact]
    public void ExtractYieldRows_EmptyWords_ReturnsEmpty()
    {
        var result = RefineryParser.ExtractYieldRows(Region([]));
        Assert.Empty(result.Rows);
        Assert.Equal(0, result.DroppedTopEdge);
        Assert.Equal(0, result.DroppedBottomEdge);
    }

    [Fact]
    public void ExtractYieldRows_SingleTwoColumnRow_ParsesCscuInteger()
    {
        var words = new[] { Word("Titanium", 0, 100), Word("644", 70, 100) };

        var row = Assert.Single(RefineryParser.ExtractYieldRows(Region(words)).Rows);

        Assert.Equal("TITANIUM", row.Name);
        Assert.Equal(644, row.YieldCscu); // raw cSCU, no /100
        Assert.Equal(0, row.QtyCscu);     // no QTY column on the completed panel
    }

    [Fact]
    public void ExtractYieldRows_RepairsOcrDigitConfusionInYield()
    {
        var words = new[] { Word("Gold", 0, 100), Word("S,OOl", 70, 100) }; // -> 5001
        var row = Assert.Single(RefineryParser.ExtractYieldRows(Region(words)).Rows);
        Assert.Equal(5001, row.YieldCscu);
    }

    [Fact]
    public void ExtractYieldRows_TwoRows_InYOrder()
    {
        var words = new[]
        {
            Word("Gold", 0, 300), Word("200", 70, 300),
            Word("Titanium", 0, 100), Word("644", 70, 100),
        };

        var rows = RefineryParser.ExtractYieldRows(Region(words)).Rows;

        Assert.Equal(2, rows.Count);
        Assert.Equal("TITANIUM", rows[0].Name); // lower Y first
        Assert.Equal("GOLD", rows[1].Name);
    }

    [Fact]
    public void ExtractYieldRows_RowTouchingTopEdge_CountedAsDroppedTop()
    {
        var words = new[] { Word("Titanium", 0, 2, height: 20), Word("644", 70, 2, height: 20) };

        var result = RefineryParser.ExtractYieldRows(Region(words));

        Assert.Empty(result.Rows);
        Assert.Equal(1, result.DroppedTopEdge);
        Assert.Equal(0, result.DroppedBottomEdge);
    }

    [Fact]
    public void ExtractYieldRows_RowTouchingBottomEdge_CountedAsDroppedBottom()
    {
        var words = new[] { Word("Titanium", 0, 995, height: 20), Word("644", 70, 995, height: 20) };

        var result = RefineryParser.ExtractYieldRows(Region(words, roiHeight: 1000));

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.DroppedTopEdge);
        Assert.Equal(1, result.DroppedBottomEdge);
    }

    [Fact]
    public void Checksum_SumOfRowsEqualsParsedTotal_WhenComplete()
    {
        var words = new[]
        {
            Word("Titanium", 0, 100), Word("313", 70, 100),
            Word("Gold", 0, 200), Word("57", 70, 200),
            Word("Iron", 0, 300), Word("274", 70, 300),
        };

        var rows = RefineryParser.ExtractYieldRows(Region(words)).Rows;
        var total = RefineryParser.ParseYieldTotal("YIELD 644");

        Assert.Equal(644, rows.Sum(r => r.YieldCscu)); // 313 + 57 + 274
        Assert.Equal(644, total);
    }

    [Fact]
    public void Checksum_SumMismatch_WhenARowIsMissing()
    {
        // Only two of three rows visible (list not scrolled) → sum < printed total.
        var words = new[]
        {
            Word("Titanium", 0, 100), Word("313", 70, 100),
            Word("Gold", 0, 200), Word("57", 70, 200),
        };

        var rows = RefineryParser.ExtractYieldRows(Region(words)).Rows;
        var total = RefineryParser.ParseYieldTotal("YIELD 644");

        Assert.NotEqual(total, rows.Sum(r => r.YieldCscu)); // 370 != 644 → Partial
    }

    [Theory]
    [InlineData("YIELD 644", 644)]
    [InlineData("YIELD: 644", 644)]
    [InlineData("YIELD 6,44", 644)]   // stray comma stripped
    [InlineData("644", 644)]          // bare total (fallback path)
    public void ParseYieldTotal_ParsesLabelledAndBare(string text, int expected)
        => Assert.Equal(expected, RefineryParser.ParseYieldTotal(text));

    [Fact]
    public void ParseYieldTotal_NoDigits_ReturnsNull()
        => Assert.Null(RefineryParser.ParseYieldTotal("YIELD"));

    [Theory]
    [InlineData("WORK ORDER 1", 1)]
    [InlineData("Work Order 12", 12)]
    public void ParseWorkOrderIndex_Parses(string text, int expected)
        => Assert.Equal(expected, RefineryParser.ParseWorkOrderIndex(text));

    [Fact]
    public void ParseWorkOrderIndex_NoMatch_ReturnsNull()
        => Assert.Null(RefineryParser.ParseWorkOrderIndex("no slot here"));

    [Theory]
    [InlineData("SETUP", PanelState.Setup)]
    [InlineData("processing", PanelState.Processing)]
    [InlineData("Completed", PanelState.Completed)]
    [InlineData("MATERIALS YIELDED", PanelState.None)]
    [InlineData("", PanelState.None)]
    public void Classify_MapsHeaderText(string header, PanelState expected)
        => Assert.Equal(expected, RefineryParser.Classify(header));

    [Theory]
    [InlineData("Torite (Ore)", "TORITE")]     // SETUP shows the ore-form suffix
    [InlineData("TORITE", "TORITE")]           // PROCESSING/COMPLETED show the base name
    [InlineData("Corundum (Raw)", "CORUNDUM")]
    public void BaseName_StripsOreSuffix(string raw, string expected)
        => Assert.Equal(expected, RefineryParser.BaseName(raw));

    [Fact]
    public void ExtractColumnarRows_SetupRow_SplitsNameAndNumbers_WithPlaceholderYield()
    {
        // SETUP layout: NAME QUALITY QTY YIELD(--), the name spanning two tokens incl. "(Ore)".
        var words = new[]
        {
            Word("Torite", 0, 100), Word("(Ore)", 70, 100),
            Word("262", 150, 100), Word("112", 220, 100), Word("--", 290, 100),
        };

        var row = Assert.Single(RefineryParser.ExtractColumnarRows(Region(words)).Rows);

        Assert.Equal("TORITE (ORE)", row.Name);
        Assert.Equal(262, row.Numbers[0]); // quality
        Assert.Equal(112, row.Numbers[1]); // qty
        Assert.Null(row.Numbers[2]);       // yield "--"
    }

    [Fact]
    public void ExtractColumnarRows_CompletedRow_NameQualityYield()
    {
        var words = new[] { Word("Torite", 0, 100), Word("262", 150, 100), Word("50", 220, 100) };

        var row = Assert.Single(RefineryParser.ExtractColumnarRows(Region(words)).Rows);

        Assert.Equal("TORITE", row.Name);
        Assert.Equal(new int?[] { 262, 50 }, row.Numbers);
    }

    // ---- TASK-RFN-01: a stray short OCR glyph must not corrupt or drop a real row ----

    [Theory]
    [InlineData("e", false)]     // a UI icon (checkmark) OCR'd as a lone stray letter — real corpus finding
    [InlineData("n", false)]
    [InlineData("no", false)]    // one digit-ish char ('o'), one not ('n') — not numeric
    [InlineData("5", true)]      // a genuine single digit
    [InlineData("O", true)]      // OCR digit-confusable single char (letter-for-zero)
    [InlineData("70", true)]
    [InlineData("7O", true)]     // OCR confusion within a genuine two-digit number
    [InlineData("--", true)]     // placeholder
    [InlineData("ORE", false)]
    [InlineData("GOLD", false)]
    [InlineData("S,OOl", true)]  // one non-digit-ish char (the comma) tolerated once length > 2
    public void LooksNumeric_ClassifiesTokens(string token, bool expected)
        => Assert.Equal(expected, RefineryParser.LooksNumeric(token));

    [Fact]
    public void ExtractColumnarRows_StrayShortGlyphBeforeName_RowSurvivesWithCorrectNumbers()
    {
        // Reproduces a real corpus finding (TASK-RFN-01 diagnostic probe against the engine's
        // v1.1.17 red-channel OCR output, frame_20260814_174748_765.png of the refinery-confirm
        // corpus): a UI icon clusters next to the row and OCRs as a lone stray "e". Before the
        // fix, LooksNumeric("e") was (wrongly) true for any 1-2 character token, so "e" swallowed
        // the following name token into the numbers column too — both failed to parse as numbers,
        // nameParts stayed empty, and the whole row (with its correct 262/50 numbers) was silently
        // dropped. Dropping this one row was what flipped the checksum-based Completeness to
        // Partial on the real corpus (sum 251 vs. printed total 303). The row's own Name here still
        // carries the stray token ("E TORITE") — that's expected and fine: OrderMatcher.SameMaterial
        // (Orders/OrderMatcher.cs) reconciles it against the clean "TORITE" identity already in the
        // ledger via name-token subset matching, and OrderLedger.MergeMaterial keeps the existing
        // clean name. What must not happen at this layer is losing the row/numbers entirely.
        var words = new[]
        {
            Word("e", 0, 100), Word("Torite", 70, 100), Word("262", 220, 100), Word("50", 290, 100),
        };

        var row = Assert.Single(RefineryParser.ExtractColumnarRows(Region(words)).Rows);

        Assert.Contains("TORITE", row.Name);
        Assert.Equal(new int?[] { 262, 50 }, row.Numbers);
    }

    [Fact]
    public void ExtractColumnarRows_UnfilledSetupSlotPlaceholder_IsDropped()
    {
        // "INERT MATERIALS" is the game's own literal placeholder for an empty SETUP slot, with an
        // unreadable quality column (real corpus finding, TASK-RFN-01). It must not become a
        // tracked material — only real ore rows should reach the ledger.
        var words = new[]
        {
            Word("Inert", 0, 100), Word("Materials", 90, 100), Word("129", 300, 100),
        };

        var result = RefineryParser.ExtractColumnarRows(Region(words));

        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ExtractColumnarRows_PlaceholderSlotWithStrayIconPrefix_IsStillDropped()
    {
        // The real corpus reading (frame_20260814_173206_049.png, setupList ROI): the placeholder
        // row carries the same per-row toggle-icon glyph as real rows do, so it parses as
        // "E INERT MATERIALS", not "INERT MATERIALS" alone. Exact-string matching missed this.
        var words = new[]
        {
            Word("e", 0, 100), Word("Inert", 90, 100), Word("Materials", 180, 100), Word("129", 350, 100),
        };

        var result = RefineryParser.ExtractColumnarRows(Region(words));

        Assert.Empty(result.Rows);
    }

    // ---- H6: OCR-garbage 12-digit tokens must never throw an unchecked (int) overflow ----

    [Fact]
    public void ExtractColumnarRows_TwelveDigitGarbageColumn_BecomesNull_DoesNotThrow()
    {
        var words = new[]
        {
            Word("Torite", 0, 100), Word("262", 150, 100), Word("999999999999", 220, 100),
        };

        var ex = Record.Exception(() => RefineryParser.ExtractColumnarRows(Region(words)));
        Assert.Null(ex);

        var row = Assert.Single(RefineryParser.ExtractColumnarRows(Region(words)).Rows);
        Assert.Equal(262, row.Numbers[0]);
        Assert.Null(row.Numbers[1]); // garbage column dropped, not a wrapped/overflowed int
    }

    [Fact]
    public void ExtractYieldRows_TwelveDigitGarbageYield_RowDropped_DoesNotThrow()
    {
        var words = new[] { Word("Titanium", 0, 100), Word("999999999999", 70, 100) };

        var ex = Record.Exception(() => RefineryParser.ExtractYieldRows(Region(words)));

        Assert.Null(ex);
        Assert.Empty(RefineryParser.ExtractYieldRows(Region(words)).Rows);
    }

    [Fact]
    public void ParseYieldTotal_TwelveDigitGarbage_ReturnsNull_DoesNotThrow_LabelledForm()
    {
        var ex = Record.Exception(() => RefineryParser.ParseYieldTotal("YIELD 999999999999"));
        Assert.Null(ex);
        Assert.Null(RefineryParser.ParseYieldTotal("YIELD 999999999999"));
    }

    [Fact]
    public void ParseYieldTotal_TwelveDigitGarbage_ReturnsNull_DoesNotThrow_BareFallbackForm()
    {
        var ex = Record.Exception(() => RefineryParser.ParseYieldTotal("999999999999"));
        Assert.Null(ex);
        Assert.Null(RefineryParser.ParseYieldTotal("999999999999"));
    }
}
