using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Runtime.InteropServices;

var options = Options.Parse(args);
string repo = Path.GetFullPath(options.Repo);

if (options.ManifestOnly)
{
    Console.WriteLine(ComputeManifest(repo));
    return;
}

string output = Path.GetFullPath(options.Output ?? throw new ArgumentException("--output is required."));
string reportPath = Path.GetFullPath(options.Report ?? Path.Combine(output, "report.json"));
Directory.CreateDirectory(output);

var platform = CurrentPlatform();
string rid = platform switch
{
    "windows" when RuntimeInformation.OSArchitecture == Architecture.X64 => "win-x64",
    "linux" when RuntimeInformation.OSArchitecture == Architecture.X64 => "linux-x64",
    "macos" when RuntimeInformation.OSArchitecture == Architecture.Arm64 => "osx-arm64",
    _ => throw new PlatformNotSupportedException(
        $"Release size measurement does not support {platform}/{RuntimeInformation.OSArchitecture}.")
};

string version = ReadVersion(repo);
string sdk = RunCapture(options.DotNet, "--version", repo).Trim();
var backends = platform == "windows" ? new[] { "Gdi", "Direct2D", "MewVG" } : new[] { "MewVG" };
var entries = new List<Measurement>();

foreach (string backend in backends)
{
    entries.Add(Measure(repo, output, options.DotNet, rid, platform, backend, "Hello World"));
    entries.Add(Measure(repo, output, options.DotNet, rid, platform, backend, "Gallery"));
}

var report = new PlatformReport(1, version, DateTime.UtcNow, sdk, rid, ComputeManifest(repo), entries);
WriteJson(reportPath, report);
Console.WriteLine(reportPath);

static Measurement Measure(
    string repo,
    string outputRoot,
    string dotnet,
    string rid,
    string platform,
    string backend,
    string sample)
{
    string slug = $"{(sample == "Gallery" ? "gallery" : "hello-world")}-{rid}-{backend.ToLowerInvariant()}";
    string publishDir = Path.Combine(outputRoot, slug);
    if (Directory.Exists(publishDir))
    {
        Directory.Delete(publishDir, recursive: true);
    }
    Directory.CreateDirectory(publishDir);

    string project = sample == "Gallery"
        ? Path.Combine(repo, "samples", "MewUI.Gallery", "MewUI.Gallery.csproj")
        : Path.Combine(repo, "tools", "aot-size", "MewUI.AotSizeProbe", "MewUI.AotSizeProbe.csproj");
    string platformDefine = platform switch
    {
        "windows" => "MEWUI_PLATFORM_WIN32",
        "linux" => "MEWUI_PLATFORM_LINUX",
        _ => "MEWUI_PLATFORM_MACOS"
    };
    string backendDefine = backend switch
    {
        "Gdi" => "MEWUI_BACKEND_GDI",
        "Direct2D" => "MEWUI_BACKEND_DIRECT2D",
        _ => "MEWUI_BACKEND_MEWVG"
    };

    var arguments = new List<string>
    {
        "publish", project,
        "-c", "Release",
        "-r", rid,
        "--self-contained", "true",
        "-p:PublishAot=true",
        "-p:TrimMode=full",
        "-p:IlcOptimizationPreference=Size",
        "-p:InvariantGlobalization=true",
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        "-p:CopyOutputSymbolsToPublishDirectory=false",
        "-p:StripSymbols=true",
        "-p:IlcGeneratePdb=false",
        "-p:IlcGenerateMapFile=false",
        "-p:UseSharedCompilation=false",
        "-p:MewVGProjectPath=",
        $"-p:MewUIBackend={backend}",
        $"-p:PublishDir={publishDir}{Path.DirectorySeparatorChar}"
    };
    if (sample != "Gallery")
    {
        arguments.Add("-p:AotSizeProbe=Text");
        arguments.Add($"-p:AotSizePlatformDefine={platformDefine}");
        arguments.Add($"-p:AotSizeBackendDefine={backendDefine}");
    }

    Run(dotnet, arguments, repo);

    string executableName = sample == "Gallery" ? "Aprillz.MewUI.Gallery" : "MewUI.AotSizeProbe";
    if (platform == "windows")
    {
        executableName += ".exe";
    }
    string executable = Path.Combine(publishDir, executableName);
    if (!File.Exists(executable))
    {
        throw new FileNotFoundException("Published executable was not found.", executable);
    }

    long executableBytes = new FileInfo(executable).Length;
    long compressedBytes = MeasureZip(executable);
    string platformBackend = platform switch
    {
        "windows" => $"Windows x64 / {(backend == "Gdi" ? "GDI" : backend)}",
        "linux" => "Linux x64 / X11 + MewVG",
        _ => "macOS arm64 / MewVG"
    };
    return new Measurement(sample, platformBackend, backend, executableBytes, compressedBytes);
}

