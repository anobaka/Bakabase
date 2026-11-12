namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>Explicit fallback for ZIP entry names without a Unicode encoding declaration.</summary>
public enum ZipFileNameCodePage
{
    DosLatin = 437,
    JapaneseShiftJis = 932,
    SimplifiedChineseGbk = 936,
    TraditionalChineseBig5 = 950,
    Utf8 = 65001
}
