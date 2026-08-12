using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using Aprillz.MewUI.TaskManager.Sample;

if (args.Contains("--resource-probe", StringComparer.Ordinal))
{
    var sampler = new SystemSampler();
    _ = sampler.CapturePerformance(sampler.CaptureProcesses());
    Thread.Sleep(350);
    var processes = sampler.CaptureProcesses();
    var performance = sampler.CapturePerformance(processes);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        os = Environment.OSVersion.ToString(),
        architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
        elevated = PrivilegeService.IsElevated,
        processCount = performance.ProcessCount,
        parentLinks = processes.Count(process => process.ParentProcessId > 0),
        accessibleProcesses = processes.Count(process => process.IsAccessible),
        cpuPercent = performance.CpuPercent,
        logicalProcessorCount = performance.LogicalProcessorPercents.Count,
        logicalProcessorPercents = performance.LogicalProcessorPercents,
        kernelPercent = performance.KernelPercent,
        logicalProcessorKernelPercents = performance.LogicalProcessorKernelPercents,
        usedMemoryBytes = performance.UsedMemoryBytes,
        totalMemoryBytes = performance.TotalMemoryBytes,
        threadCount = performance.ThreadCount,
        uptimeSeconds = performance.Uptime.TotalSeconds,
    }));
    return;
}

RegisterPlatformAndBackend();

var view = new TaskManagerView();
var window = new Window()
    .Padding(0)
    .Resizable(1280, 800, minWidth: 920, minHeight: 620)
    .StartCenterScreen()
    .Content(view)
    .OnLoaded(view.Start)
    .OnClosed(view.Dispose);
window.Title = "Task Manager";

Application.Run(window);

static void RegisterPlatformAndBackend()
{
    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();
        Direct2DBackend.Register();
    }
    else if (OperatingSystem.IsLinux())
    {
        X11Platform.Register();
        MewVGX11Backend.Register();
    }
    else if (OperatingSystem.IsMacOS())
    {
        MacOSPlatform.Register();
        MewVGMacOSBackend.Register();
    }
    else
    {
        throw new PlatformNotSupportedException();
    }
}
