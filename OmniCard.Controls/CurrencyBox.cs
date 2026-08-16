using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OmniCard.Controls;

/// <summary>
/// Attached behavior that turns a plain <see cref="TextBox"/> into a calculator/POS-style
/// currency entry field. Set <c>helpers:CurrencyBox.IsCurrency="True"</c> on any TextBox bound
/// to a (nullable) decimal.
///
/// Behavior:
/// <list type="bullet">
/// <item>All contents are selected on focus (click or tab), so typing over an existing value
/// replaces it.</item>
/// <item>Digits shift in from the right, cents-first: on an empty/zero field, typing 1, 2, 0
/// produces 0.01, 0.12, 1.20. The caret is always treated as being at the end, so entry is
/// predictable regardless of where the user clicks (pure POS model).</item>
/// <item>Backspace/Delete pops the right-most digit (1.20 -> 0.12); with the whole field
/// selected either key clears it.</item>
/// <item>Anything that is not a digit — decimal points, commas, letters, minus, spaces — is
/// rejected. Pasting is sanitized: the pasted text is parsed as a number and applied.</item>
/// </list>
///
/// When <see cref="AllowEmptyProperty"/> is set the field is left blank (rather than showing
/// 0.00) once cleared, so optional/nullable prices round-trip to <c>null</c> instead of 0.
/// </summary>
public static class CurrencyBox
{
    // $99,999,999.99 — a ceiling that stops runaway growth / overflow while staying far above
    // any realistic card or order value. Extra digits past this are ignored.
    private const long MaxCents = 9_999_999_999L;

    public static readonly DependencyProperty IsCurrencyProperty =
        DependencyProperty.RegisterAttached(
            "IsCurrency", typeof(bool), typeof(CurrencyBox),
            new PropertyMetadata(false, OnIsCurrencyChanged));

    public static bool GetIsCurrency(DependencyObject o) => (bool)o.GetValue(IsCurrencyProperty);
    public static void SetIsCurrency(DependencyObject o, bool v) => o.SetValue(IsCurrencyProperty, v);

    /// <summary>
    /// When true, a fully-cleared field shows an empty string instead of 0.00 so nullable
    /// bindings (TargetNullValue='') round-trip to null. Set on optional price fields.
    /// </summary>
    public static readonly DependencyProperty AllowEmptyProperty =
        DependencyProperty.RegisterAttached(
            "AllowEmpty", typeof(bool), typeof(CurrencyBox),
            new PropertyMetadata(false));

    public static bool GetAllowEmpty(DependencyObject o) => (bool)o.GetValue(AllowEmptyProperty);
    public static void SetAllowEmpty(DependencyObject o, bool v) => o.SetValue(AllowEmptyProperty, v);

    private static void OnIsCurrencyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        if ((bool)e.NewValue)
        {
            tb.PreviewTextInput += OnPreviewTextInput;
            tb.PreviewKeyDown += OnPreviewKeyDown;
            tb.GotKeyboardFocus += OnGotKeyboardFocus;
            tb.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            DataObject.AddPastingHandler(tb, OnPaste);
        }
        else
        {
            tb.PreviewTextInput -= OnPreviewTextInput;
            tb.PreviewKeyDown -= OnPreviewKeyDown;
            tb.GotKeyboardFocus -= OnGotKeyboardFocus;
            tb.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            DataObject.RemovePastingHandler(tb, OnPaste);
        }
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // Normalize whatever the source binding rendered (e.g. "12.5", "$12.50", "1,234.5")
        // to the canonical two-decimal form, so the digit-shift model reads it correctly.
        if (!string.IsNullOrWhiteSpace(tb.Text) &&
            CurrencyInputCore.TryParseToCents(tb.Text, out var cents))
        {
            tb.Text = CurrencyInputCore.FormatCents(cents, GetAllowEmpty(tb));
        }

        tb.SelectAll();
    }

    // First click into an unfocused box focuses + selects all without dropping a caret
    // (the usual WPF "select-all-on-focus gets cleared by the click" fix).
    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (!tb.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            tb.Focus();
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // Only single digits advance the value; everything else is rejected outright.
        if (e.Text.Length != 1 || e.Text[0] < '0' || e.Text[0] > '9')
        {
            e.Handled = true;
            return;
        }

        var wholeSelected = tb.SelectionLength == tb.Text.Length && tb.Text.Length > 0;
        var current = wholeSelected ? 0 : CurrencyInputCore.DigitsToCents(tb.Text);
        var next = CurrencyInputCore.AppendDigit(current, e.Text[0] - '0', MaxCents);

        // User is typing, so always render a number (never blank) even on optional fields.
        Apply(tb, next, allowEmpty: false);
        e.Handled = true;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true; // no spaces in currency
                break;

            case Key.Back:
            case Key.Delete:
                var wholeSelected = tb.SelectionLength == tb.Text.Length && tb.Text.Length > 0;
                var next = wholeSelected ? 0 : CurrencyInputCore.DigitsToCents(tb.Text) / 10;
                Apply(tb, next, allowEmpty: GetAllowEmpty(tb));
                e.Handled = true;
                break;
        }
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox tb) return;

        var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string;
        if (pasted is not null && CurrencyInputCore.TryParseToCents(pasted, out var cents))
        {
            Apply(tb, cents, allowEmpty: false);
        }

        // Always cancel the raw paste: we've either applied a sanitized value or rejected junk.
        e.CancelCommand();
    }

    private static void Apply(TextBox tb, long cents, bool allowEmpty)
    {
        tb.Text = CurrencyInputCore.FormatCents(cents, allowEmpty);
        tb.CaretIndex = tb.Text.Length;
    }
}

/// <summary>
/// Pure, UI-free currency-entry math backing <see cref="CurrencyBox"/>. Kept separate so the
/// digit-shift model can be unit tested without spinning up WPF.
/// </summary>
public static class CurrencyInputCore
{
    /// <summary>Interprets the digits already in a canonical (2-decimal) string as a cents count.</summary>
    public static long DigitsToCents(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        long cents = 0;
        foreach (var c in text)
        {
            if (c is >= '0' and <= '9')
            {
                cents = cents * 10 + (c - '0');
                if (cents > 9_999_999_999L) cents = 9_999_999_999L;
            }
        }
        return cents;
    }

    /// <summary>Shifts a new digit in from the right, clamped to <paramref name="maxCents"/>.</summary>
    public static long AppendDigit(long cents, int digit, long maxCents)
    {
        var next = cents * 10 + digit;
        return next > maxCents ? cents : next;
    }

    /// <summary>
    /// Parses arbitrary user/pasted/source text (with symbols, grouping, or fewer than two
    /// decimals) as a currency amount and returns it as whole cents. Used on focus and paste.
    /// </summary>
    public static bool TryParseToCents(string? text, out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (decimal.TryParse(
                text,
                NumberStyles.Currency,
                CultureInfo.CurrentCulture,
                out var value))
        {
            cents = (long)decimal.Round(Math.Abs(value) * 100m, 0, MidpointRounding.AwayFromZero);
            if (cents > 9_999_999_999L) cents = 9_999_999_999L;
            return true;
        }
        return false;
    }

    /// <summary>Formats cents as "N.NN"; blank when <paramref name="allowEmpty"/> and zero.</summary>
    public static string FormatCents(long cents, bool allowEmpty)
    {
        if (allowEmpty && cents == 0) return string.Empty;
        return (cents / 100m).ToString("0.00", CultureInfo.CurrentCulture);
    }
}