static long MeasureZip(string executable)
{
    using var buffer = new MemoryStream();
    using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = archive.CreateEntry(Path.GetFileName(executable), CompressionLevel.SmallestSize);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream destination = entry.Open();
        using FileStream source = File.OpenRead(executable);
        source.CopyTo(destination);
    }
    return buffer.Length;
}

static string ComputeManifest(string repo)
{
    string[] roots = ["assets", "build", "src", "samples/MewUI.Gallery", "tools/aot-size/MewUI.AotSizeProbe", "tools/aot-size/MewUI.ReleaseSizeTool"];
    var files = roots
        .Select(root => Path.Combine(repo, root.Replace('/', Path.DirectorySeparatorChar)))
        .Where(Directory.Exists)
        .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
        .Where(path => !Path.GetRelativePath(repo, path).Equals(
            Path.Combine("build", "MewUI.Local.props"), StringComparison.OrdinalIgnoreCase))
        .Where(path => !Path.GetExtension(path).Equals(".user", StringComparison.OrdinalIgnoreCase))
        .Where(path => !Path.GetFileName(path).Equals(".DS_Store", StringComparison.OrdinalIgnoreCase))
        .Where(path => !Path.GetFileName(path).Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => Path.GetRelativePath(repo, path).Replace('\\', '/'), StringComparer.Ordinal)
        .ToArray();

    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (string file in files)
    {
        string relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
        hash.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
        hash.AppendData(File.ReadAllBytes(file));
    }
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}

static string ReadVersion(string repo)
{
    var document = XDocument.Load(Path.Combine(repo, "build", "MewUI.Common.props"));
    return document.Descendants("MewUIVersion").Single().Value.Trim();
}

static string CurrentPlatform()
    => OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsMacOS() ? "macos"
        : throw new PlatformNotSupportedException();

static void Run(string fileName, IEnumerable<string> arguments, string workingDirectory)
{
    using var process = CreateProcess(fileName, arguments, workingDirectory, redirect: false);
    process.Start();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"'{fileName}' exited with code {process.ExitCode}.");
    }
}

static string RunCapture(string fileName, string argument, string workingDirectory)
{
    using var process = CreateProcess(fileName, [argument], workingDirectory, redirect: true);
    process.Start();
    string output = process.StandardOutput.ReadToEnd();
    string error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(error);
    }
    return output;
}

static Process CreateProcess(string fileName, IEnumerable<string> arguments, string workingDirectory, bool redirect)
{
    var info = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, UseShellExecute = false };
    foreach (string argument in arguments)
    {
        info.ArgumentList.Add(argument);
    }
    info.RedirectStandardOutput = redirect;
    info.RedirectStandardError = redirect;
    return new Process { StartInfo = info };
}

static void WriteJson<T>(string path, T value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json + "\n", new UTF8Encoding(false));
}

sealed record Measurement(string Sample, string PlatformBackend, string Backend, long ExecutableBytes, long CompressedBytes);
sealed record PlatformReport(int SchemaVersion, string MewUIVersion, DateTime MeasuredAtUtc, string DotnetSdk, string RuntimeIdentifier, string SourceManifest, IReadOnlyList<Measurement> Entries);

sealed record Options(string Repo, string? Output, string? Report, string DotNet, bool ManifestOnly)
{
    public static Options Parse(string[] args)
    {
        string? repo = null, output = null, report = null;
        string dotnet = "dotnet";
        bool manifestOnly = false;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo": repo = args[++index]; break;
                case "--output": output = args[++index]; break;
                case "--report": report = args[++index]; break;
                case "--dotnet": dotnet = args[++index]; break;
                case "--manifest-only": manifestOnly = true; break;
                default: throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }
        return new Options(repo ?? throw new ArgumentException("--repo is required."), output, report, dotnet, manifestOnly);
    }
}
