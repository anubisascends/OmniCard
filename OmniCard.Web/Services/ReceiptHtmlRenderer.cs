using System.Globalization;
using System.Net;
using System.Text;
using OmniCard.Shared.Sales;

namespace OmniCard.Web.Services;

/// <summary>
/// Renders a <see cref="ReceiptDocument"/> as a self-contained, print-ready HTML page for thermal/roll
/// receipt printers. The key advantage over the PDF path is the <c>@page { size: {width}mm auto }</c>
/// rule: the browser prints a page exactly the roll's width and only as tall as the content, so picking
/// the receipt printer doesn't pad the job out to a Letter/A4 sheet (the cause of the "uses a lot of
/// paper" problem). The page auto-invokes the print dialog on load.
/// </summary>
public static class ReceiptHtmlRenderer
{
    public static string Render(ReceiptDocument doc)
    {
        var w = doc.WidthMm.ToString(CultureInfo.InvariantCulture);
        var m = doc.MarginMm.ToString(CultureInfo.InvariantCulture);
        var f = doc.FontPointSize.ToString(CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>Receipt</title><style>");
        // Initial page = roll width, auto height (fallback if the measuring script can't run). The script
        // below replaces this with an EXACT height so the printer only feeds the content, no blank tail.
        sb.Append($"@page{{size:{w}mm auto;margin:0;}}");
        sb.Append("*{box-sizing:border-box;}html,body{margin:0;padding:0;}");
        // All printable content lives in #rcpt (that's what we measure) — the width + margin live here,
        // NOT on body, so the on-screen toolbar can't inflate the measured height.
        sb.Append($"#rcpt{{width:{w}mm;padding:{m}mm;font-family:'Segoe UI',Arial,sans-serif;font-size:{f}pt;color:#000;line-height:1.25;}}");
        sb.Append(".c{text-align:center;}.b{font-weight:700;}.r{text-align:right;}");
        sb.Append($".name{{font-size:calc({f}pt + 3pt);}}");
        sb.Append(".logo{display:block;margin:0 auto 4px;max-height:64px;max-width:100%;}");
        sb.Append("hr{border:none;border-top:1px solid #000;margin:4px 0;}");
        sb.Append("table{width:100%;border-collapse:collapse;}");
        sb.Append("td{padding:1px 0;vertical-align:top;}");
        sb.Append("td.q{text-align:right;white-space:nowrap;padding-left:4px;}");
        sb.Append("td.a{text-align:right;white-space:nowrap;padding-left:4px;}");
        sb.Append(".sec{margin-top:4px;}");
        // On-screen-only toolbar, fixed (out of flow so it never affects #rcpt's measured height).
        sb.Append("@media screen{.bar{position:fixed;top:0;right:0;background:#f3f3f3;padding:8px;font-family:Arial;font-size:12pt;border:1px solid #ccc;}.bar button{font-size:12pt;padding:4px 12px;cursor:pointer;}}");
        sb.Append("@media print{.bar{display:none;}}");
        sb.Append("</style></head><body>");

        sb.Append("<div class=\"bar noprint\"><button onclick=\"window.print()\">Print receipt</button></div>");
        sb.Append("<div id=\"rcpt\">");

        // Header: logo + company identity
        if (!string.IsNullOrWhiteSpace(doc.CompanyLogoUrl))
            sb.Append($"<img class=\"logo\" src=\"{Attr(doc.CompanyLogoUrl)}\" alt=\"\">");
        if (!string.IsNullOrWhiteSpace(doc.CompanyName))
            sb.Append($"<div class=\"c b name\">{Enc(doc.CompanyName)}</div>");
        AppendBlockLines(sb, doc.CompanyAddressBlock, "c");
        if (!string.IsNullOrWhiteSpace(doc.CompanyPhone))
            sb.Append($"<div class=\"c\">{Enc(doc.CompanyPhone)}</div>");
        if (!string.IsNullOrWhiteSpace(doc.CompanyEmail))
            sb.Append($"<div class=\"c\">{Enc(doc.CompanyEmail)}</div>");

        sb.Append("<hr>");

        // Order info
        if (!string.IsNullOrWhiteSpace(doc.OrderNumber))
            sb.Append($"<div class=\"b\">Order: {Enc(doc.OrderNumber)}</div>");
        if (!string.IsNullOrWhiteSpace(doc.Channel))
            sb.Append($"<div>Channel: {Enc(doc.Channel)}</div>");
        sb.Append($"<div>Date: {doc.OrderDate:yyyy-MM-dd}</div>");
        if (!string.IsNullOrWhiteSpace(doc.TrackingNumber))
        {
            var carrier = string.IsNullOrWhiteSpace(doc.Carrier) ? "" : $" ({Enc(doc.Carrier)})";
            sb.Append($"<div>Tracking No.: {Enc(doc.TrackingNumber)}{carrier}</div>");
        }

        // Customer / ship-to
        sb.Append("<div class=\"sec b\">Ship to:</div>");
        sb.Append($"<div>{Enc(doc.CustomerName)}</div>");
        AppendBlockLines(sb, doc.CustomerAddressBlock, null);

        sb.Append("<hr>");

        // Line items
        sb.Append("<table>");
        foreach (var line in doc.Lines)
        {
            var label = line.Name
                        + (string.IsNullOrWhiteSpace(line.Set) ? "" : $" ({line.Set})")
                        + (string.IsNullOrWhiteSpace(line.Condition) ? "" : $" {line.Condition}")
                        + (line.IsFoil ? " *foil" : "");
            sb.Append("<tr>");
            sb.Append($"<td>{Enc(label)}</td>");
            sb.Append($"<td class=\"q\">x{line.Quantity}</td>");
            if (doc.ShowPrices)
                sb.Append($"<td class=\"a\">${line.LineTotal.ToString("N2", CultureInfo.InvariantCulture)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");

        // Totals
        if (doc.ShowPrices)
        {
            sb.Append("<hr>");
            sb.Append($"<div class=\"r\">Items: ${doc.ItemsTotal.ToString("N2", CultureInfo.InvariantCulture)}</div>");
            sb.Append($"<div class=\"r\">Shipping: ${doc.Shipping.ToString("N2", CultureInfo.InvariantCulture)}</div>");
            sb.Append($"<div class=\"r b\">Total: ${doc.GrandTotal.ToString("N2", CultureInfo.InvariantCulture)}</div>");
        }

        if (!string.IsNullOrWhiteSpace(doc.FooterText))
            sb.Append($"<div class=\"c sec\" style=\"margin-top:8px;\">{Enc(doc.FooterText)}</div>");

        sb.Append("</div>"); // #rcpt

        // Measure the rendered content height and pin the @page box to it (px→mm at the CSS 96dpi
        // reference) BEFORE printing. `size: <w>mm auto` alone doesn't shrink the sheet — browsers keep
        // the printer's default page length and feed the remainder as blank paper. A tiny margin avoids
        // spilling the last line onto a second page. Runs after load() so the logo image is measured too.
        sb.Append("<script>(function(){function go(){var el=document.getElementById('rcpt');");
        sb.Append("var mm=Math.ceil(el.getBoundingClientRect().height*25.4/96)+2;");
        sb.Append("var s=document.createElement('style');s.textContent='@page{size:");
        sb.Append(w);
        sb.Append("mm '+mm+'mm;margin:0;}';document.head.appendChild(s);");
        sb.Append("setTimeout(function(){window.print();},60);}");
        sb.Append("if(document.readyState==='complete')go();else window.addEventListener('load',go);})();</script>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendBlockLines(StringBuilder sb, string? block, string? cssClass)
    {
        if (string.IsNullOrWhiteSpace(block)) return;
        var cls = cssClass is null ? "" : $" class=\"{cssClass}\"";
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            sb.Append($"<div{cls}>{Enc(line)}</div>");
        }
    }

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");

    // For attribute contexts (the logo URL). Encoded so a crafted path can't break out of the attribute.
    private static string Attr(string? s) => WebUtility.HtmlEncode(s ?? "").Replace("\"", "&quot;");
}
