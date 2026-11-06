using System.Globalization;
using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Features;
using Erp.Shared.BuildingBlocks;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Erp.Modules.Invoicing.Infrastructure;

// The printed invoice, laid out to show what BIR RR 7-2024 Sec. 6(B) requires: the seller's registered name, TIN
// statement and address, the word "Invoice", a prominent serial number, the date, space for the buyer's name, address
// and TIN, quantity, unit cost and description per line, the VAT breakdown, a special discount's details (B.18), and the CAS
// permit details at the bottom.
internal static class InvoicePdf
{
    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");

    // Everything about the seller comes from the invoice's own copy, so a reprint matches the original.
    public static byte[] Render(InvoiceDetailDto i, bool reprint)
    {
        var regime = Enum.Parse<TaxRegime>(i.TaxRegime);
        var doc = new Document();
        var seller = i.Seller;
        doc.Info.Title = $"{seller.InvoiceTitle} {i.Number}";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = ErpFontResolver.Family;
        normal.Font.Size = 9;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.5);

        if (reprint) Line(section, "REPRINT", 14, bold: true, ParagraphAlignment.Right);

        Line(section, seller.Name, 14, bold: true);
        if (seller.TradeName is not null) Line(section, seller.TradeName);
        // "VAT Reg TIN 123-456-789-00000" or "Non-VAT Reg TIN ...": Sec. 6(B.2).
        Line(section, $"{(regime == TaxRegime.Vat ? "VAT" : "Non-VAT")} Reg TIN {seller.Tin}");
        Line(section, seller.Address);

        var title = Line(section, seller.InvoiceTitle, 16, bold: true);
        title.Format.SpaceBefore = Unit.FromPoint(12);
        if (regime == TaxRegime.Exempt) Line(section, "EXEMPT", 12, bold: true);
        var onlyExempt = i.VatableSales == 0 && i.ZeroRatedSales == 0 && i.VatExemptSales > 0;
        var onlyZeroRated = i.VatableSales == 0 && i.VatExemptSales == 0 && i.ZeroRatedSales > 0;
        if (regime == TaxRegime.Vat && onlyExempt) Line(section, "VAT-Exempt Sale", 11, bold: true);
        if (regime == TaxRegime.Vat && onlyZeroRated) Line(section, "Zero-Rated Sale", 11, bold: true);
        if (i.Status == "Void") Line(section, $"VOID — {i.VoidReason}", 11, bold: true);

