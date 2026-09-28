using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

using Aprillz.MewUI.TaskManager.Sample;

// The probe serializes anonymous types by reflection, which a native AOT build does not have.
#if !MEWUI_TASKMANAGER_AOT
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
        kernelPercent = performance.KernelPercent,
        threadCount = performance.ThreadCount,
        handleCount = performance.HandleCount,
        uptimeSeconds = performance.Uptime.TotalSeconds,
        topMemory = processes.OrderByDescending(process => process.MemoryBytes).Take(8)
            .Select(process => new { process.Name, process.ProcessId, process.MemoryBytes, process.CpuPercent, process.DiskBytesPerSecond }),
        resources = performance.Resources.Select(resource => new
        {
            resource.Id,
            resource.Title,
            resource.Subtitle,
            resource.Summary,
            resource.Heading,
            chart = resource.Chart,
            secondChart = resource.SecondChart,
            metrics = resource.Metrics.Select(metric => $"{metric.Label}: {metric.Value}"),
            properties = resource.Properties.Select(metric => $"{metric.Label}: {metric.Value}"),
            composition = resource.Composition?.Select(part => $"{part.Label}: {part.Bytes}"),
        }),
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    sampler.Dispose();
    return;
}
#endif

RegisterPlatformAndBackend(args);

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

static void RegisterPlatformAndBackend(string[] args)
{
#if MEWUI_TASKMANAGER_WIN
#pragma warning disable CA1416
    Win32Platform.Register();
    MewVGWin32Backend.Register();
#pragma warning restore CA1416
#elif MEWUI_TASKMANAGER_LINUX
    X11Platform.Register();
    MewVGX11Backend.Register();
#elif MEWUI_TASKMANAGER_OSX
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
#else
    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();
        if (args.Any(x => x.Equals("--vg", StringComparison.OrdinalIgnoreCase)))
        {
            MewVGWin32Backend.Register();
        }
        else
        {
            Direct2DBackend.Register();
        }
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
#endif
}
