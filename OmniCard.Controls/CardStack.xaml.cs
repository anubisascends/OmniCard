using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using OmniCard.Models;

namespace OmniCard.Controls;

/// <summary>Event args for a mouse press on a card in a <see cref="CardStack"/>. The host decides
/// selection/activation from these — the stack itself stays presentation-only.</summary>
public sealed class CardStackPressEventArgs(CollectionCard card, MouseButton button, int clickCount, bool ctrl, bool shift) : EventArgs
{
    public CollectionCard Card { get; } = card;
    public MouseButton Button { get; } = button;
    public int ClickCount { get; } = clickCount;
    public bool Ctrl { get; } = ctrl;
    public bool Shift { get; } = shift;
}

/// <summary>A vertical pile of cards drawn as thin title bars (see the XAML). Hovering one card
/// animates its slot to full card height; the StackPanel then pushes the cards below it down, so
/// only the hovered card is enlarged — matching Archidekt's per-card expand. Mouse presses are
/// surfaced via <see cref="CardPressed"/> for the host to turn into selection / editor activation;
/// <see cref="CardWidth"/> drives zoom.</summary>
public partial class CardStack : UserControl
{
    private const double CollapsedHeight = 34;
    private const double CardAspect = 88.0 / 63.0; // card height / width
    private static readonly Duration ExpandDuration = new(TimeSpan.FromMilliseconds(150));

    public CardStack() => InitializeComponent();

    /// <summary>Raised on any mouse-button press on a card. The host handles selection and, on a
    /// left double-click, activation.</summary>
    public event EventHandler<CardStackPressEventArgs>? CardPressed;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(CardStack), new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty DataDirectoryProperty =
        DependencyProperty.Register(nameof(DataDirectory), typeof(string), typeof(CardStack), new PropertyMetadata(""));

    public string DataDirectory
    {
        get => (string)GetValue(DataDirectoryProperty);
        set => SetValue(DataDirectoryProperty, value);
    }

    /// <summary>Width of each card in the pile (px). Bound to the deck view's zoom level. Keeps
    /// <see cref="CardHeight"/> in sync.</summary>
    public static readonly DependencyProperty CardWidthProperty =
        DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(CardStack),
            new PropertyMetadata(150.0, OnCardWidthChanged));

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    /// <summary>Full card height (px) = <see cref="CardWidth"/> × aspect. The card image is given
    /// this explicit height so it renders full-size and the collapsed slot clips it to the top name
    /// band, instead of Uniform shrinking the whole card into the 34px strip.</summary>
    public static readonly DependencyProperty CardHeightProperty =
        DependencyProperty.Register(nameof(CardHeight), typeof(double), typeof(CardStack),
            new PropertyMetadata(150.0 * CardAspect));

    public double CardHeight
    {
        get => (double)GetValue(CardHeightProperty);
        private set => SetValue(CardHeightProperty, value);
    }

    private static void OnCardWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CardStack)d).CardHeight = (double)e.NewValue * CardAspect;

    private void Slot_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border slot)
            Animate(slot, CardHeight, handBack: false);
    }

    private void Slot_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not Border slot) return;
        // The last card sits at full height (its base state); everyone else collapses to a strip.
        // Hand the height binding back afterward so zoom keeps re-sizing the bottom card.
        var toHeight = IsLastCard(slot.DataContext as CollectionCard) ? CardHeight : CollapsedHeight;
        Animate(slot, toHeight, handBack: true);
    }

    private static void Animate(Border slot, double toHeight, bool handBack)
    {
        var anim = new DoubleAnimation(toHeight, ExpandDuration) { AccelerationRatio = 0.3, DecelerationRatio = 0.5 };
        if (handBack)
            // On completion, drop the animation so the Height MultiBinding drives the value again
            // (BeginAnimation(null) leaves the current value in place — no visual jump).
            anim.Completed += (_, _) => slot.BeginAnimation(HeightProperty, null);
        slot.BeginAnimation(HeightProperty, anim);
    }

    private bool IsLastCard(CollectionCard? card)
    {
        if (card is null || ItemsSource is null) return false;
        object? last = null;
        foreach (var item in ItemsSource) last = item;
        return ReferenceEquals(card, last);
    }

    private void Slot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        RaisePress(sender, MouseButton.Left, e.ClickCount);

    private void Slot_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        RaisePress(sender, MouseButton.Right, e.ClickCount);

    private void RaisePress(object sender, MouseButton button, int clickCount)
    {
        if (sender is not FrameworkElement { DataContext: CollectionCard card }) return;
        var mods = Keyboard.Modifiers;
        CardPressed?.Invoke(this, new CardStackPressEventArgs(
            card, button, clickCount,
            ctrl: mods.HasFlag(ModifierKeys.Control),
            shift: mods.HasFlag(ModifierKeys.Shift)));
    }
}
