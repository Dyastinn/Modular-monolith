using Erp.Modules.Invoicing.Domain;
using Erp.Modules.Invoicing.Features;
using Erp.Shared.BuildingBlocks;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using static Erp.Modules.Invoicing.Infrastructure.InvoicePdf;

namespace Erp.Modules.Invoicing.Infrastructure;

// The credit memo the customer keeps: the seller as on the invoice, the invoice it reduces, why, what is credited
// and its VAT, the way the invoice showed them.
internal static class CreditMemoPdf
{
    public static byte[] Render(CreditMemoDetailDto m, bool reprint)
    {
        var regime = Enum.Parse<TaxRegime>(m.TaxRegime);
        var doc = new Document();
        doc.Info.Title = $"Credit Memo {m.Number}";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = ErpFontResolver.Family;
        normal.Font.Size = 9;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.5);

        if (reprint) Line(section, "REPRINT", 14, bold: true, ParagraphAlignment.Right);
        Line(section, m.Seller.Name, 14, bold: true);
        if (m.Seller.TradeName is not null) Line(section, m.Seller.TradeName);
        Line(section, $"{(regime == TaxRegime.Vat ? "VAT" : "Non-VAT")} Reg TIN {m.Seller.Tin}");
        Line(section, m.Seller.Address);
        Line(section, "CREDIT MEMO", 16, bold: true).Format.SpaceBefore = Unit.FromPoint(12);

        var info = section.AddTable();
        info.AddColumn(Unit.FromCentimeter(10));
        info.AddColumn(Unit.FromCentimeter(7.4));
        var row = info.AddRow();
        row.TopPadding = Unit.FromPoint(8);
        row.Cells[0].AddParagraph("Customer:").Format.Font.Bold = true;
        row.Cells[0].AddParagraph($"Registered name: {m.CustomerName}");
        row.Cells[0].AddParagraph($"TIN: {m.CustomerTin ?? "____________________"}");
        row.Cells[0].AddParagraph($"Address: {m.CustomerAddress ?? "____________________"}");
        var number = row.Cells[1].AddParagraph($"No. {m.Number}");
        number.Format.Font.Size = 13;
        number.Format.Font.Bold = true;
        row.Cells[1].AddParagraph($"Date: {m.IssueDate:MMMM d, yyyy}");
        row.Cells[1].AddParagraph($"Against invoice: {m.InvoiceNumber}");
        section.AddParagraph($"Reason: {m.Reason}").Format.SpaceBefore = Unit.FromPoint(8);
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);

        var lines = section.AddTable();
        lines.Borders.Width = 0.5;
        foreach (var width in new[] { 7.4, 2.0, 2.0, 3.0, 3.0 }) lines.AddColumn(Unit.FromCentimeter(width));
        var head = lines.AddRow();
        head.HeadingFormat = true;
        head.Format.Font.Bold = true;
        Cells(head, "Description", "Qty", "Unit", "Unit price", "Amount");
        foreach (var l in m.Lines)
            Cells(lines.AddRow(), Describe(l.Description, l.TaxType, regime), l.Quantity.ToString("0.###"), l.Unit, Peso(l.UnitPrice), Peso(l.LineTotal));

        var totals = section.AddTable();
        totals.AddColumn(Unit.FromCentimeter(14.4));
        totals.AddColumn(Unit.FromCentimeter(3.0));
        if (regime == TaxRegime.Vat)
        {
            Total(totals, "VATable Sales credited", m.VatableSales);
            Total(totals, $"VAT credited ({Percent(m.VatRate)})", m.VatAmount);
            Total(totals, "Zero-Rated Sales credited", m.ZeroRatedSales);
            Total(totals, "VAT-Exempt Sales credited", m.VatExemptSales);
        }
        if (m.SpecialDiscountAmount > 0)
        {
            if (m.SpecialDiscountVatExemption > 0) Total(totals, "Less: VAT exemption (special discount)", m.SpecialDiscountVatExemption);
            Total(totals, "Less: special discount", m.SpecialDiscountAmount);
        }
        Total(totals, "Total credited", m.Total, bold: true);
        Total(totals, $"Applied to invoice {m.InvoiceNumber}", m.AppliedToInvoice);
        if (m.RefundDue > 0) Total(totals, "To be refunded", m.RefundDue);

        var footer = section.Footers.Primary;
        footer.Format.Font.Size = 8;
        if (m.Seller.PermitNumber is not null) footer.AddParagraph($"BIR Permit to Use / Acknowledgement Certificate Control No.: {m.Seller.PermitNumber}");

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }
}
