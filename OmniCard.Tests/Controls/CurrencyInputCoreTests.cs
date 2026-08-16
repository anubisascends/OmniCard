using OmniCard.Controls;

namespace OmniCard.Tests.Controls;

/// <summary>
/// Covers the UI-free currency-entry math behind the CurrencyBox attached behavior:
/// the calculator/POS digit-shift model, backspace, and focus/paste normalization.
/// </summary>
public class CurrencyInputCoreTests
{
    private const long Max = 9_999_999_999L;

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("0.00", 0)]
    [InlineData("0.12", 12)]
    [InlineData("1.20", 120)]
    [InlineData("12.50", 1250)]
    [InlineData("$1,234.50", 123450)]
    public void DigitsToCents_ReadsCanonicalAndSymboledText(string? text, long expected)
    {
        Assert.Equal(expected, CurrencyInputCore.DigitsToCents(text));
    }

    [Fact]
    public void AppendDigit_ShiftsInFromTheRight()
    {
        // The scenario from the feature request: type 1, 2, 0 into an empty field.
        long cents = 0;
        cents = CurrencyInputCore.AppendDigit(cents, 1, Max);
        Assert.Equal(1, cents);   // 0.01
        cents = CurrencyInputCore.AppendDigit(cents, 2, Max);
        Assert.Equal(12, cents);  // 0.12
        cents = CurrencyInputCore.AppendDigit(cents, 0, Max);
        Assert.Equal(120, cents); // 1.20
    }

    [Fact]
    public void AppendDigit_ClampsAtMaximum()
    {
        Assert.Equal(Max, CurrencyInputCore.AppendDigit(Max, 9, Max));
    }

    [Theory]
    [InlineData(120, 12)]   // backspace turns 1.20 -> 0.12
    [InlineData(12, 1)]     // 0.12 -> 0.01
    [InlineData(1, 0)]      // 0.01 -> 0.00
    [InlineData(0, 0)]      // already empty stays empty
    public void IntegerDivideByTen_ModelsBackspace(long cents, long expected)
    {
        Assert.Equal(expected, cents / 10);
    }

    [Theory]
    [InlineData("12.5", 1250)]     // fewer than 2 decimals from an unformatted binding
    [InlineData("12", 1200)]       // a bare integer paste means $12.00, not $0.12
    [InlineData("$12.50", 1250)]
    [InlineData("1,234.50", 123450)]
    [InlineData("-5", 500)]        // negatives are coerced positive (no negative currency fields)
    public void TryParseToCents_ParsesArbitraryInput(string text, long expected)
    {
        Assert.True(CurrencyInputCore.TryParseToCents(text, out var cents));
        Assert.Equal(expected, cents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void TryParseToCents_RejectsNonNumeric(string text)
    {
        Assert.False(CurrencyInputCore.TryParseToCents(text, out _));
    }

    [Theory]
    [InlineData(0, false, "0.00")]
    [InlineData(0, true, "")]        // optional/nullable field renders blank when cleared
    [InlineData(12, false, "0.12")]
    [InlineData(120, false, "1.20")]
    [InlineData(123450, false, "1234.50")]
    public void FormatCents_RendersTwoDecimalsOrBlank(long cents, bool allowEmpty, string expected)
    {
        Assert.Equal(expected, CurrencyInputCore.FormatCents(cents, allowEmpty));
    }
}
