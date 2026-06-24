using XRedactor.Models;
using XRedactor.Services;

var store = new DocumentStore();
var testName = $"Serialization Test {Guid.NewGuid():N}";
string? path = null;

try
{
    var source = new XRedDocument
    {
        Name = testName,
        Kind = DocumentKind.Plane,
        Plane = new PlaneData
        {
            TileWidth = 900,
            TileHeight = 600,
            Tiles =
            [
                new CanvasTileData { Column = 0, Row = 0 },
                new CanvasTileData { Column = 1, Row = 0 }
            ],
            Elements =
            [
                new CanvasElementData
                {
                    Kind = ElementKind.NoteSheet,
                    X = 125,
                    Y = -70,
                    Text = "Unicode survives: Привет"
                }
            ]
        }
    };

    path = store.Save(source);
    Assert(File.Exists(path), "Document file was not created.");
    Assert(!File.Exists(path + ".tmp"), "Temporary file remained after atomic save.");

    var loaded = store.Load(path);
    Assert(loaded.Id == source.Id, "Document ID changed during serialization.");
    Assert(loaded.Kind == DocumentKind.Plane, "Document kind changed during serialization.");
    Assert(loaded.Plane?.Tiles.Count == 2, "Canvas tiles were not restored.");
    Assert(loaded.Plane?.Elements.Single().Text == "Unicode survives: Привет", "Element text was not restored.");

    var invalid = new XRedDocument
    {
        Name = "Invalid Test",
        Kind = DocumentKind.Plane,
        Plane = new PlaneData { TileWidth = 100, TileHeight = 100 }
    };
    var rejected = false;
    try
    {
        store.Save(invalid);
    }
    catch (InvalidDataException)
    {
        rejected = true;
    }
    Assert(rejected, "Invalid canvas dimensions were accepted.");

    Console.WriteLine("PASS: document round-trip, atomic save, Unicode, and validation.");
}
finally
{
    if (path is not null && File.Exists(path))
    {
        File.Delete(path);
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
