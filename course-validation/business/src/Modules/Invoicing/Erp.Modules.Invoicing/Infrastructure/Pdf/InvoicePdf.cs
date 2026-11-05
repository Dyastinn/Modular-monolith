using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Erp.Modules.Invoicing.Infrastructure;

internal sealed record CompanyInfo(string Name, string? Tin, string? Address, bool VatRegistered);

internal sealed record InvoicePdfModel(
    string Number, DateOnly IssueDate, DateOnly DueDate, string OrderNumber, string Status,
    string CustomerName, string? CustomerTin, string? CustomerAddress,
    IReadOnlyList<(string Description, string Unit, decimal Quantity, decimal UnitPrice, decimal LineTotal)> Lines,
    decimal Total, decimal AmountPaid, decimal Vatable, decimal Vat);

internal static class InvoicePdf
{
    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");

    public static byte[] Render(InvoicePdfModel m, CompanyInfo company)
    {
        var doc = new Document();
        doc.Info.Title = $"Sales Invoice {m.Number}";
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = ErpFontResolver.Family;
        normal.Font.Size = 9;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);

        var header = section.AddParagraph(company.Name);
        header.Format.Font.Size = 14;
        header.Format.Font.Bold = true;
        if (company.Address is not null) section.AddParagraph(company.Address);
        if (company.Tin is not null) section.AddParagraph($"TIN {company.Tin}{(company.VatRegistered ? " · VAT registered" : "")}");

        var title = section.AddParagraph(m.Status == "Void" ? "SALES INVOICE (VOID)" : "SALES INVOICE");
        title.Format.Font.Size = 12;
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromPoint(14);
        title.Format.SpaceAfter = Unit.FromPoint(8);

        var info = section.AddTable();
        info.AddColumn(Unit.FromCentimeter(9));
        info.AddColumn(Unit.FromCentimeter(8.4));
        var row = info.AddRow();
        row.Cells[0].AddParagraph($"Sold to: {m.CustomerName}");
        if (m.CustomerTin is not null) row.Cells[0].AddParagraph($"TIN: {m.CustomerTin}");
        if (m.CustomerAddress is not null) row.Cells[0].AddParagraph(m.CustomerAddress);
        row.Cells[1].AddParagraph($"Invoice no.: {m.Number}");
        row.Cells[1].AddParagraph($"Date: {m.IssueDate:MMMM d, yyyy}");
        row.Cells[1].AddParagraph($"Due: {m.DueDate:MMMM d, yyyy}");
        row.Cells[1].AddParagraph($"Order: {m.OrderNumber}");

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);

        var lines = section.AddTable();
        lines.Borders.Width = 0.5;
        foreach (var width in new[] { 7.4, 2.0, 2.0, 3.0, 3.0 }) lines.AddColumn(Unit.FromCentimeter(width));
        var head = lines.AddRow();
        head.HeadingFormat = true;
        head.Format.Font.Bold = true;
        Cells(head, "Description", "Qty", "Unit", "Unit price", "Amount");
        foreach (var l in m.Lines)
            Cells(lines.AddRow(), l.Description, l.Quantity.ToString("0.###", Ph), l.Unit, Peso(l.UnitPrice), Peso(l.LineTotal));

        var totals = section.AddTable();
        totals.AddColumn(Unit.FromCentimeter(14.4));
        totals.AddColumn(Unit.FromCentimeter(3.0));
        if (company.VatRegistered)
        {
            Total(totals, "VATable sales", m.Vatable);
            Total(totals, "VAT (12%)", m.Vat);
        }
        Total(totals, "Total amount due", m.Total, bold: true);
        if (m.AmountPaid > 0)
        {
            Total(totals, "Less payments received", m.AmountPaid);
            Total(totals, "Balance due", m.Total - m.AmountPaid, bold: true);
        }

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static string Peso(decimal amount) => "PHP " + amount.ToString("N2", Ph);

    private static void Cells(Row row, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var p = row.Cells[i].AddParagraph(values[i]);
            if (i > 0 && i != 2) p.Format.Alignment = ParagraphAlignment.Right;
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
