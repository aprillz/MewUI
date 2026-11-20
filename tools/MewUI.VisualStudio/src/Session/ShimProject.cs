using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Aprillz.MewUI.VisualStudio.Session
{
    /// <summary>Generates the shim fallback project (app reference, no app Main) under the app's obj folder.</summary>
    internal static class ShimProject
    {
        private const int QUERY_TIMEOUT_MS = 30000;

        internal static string Generate(string projectPath)
        {
            string shimDir = Path.Combine(Path.GetDirectoryName(projectPath), "obj", "mewui-preview", "shim");
            Directory.CreateDirectory(shimDir);

            string targetFramework = QueryTargetFramework(projectPath) ?? "net8.0";
            string referencePath = Path.GetFullPath(projectPath).Replace('\\', '/');
            File.WriteAllText(Path.Combine(shimDir, "MewUIPreviewShim.csproj"), $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{targetFramework}</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include=""{referencePath}"" />
  </ItemGroup>

</Project>
");
            File.WriteAllText(Path.Combine(shimDir, "Program.cs"), SHIM_PROGRAM_SOURCE);
            return Path.Combine(shimDir, "MewUIPreviewShim.csproj");
        }

        private static string QueryTargetFramework(string projectPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo(
                    "dotnet",
                    $"msbuild {CommandLine.Quote(projectPath)} -getProperty:TargetFramework -getProperty:TargetFrameworks")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var output = new StringBuilder();
                // Not disposed: a reader callback may still signal it after a timeout return.
                var outputClosed = new ManualResetEventSlim();
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (_, args) =>
                    {
                        if (args.Data == null)
                        {
                            outputClosed.Set();
                        }
                        else
                        {
                            lock (output)
                            {
                                output.AppendLine(args.Data);
                            }
                        }
                    };
                    process.ErrorDataReceived += (_, __) => { };
                    process.Start();
                    ChildProcessJob.TryAssign(process);
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(QUERY_TIMEOUT_MS))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // Exited between the timeout and the kill.
                        }
                        return null;
                    }
                    if (process.ExitCode != 0 || !outputClosed.Wait(QUERY_TIMEOUT_MS))
                    {
                        return null;
                    }
                    string text;
                    lock (output)
                    {
                        text = output.ToString();
                    }
                    JObject properties = (JObject)JObject.Parse(text)["Properties"];
                    string single = ((string)properties?["TargetFramework"])?.Trim();
                    if (!string.IsNullOrEmpty(single))
                    {
                        return single;
                    }
                    return ((string)properties?["TargetFrameworks"])?
                        .Split(';')
                        .Select(tfm => tfm.Trim())
                        .FirstOrDefault(tfm => tfm.Length > 0);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // The generated shim entry point: loads every managed assembly in the output folder (forcing
        // module initializers so the injected preview bootstrap registers), registers the per-OS default
        // platform/backend reflectively, and runs an empty window. App themes/fonts configured in the
        // real Main are absent. Loading by folder rather than by reference walk is deliberate: the shim
        // code never uses app types, so the compiler drops the app AssemblyRef entirely.
        private const string SHIM_PROGRAM_SOURCE = @"// Auto-generated by the MewUI preview extension for shim fallback sessions. Do not edit.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

internal static class Program
{
    private static void Main()
    {
        LoadLocalAssemblies();
        RegisterPlatform();
        Application.Run(new Window { Title = ""MewUI preview shim"" });
    }

    private static void LoadLocalAssemblies()
    {
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, ""*.dll""))
        {
            try
            {
                var loaded = Assembly.Load(AssemblyName.GetAssemblyName(file));
                // Force module initializers so the preview bootstrap injected into the app
                // assembly registers even though the shim never calls into app code.
                RuntimeHelpers.RunModuleConstructor(loaded.ManifestModule.ModuleHandle);
            }
            catch
            {
                // Native or otherwise unloadable images; the scan proceeds with what did load.
            }
        }
    }

    private static void RegisterPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            Register(""Aprillz.MewUI.Platform.Win32"", ""Aprillz.MewUI.Win32Platform"");
            Register(""Aprillz.MewUI.Backend.Gdi"", ""Aprillz.MewUI.GdiBackend"");
        }
        else if (OperatingSystem.IsLinux())
        {
            Register(""Aprillz.MewUI.Platform.X11"", ""Aprillz.MewUI.X11Platform"");
            Register(""Aprillz.MewUI.Backend.MewVG.X11"", ""Aprillz.MewUI.MewVGX11Backend"");
        }
        else if (OperatingSystem.IsMacOS())
        {
            Register(""Aprillz.MewUI.Platform.MacOS"", ""Aprillz.MewUI.MacOSPlatform"");
            Register(""Aprillz.MewUI.Backend.MewVG.MacOS"", ""Aprillz.MewUI.MewVGMacOSBackend"");
        }
    }

    private static void Register(string assemblyName, string typeName)
    {
        try
        {
            Assembly.Load(assemblyName)
                .GetType(typeName)?
                .GetMethod(""Register"", BindingFlags.Public | BindingFlags.Static)?
                .Invoke(null, null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(""[mewui-preview-shim] "" + typeName + "" registration failed: "" + ex.Message);
        }
    }
}
";
    }
}
