using System.Windows.Media;
using XRedactor.Models;

namespace XRedactor;

public static class FontSizing
{
    public const double GridStep = 40;
    public const double RuledLineStep = 40;

    public static double GetDefaultDisplayFontSize(NotebookTemplate template) =>
        template switch
        {
            NotebookTemplate.Ruled => 1,
            NotebookTemplate.Blank or NotebookTemplate.SpiralTop or NotebookTemplate.SpiralLeft or NotebookTemplate.SpiralBottom => 18,
            _ => 2
        };

    public static double GetDisplayFontSize(NotebookTemplate template, double pixelSize, string fontFamily = "Segoe UI") =>
        template switch
        {
            NotebookTemplate.Lined => pixelSize * GetLineSpacing(fontFamily) / GridStep,
            NotebookTemplate.Ruled => pixelSize * GetLineSpacing(fontFamily) / RuledLineStep,
            _ => pixelSize * 3 / 4
        };

    public static double GetPixelFontSize(NotebookTemplate template, double displaySize, string fontFamily = "Segoe UI") =>
        template switch
        {
            NotebookTemplate.Lined => displaySize * GridStep / GetLineSpacing(fontFamily),
            NotebookTemplate.Ruled => displaySize * RuledLineStep / GetLineSpacing(fontFamily),
            _ => displaySize * 4 / 3
        };

    private static double GetLineSpacing(string fontFamily)
    {
        try
        {
            return new FontFamily(fontFamily).LineSpacing;
        }
        catch
        {
            return new FontFamily("Segoe UI").LineSpacing;
        }
    }
}
