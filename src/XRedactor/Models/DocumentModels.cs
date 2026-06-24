using System.Text.Json.Serialization;

namespace XRedactor.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentKind
{
    Notebook,
    Plane
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotebookTemplate
{
    Blank,
    Lined,
    SpiralTop,
    SpiralBottom
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ElementKind
{
    Text,
    Rectangle,
    Ellipse,
    NoteSheet
}

public sealed class XRedDocument
{
    public int Version { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public DocumentKind Kind { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public NotebookData? Notebook { get; set; }
    public PlaneData? Plane { get; set; }
}

public sealed class NotebookData
{
    public NotebookTemplate Template { get; set; } = NotebookTemplate.Lined;
    public string RichTextRtfBase64 { get; set; } = string.Empty;
    public List<InkStrokeData> Strokes { get; set; } = [];
}

public sealed class PlaneData
{
    public double TileWidth { get; set; } = 960;
    public double TileHeight { get; set; } = 680;
    public double Zoom { get; set; } = 0.8;
    public double HorizontalOffset { get; set; } = 4400;
    public double VerticalOffset { get; set; } = 4400;
    public List<CanvasTileData> Tiles { get; set; } = [new()];
    public List<CanvasElementData> Elements { get; set; } = [];
    public List<InkStrokeData> Strokes { get; set; } = [];
}

public sealed class CanvasTileData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Column { get; set; }
    public int Row { get; set; }
    public string Background { get; set; } = "#FFFDF8";
}

public sealed class CanvasElementData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ElementKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 140;
    public string Fill { get; set; } = "#FFFFF2B8";
    public string Stroke { get; set; } = "#FF34313A";
    public double StrokeThickness { get; set; } = 2;
    public string Text { get; set; } = string.Empty;
    public string TextColor { get; set; } = "#FF242128";
    public double FontSize { get; set; } = 18;
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderlined { get; set; }
    public int ZIndex { get; set; }
}

public sealed class InkStrokeData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Color { get; set; } = "#FF2A2730";
    public double Thickness { get; set; } = 3;
    public List<InkPointData> Points { get; set; } = [];
}

public sealed class InkPointData
{
    public double X { get; set; }
    public double Y { get; set; }
    public float Pressure { get; set; } = 0.5f;
}