        var info = section.AddTable();
        info.AddColumn(Unit.FromCentimeter(10));
        info.AddColumn(Unit.FromCentimeter(7.4));
        info.Format.SpaceBefore = Unit.FromPoint(8);
        var row = info.AddRow();
        row.TopPadding = Unit.FromPoint(8);
        row.Cells[0].AddParagraph("Sold to:").Format.Font.Bold = true;
        row.Cells[0].AddParagraph($"Registered name: {i.CustomerName}");
        row.Cells[0].AddParagraph($"TIN: {i.CustomerTin ?? "____________________"}");
        row.Cells[0].AddParagraph($"Address: {i.CustomerAddress ?? "____________________"}");
        var number = row.Cells[1].AddParagraph($"No. {i.Number}");
        number.Format.Font.Size = 13;
        number.Format.Font.Bold = true;
        row.Cells[1].AddParagraph($"Date: {i.IssueDate:MMMM d, yyyy}");
        row.Cells[1].AddParagraph($"Due: {i.DueDate:MMMM d, yyyy}");
        row.Cells[1].AddParagraph($"Order: {i.OrderNumber}");

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);

        var lines = section.AddTable();
        lines.Borders.Width = 0.5;
        foreach (var width in new[] { 7.4, 2.0, 2.0, 3.0, 3.0 }) lines.AddColumn(Unit.FromCentimeter(width));
        var head = lines.AddRow();
        head.HeadingFormat = true;
        head.Format.Font.Bold = true;
        Cells(head, "Description", "Qty", "Unit", "Unit price", "Amount");
        foreach (var l in i.Lines)
            Cells(lines.AddRow(), Describe(l, regime), l.Quantity.ToString("0.###", Ph), l.Unit, Peso(l.UnitPrice), Peso(l.LineTotal));

        var totals = section.AddTable();
        totals.AddColumn(Unit.FromCentimeter(14.4));
        totals.AddColumn(Unit.FromCentimeter(3.0));
        switch (regime)
        {
            case TaxRegime.Vat:
                Total(totals, "VATable Sales", i.VatableSales);
                Total(totals, $"VAT ({(i.VatRate * 100).ToString("0.##", Ph)}%)", i.VatAmount);
                Total(totals, "Zero-Rated Sales", i.ZeroRatedSales);
                Total(totals, "VAT-Exempt Sales", i.VatExemptSales);
                LessDiscount(totals, i.SpecialDiscount);
                Total(totals, "Total Amount Due (VAT inclusive)", i.Total, bold: true);
                break;
            case TaxRegime.PercentageTax:
                Total(totals, "Sales Subject to Percentage Tax", i.GrossSales - i.VatExemptSales);
                if (i.VatExemptSales > 0) Total(totals, "Exempt Sales", i.VatExemptSales);
                LessDiscount(totals, i.SpecialDiscount);
                Total(totals, "Total Amount Due", i.Total, bold: true);
                break;
            default:
                LessDiscount(totals, i.SpecialDiscount);
                Total(totals, "Total Amount Due", i.Total, bold: true);
                break;
        }

        // Sec. 6(B.18): the holder's ID, the discount broken down into the VAT exemption and the discount, and a signature.
        if (i.SpecialDiscount is { } d)
        {
            var block = section.AddParagraph($"{d.Program} discount (items marked *)");
            block.Format.Font.Bold = true;
            block.Format.SpaceBefore = Unit.FromPoint(12);
            section.AddParagraph($"Name: {d.HolderName}    {d.IdLabel}: {d.IdNumber}");
            var detail = section.AddTable();
            detail.AddColumn(Unit.FromCentimeter(14.4));
            detail.AddColumn(Unit.FromCentimeter(3.0));
            Total(detail, "Sales eligible for the discount", i.Lines.Where(l => l.SpecialDiscountEligible).Sum(l => l.LineTotal));
            if (d.VatExemption > 0) Total(detail, $"Less: {Percent(d.VatRateRemoved)} VAT exemption", d.VatExemption);
            Total(detail, $"Less: {Percent(d.Rate)} discount", d.Discount);
            var signature = section.AddParagraph("Signature: ________________________________");
            signature.Format.SpaceBefore = Unit.FromPoint(18);
        }
        if (i.AmountPaid > 0)
        {
            Total(totals, "Less payments received", i.AmountPaid);
            Total(totals, "Balance due", i.BalanceDue, bold: true);
        }

        // Sec. 6(B.21): system-generated invoices carry the permit (PTU/ACCN), the series range and the date issued.
        var footer = section.Footers.Primary;
        footer.Format.Font.Size = 8;
        if (seller.PermitNumber is not null) footer.AddParagraph($"BIR Permit to Use / Acknowledgement Certificate Control No.: {seller.PermitNumber}");
        if (seller.PermitDateIssued is not null) footer.AddParagraph($"Date Issued: {seller.PermitDateIssued}");
        if (seller.SeriesRange is not null) footer.AddParagraph($"Series Range: {seller.SeriesRange}");

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static string Describe(InvoiceLineDto line, TaxRegime regime) =>
        (line.SpecialDiscountEligible ? "* " : "") + Describe(line.Description, line.TaxType, regime);

    private static string Describe(string description, string taxType, TaxRegime regime) =>
        regime != TaxRegime.Vat ? description
        : taxType == nameof(TaxType.ZeroRated) ? $"{description} (zero-rated)"
        : taxType == nameof(TaxType.VatExempt) ? $"{description} (VAT-exempt)"
        : description;

    private static void LessDiscount(Table totals, InvoiceSpecialDiscountDto? d)
    {
        if (d is null) return;
        if (d.VatExemption > 0) Total(totals, $"Less: VAT exemption ({d.Program})", d.VatExemption);
        Total(totals, $"Less: {Percent(d.Rate)} {d.Program} discount", d.Discount);
    }

    private static string Percent(decimal rate) => (rate * 100).ToString("0.##", Ph) + "%";

    // "PHP": the peso sign isn't in every font, and Philippine invoices commonly print the code anyway.
    private static string Peso(decimal amount) => "PHP " + amount.ToString("N2", Ph);

    private static Paragraph Line(Section section, string text, int size = 9, bool bold = false,
        ParagraphAlignment align = ParagraphAlignment.Left)
    {
        var p = section.AddParagraph(text);
        p.Format.Font.Size = size;
        p.Format.Font.Bold = bold;
        p.Format.Alignment = align;
        return p;
    }

    private static void Cells(Row row, params string[] values)
    {
        for (var c = 0; c < values.Length; c++)
        {
            var p = row.Cells[c].AddParagraph(values[c]);
            if (c > 0 && c != 2) p.Format.Alignment = ParagraphAlignment.Right;
        }
    }

    private static void Total(Table table, string label, decimal amount, bool bold = false)
    {
        var row = table.AddRow();
        row.Format.Font.Bold = bold;
        row.Cells[0].AddParagraph(label).Format.Alignment = ParagraphAlignment.Right;
        row.Cells[1].AddParagraph(Peso(amount)).Format.Alignment = ParagraphAlignment.Right;
    }
}
