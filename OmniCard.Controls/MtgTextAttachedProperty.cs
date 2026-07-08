using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace OmniCard.Controls;

public static class MtgText
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(MtgText),
            new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject obj) => (string?)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string? value) => obj.SetValue(TextProperty, value);

    private static readonly WpfDrawingSettings SvgSettings = new()
    {
        IncludeRuntime = true,
        TextAsGeometry = false,
    };

    private static readonly Dictionary<string, DrawingImage?> SvgCache = [];
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RichTextBox, EventHandler> FontSizeHandlers = [];

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox richTextBox)
            return;

        var text = e.NewValue as string;
        var paragraph = new Paragraph();

        if (!string.IsNullOrEmpty(text))
        {
            var segments = MtgSymbolParser.Parse(text);
            foreach (var segment in segments)
            {
                switch (segment)
                {
                    case TextSegment ts:
                        // Split on newlines to create separate paragraphs-worth of runs
                        var lines = ts.Text.Split('\n');
                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].Length > 0)
                                paragraph.Inlines.Add(new Run(lines[i]));
                            if (i < lines.Length - 1)
                                paragraph.Inlines.Add(new LineBreak());
                        }
                        break;

                    case SymbolSegment ss:
                        var image = LoadSymbolImage(ss.FileName);
                        if (image != null)
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Source = image,
                                Stretch = Stretch.Uniform,
                                // Height bound to font size — inherited from parent
                                Height = richTextBox.FontSize,
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            paragraph.Inlines.Add(new InlineUIContainer(img)
                            {
                                BaselineAlignment = BaselineAlignment.Center,
                            });
                        }
                        else
                        {
                            // Fallback: render raw {CODE} text
                            paragraph.Inlines.Add(new Run($"{{{ss.Code}}}"));
                        }
                        break;
                }
            }
        }

        var doc = new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(0),
        };
        richTextBox.Document = doc;

        // Re-render when font size changes so symbols scale with text
        var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            Control.FontSizeProperty, typeof(RichTextBox));
        if (dpd != null)
        {
            // Remove previous handler to avoid accumulation across card selections
            if (FontSizeHandlers.TryGetValue(richTextBox, out var old))
                dpd.RemoveValueChanged(richTextBox, old);

            EventHandler handler = (s, _) =>
            {
                if (s is RichTextBox rtb)
                {
                    foreach (var block in rtb.Document.Blocks.OfType<Paragraph>())
                    {
                        foreach (var inline in block.Inlines.OfType<InlineUIContainer>())
                        {
                            if (inline.Child is System.Windows.Controls.Image img)
                                img.Height = rtb.FontSize;
                        }
                    }
                }
            };
            FontSizeHandlers.AddOrUpdate(richTextBox, handler);
            dpd.AddValueChanged(richTextBox, handler);
        }
    }

    private static DrawingImage? LoadSymbolImage(string fileName)
    {
        if (SvgCache.TryGetValue(fileName, out var cached))
            return cached;

        try
        {
            var uri = new Uri($"pack://application:,,,/Resources/Symbols/Mana/{fileName}.svg", UriKind.Absolute);
            var resourceInfo = Application.GetResourceStream(uri);
            if (resourceInfo == null)
            {
                SvgCache[fileName] = null;
                return null;
            }

            using var stream = resourceInfo.Stream;
            using var reader = new FileSvgReader(SvgSettings);
            var drawing = reader.Read(stream);

            if (drawing == null)
            {
                SvgCache[fileName] = null;
                return null;
            }

            var image = new DrawingImage(drawing);
            image.Freeze();
            SvgCache[fileName] = image;
            return image;
        }
        catch
        {
            SvgCache[fileName] = null;
            return null;
        }
    }
}
