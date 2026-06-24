using System.IO;
using System.Text.Json;
using XRedactor.Models;

namespace XRedactor.Services;

public sealed class DocumentStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DocumentStore()
    {
        DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Notebooks");
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public IReadOnlyList<DocumentEntry> ListDocuments()
    {
        var entries = new List<DocumentEntry>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.xred"))
        {
            try
            {
                var document = Load(path);
                entries.Add(new DocumentEntry(path, document.Name, document.Kind, document.ModifiedUtc));
            }
            catch
            {
                entries.Add(new DocumentEntry(path, Path.GetFileNameWithoutExtension(path), null, File.GetLastWriteTimeUtc(path), true));
            }
        }

        return entries.OrderByDescending(entry => entry.ModifiedUtc).ToList();
    }

    public XRedDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<XRedDocument>(json, _jsonOptions)
            ?? throw new InvalidDataException("The document is empty.");
        Validate(document);
        return document;
    }

    public string Save(XRedDocument document, string? existingPath = null)
    {
        Validate(document);
        document.ModifiedUtc = DateTime.UtcNow;
        var path = existingPath ?? CreateUniquePath(document.Name);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, _jsonOptions));
        File.Move(temporaryPath, path, true);
        return path;
    }

    public void Delete(string path) => File.Delete(path);

    private string CreateUniquePath(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var stem = new string(name.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "Untitled";
        }

        var path = Path.Combine(DirectoryPath, stem + ".xred");
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(DirectoryPath, $"{stem} {suffix}.xred");
        }

        return path;
    }

    private static void Validate(XRedDocument document)
    {
        if (document.Version is < 1 or > 1)
        {
            throw new InvalidDataException($"Unsupported document version: {document.Version}.");
        }

        if (string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 160)
        {
            throw new InvalidDataException("The document name is invalid.");
        }

        if (document.Kind == DocumentKind.Notebook && document.Notebook is null)
        {
            throw new InvalidDataException("Notebook data is missing.");
        }

        if (document.Kind == DocumentKind.Plane)
        {
            var plane = document.Plane ?? throw new InvalidDataException("Plane data is missing.");
            if (plane.TileWidth is < 320 or > 4000 || plane.TileHeight is < 240 or > 4000)
            {
                throw new InvalidDataException("Canvas dimensions are outside the supported range.");
            }

            if (plane.Tiles.Count is < 1 or > 1000 || plane.Elements.Count > 10000 || plane.Strokes.Count > 50000)
            {
                throw new InvalidDataException("The document contains too many items.");
            }
        }
    }
}

public sealed record DocumentEntry(
    string Path,
    string Name,
    DocumentKind? Kind,
    DateTime ModifiedUtc,
    bool IsDamaged = false);

