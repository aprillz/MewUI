using System.Reflection;

namespace Aprillz.MewUI.Platform.Linux;

/// <summary>The names a Linux desktop uses to group an application's windows, tray items and launcher entry.</summary>
internal static class LinuxApplicationIdentity
{
    /// <summary>The application identifier, or the entry assembly's name when the application set none.</summary>
    public static string Id => Application.ApplicationId ?? Assembly.GetEntryAssembly()?.GetName().Name ?? InstanceName;

    /// <summary>The name of the running program.</summary>
    public static string InstanceName
    {
        get
        {
            string? name = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
            return string.IsNullOrEmpty(name) ? "mewui" : name;
        }
    }
}
