namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>Explicit byte encoding of a known ZIP AES password, independent of entry names.</summary>
public enum ZipPasswordCodePage
{
    DosLatin = 437,
    JapaneseShiftJis = 932,
    SimplifiedChineseGbk = 936,
    TraditionalChineseBig5 = 950,
    Utf8 = 65001
}
