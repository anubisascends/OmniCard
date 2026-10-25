namespace OmniCard.Shared.Labels;

/// <summary>
/// A user-defined label template: the sheet the labels are printed on (page size, margins, a grid of
/// <see cref="Columns"/> × <see cref="Rows"/> labels and the spacing between them) plus the design of one
/// label. The design is a pdfme template (JSON) for a single label — its text, barcode and image
/// elements, laid out by the user in the Label Designer — and the SPA renders it per card and tiles the
/// labels onto sheets, so the app defines no layout of its own. Every label is the same size, derived
/// from the sheet (<see cref="LabelWidthMm"/>/<see cref="LabelHeightMm"/>). Templates are shared by
/// everyone who prints labels; names are unique. All lengths are millimetres.
/// </summary>
public class LabelTemplate
{
    public const int MaxNameLength = 100;
    /// <summary>Design JSON cap — room for a few embedded logo images.</summary>
    public const int MaxDesignLength = 4_000_000;
    public const int MaxGrid = 50;
    public const double MinPageMm = 10;
    public const double MaxPageMm = 2000;

    public int Id { get; set; }

    public string Name { get; set; } = "";

    public double PageWidthMm { get; set; }
    public double PageHeightMm { get; set; }

    public double MarginTopMm { get; set; }
    public double MarginRightMm { get; set; }
    public double MarginBottomMm { get; set; }
    public double MarginLeftMm { get; set; }

    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;

    /// <summary>Horizontal space between neighbouring labels in a row.</summary>
    public double ColumnGapMm { get; set; }
    /// <summary>Vertical space between neighbouring rows of labels.</summary>
    public double RowGapMm { get; set; }

    /// <summary>The pdfme template for one label (<c>{ "schemas": [[…]], "basePdf": {…} }</c>).</summary>
    public string Design { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Width of one label: what's left of the page width after the side margins and the gaps
    /// between columns, shared equally by the columns.</summary>
    public static double LabelWidth(double pageWidth, double marginLeft, double marginRight, int columns, double columnGap) =>
        columns < 1 ? 0 : (pageWidth - marginLeft - marginRight - (columns - 1) * columnGap) / columns;

    /// <summary>Height of one label (see <see cref="LabelWidth"/>).</summary>
    public static double LabelHeight(double pageHeight, double marginTop, double marginBottom, int rows, double rowGap) =>
        rows < 1 ? 0 : (pageHeight - marginTop - marginBottom - (rows - 1) * rowGap) / rows;

    public double LabelWidthMm => LabelWidth(PageWidthMm, MarginLeftMm, MarginRightMm, Columns, ColumnGapMm);
    public double LabelHeightMm => LabelHeight(PageHeightMm, MarginTopMm, MarginBottomMm, Rows, RowGapMm);
}
