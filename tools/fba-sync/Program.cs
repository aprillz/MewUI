using Aprillz.MewUI.FbaSync;

// The host template stands in for this one; the file-based app downloads what it loads from disk.
string[] excludedFiles =
[
    "Program.cs",
];

try
{
    string repoRoot = Locate.RepoRoot(AppContext.BaseDirectory);
    string galleryDir = Path.Combine(repoRoot, "samples", "MewUI.Gallery");
    string templateDir = Locate.TemplateDir(AppContext.BaseDirectory, repoRoot);
    string output = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "samples", "FBASample", "fba_gallery.cs");

    var reader = new GalleryReader();
    var sources = Directory.GetFiles(galleryDir, "*.cs")
        .Where(path => !excludedFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal))
        .OrderBy(Path.GetFileName, StringComparer.Ordinal)
        .ToList();

    foreach (var source in sources)
    {
        reader.Read(source);
    }

    string text = Assemble.File(templateDir, reader);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, text);

    Console.WriteLine($"{Path.GetFileName(output)}: {text.Split('\n').Length} lines, {reader.Types.Count} types from {sources.Count} gallery files");
    return 0;
}
catch (FbaSyncException error)
{
    Console.Error.WriteLine($"fba-sync: {error.Message}");
    return 1;
}
