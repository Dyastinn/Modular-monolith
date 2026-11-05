using PdfSharp.Fonts;

namespace Erp.Modules.Invoicing.Infrastructure;

// PDFsharp embeds the font it draws with, so it needs the font file. One family, regular and bold, found on the
// machine: DejaVu Sans in the Docker image (fonts-dejavu-core), Arial on Windows, or paths from configuration.
// The fonts come with the operating system, so no font file ships in this source.
internal sealed class ErpFontResolver(string? regularPath, string? boldPath) : IFontResolver
{
    public const string Family = "ErpSans";

    private static readonly string[] RegularCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
    ];
    private static readonly string[] BoldCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf"),
    ];

    private readonly Lazy<byte[]> _regular = new(() => Load(regularPath, RegularCandidates));
    private readonly Lazy<byte[]> _bold = new(() => Load(boldPath, BoldCandidates));

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "ErpSans-Bold" : "ErpSans-Regular");

    public byte[] GetFont(string faceName) => faceName == "ErpSans-Bold" ? _bold.Value : _regular.Value;

    private static byte[] Load(string? configured, string[] candidates)
    {
        var path = new[] { configured }.Concat(candidates).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            ?? throw new InvalidOperationException(
                "No font found for invoice PDFs. Install fonts-dejavu-core, or set Invoicing:FontPath and Invoicing:BoldFontPath.");
        return File.ReadAllBytes(path);
    }
}
