using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;

using Svg;

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MewUI.Svg.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class SvgFilterCacheLifetimeTests
{
    private const string FilteredSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <defs>
            <filter id="blur" x="-25%" y="-25%" width="150%" height="150%">
              <feGaussianBlur stdDeviation="3" />
            </filter>
          </defs>
          <rect x="12" y="12" width="40" height="40" fill="#3366cc" filter="url(#blur)" />
        </svg>
        """;

    [TestMethod]
    public void Render_PreCanceledTokenStopsBeforeAllocatingFilterResources()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var factory = GetFactory();
        var cache = (RenderResourceCache)factory.ResourceCache!;
        var baseline = cache.GetStatistics();
        var document = SvgDocument.Parse(FilteredSvg);
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(96, 96, 1));
        using var context = factory.CreateContext(surface);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        context.BeginFrame(surface);
        try
        {
            Assert.ThrowsExactly<OperationCanceledException>(() =>
                document.Render(context, new Rect(0, 0, 96, 96), cancellation.Token));
        }
        finally
        {
            context.EndFrame();
        }

        Assert.AreEqual(baseline.PersistentCount, cache.GetStatistics().PersistentCount);
        Assert.AreEqual(baseline.PersistentBytes, cache.GetStatistics().PersistentBytes);
    }

    [TestMethod]
    public async Task Render_CancellationStopsBlockedExternalElementRequest()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var document = SvgDocument.Parse($$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32">
              <rect width="32" height="32"
                    clip-path="url(http://127.0.0.1:{{port}}/blocked.svg#clip)"
                    fill="#3366cc" />
            </svg>
            """);
        using var cancellation = new CancellationTokenSource();
        var renderTask = Task.Run(() =>
            Render(document, GetFactory(), cancellation.Token));

        using var connection = await listener.AcceptTcpClientAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await connection.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: image/svg+xml\r\nContent-Length: 1048576\r\n\r\n"));
        cancellation.Cancel();

        OperationCanceledException? cancellationException = null;
        try
        {
            await renderTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException ex)
        {
            cancellationException = ex;
        }
        Assert.IsNotNull(cancellationException);
    }

    [TestMethod]
    public async Task Render_CancellationStopsBlockedExternalImageRequest()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var document = SvgDocument.Parse($$"""
            <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32">
              <image width="32" height="32"
                     href="http://127.0.0.1:{{port}}/blocked.png" />
            </svg>
            """);
        using var cancellation = new CancellationTokenSource();
        var renderTask = Task.Run(() =>
            Render(document, GetFactory(), cancellation.Token));

        using var connection = await listener.AcceptTcpClientAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await connection.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 1048576\r\n\r\n"));
        cancellation.Cancel();

        OperationCanceledException? cancellationException = null;
        try
        {
            await renderTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException ex)
        {
            cancellationException = ex;
        }
        Assert.IsNotNull(cancellationException);
    }

    [TestMethod]
    public void FilterCache_HitDoesNotGrowAndDocumentInvalidationReleasesOwnership()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var factory = GetFactory();
        var previousFactory = factory;
        try
        {
            var cache = (RenderResourceCache)factory.ResourceCache!;
            var baseline = cache.GetStatistics();
            var document = SvgDocument.Parse(FilteredSvg);

            Render(document, factory);
            var afterMiss = cache.GetStatistics();
            Assert.AreEqual(baseline.PersistentCount + 1, afterMiss.PersistentCount);

            Render(document, factory);
            var afterHit = cache.GetStatistics();
            Assert.AreEqual(afterMiss.PersistentCount, afterHit.PersistentCount);
            Assert.AreEqual(afterMiss.PersistentBytes, afterHit.PersistentBytes);

            document.InvalidateRenderCaches();
            cache.Trim(RenderCacheTrimReason.Manual);
            var afterClear = cache.GetStatistics();
            Assert.AreEqual(baseline.PersistentCount, afterClear.PersistentCount);
            Assert.AreEqual(baseline.PersistentBytes, afterClear.PersistentBytes);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void FilterCache_NewGenerationReplacesReleasedGeneration()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var factory = GetFactory();
        var previousFactory = factory;
        try
        {
            var cache = (RenderResourceCache)factory.ResourceCache!;
            var baseline = cache.GetStatistics();
            var document = SvgDocument.Parse(FilteredSvg);

            Render(document, factory);
            var firstKey = cache.SnapshotPersistentKeys().Single(
                static key => key.Kind == RenderCacheEntryKind.FilterResult);
            document.InvalidateRenderCaches();
            Render(document, factory);

            var replaced = cache.GetStatistics();
            Assert.AreEqual(baseline.PersistentCount + 1, replaced.PersistentCount);
            var secondKey = cache.SnapshotPersistentKeys().Single(
                static key => key.Kind == RenderCacheEntryKind.FilterResult);
            var firstScope = firstKey.Scope!.Split(':');
            var secondScope = secondKey.Scope!.Split(':');
            CollectionAssert.AreEqual(firstScope[..4], secondScope[..4]);
            Assert.AreNotEqual(firstScope[4], secondScope[4]);
            Assert.AreNotEqual(firstKey.SourceVersion, secondKey.SourceVersion);
            Assert.AreEqual(firstKey.DeviceId, secondKey.DeviceId);
            Assert.AreEqual(firstKey.ContextId, secondKey.ContextId);

            document.InvalidateRenderCaches();
            cache.Trim(RenderCacheTrimReason.Manual);
            Assert.AreEqual(baseline.PersistentCount, cache.GetStatistics().PersistentCount);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void DocumentInvalidation_RetiresEntryWithoutInvalidatingActiveRenderLease()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var factory = GetFactory();
        var cache = (RenderResourceCache)factory.ResourceCache!;
        var baselineCache = cache.GetStatistics();
        var baselineLedger = RenderMemoryLedger.Snapshot();
        var document = SvgDocument.Parse(FilteredSvg);

        Render(document, factory);
        var key = cache.SnapshotPersistentKeys().Single(
            static candidate => candidate.Kind == RenderCacheEntryKind.FilterResult);
        Assert.IsTrue(cache.TryGet(key, out var activeRenderLease));

        document.InvalidateRenderCaches();

        Assert.AreEqual(baselineCache.PersistentCount, cache.GetStatistics().PersistentCount);
        Assert.IsGreaterThan(0, activeRenderLease.Image.PixelWidth);
        Assert.AreEqual(
            baselineLedger.PersistentResourceCount + 1,
            RenderMemoryLedger.Snapshot().PersistentResourceCount);

        activeRenderLease.Dispose();
        Assert.AreEqual(
            baselineLedger.PersistentResourceCount,
            RenderMemoryLedger.Snapshot().PersistentResourceCount);
    }

    [TestMethod]
    public void DocumentInvalidation_ReleasesEmbeddedRasterRealization()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16">
              <image width="16" height="16"
                     href="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" />
            </svg>
            """;
        var factory = GetFactory();
        var previousFactory = factory;
        try
        {
            var baseline = RenderMemoryLedger.Snapshot();
            var document = SvgDocument.Parse(svg);

            Render(document, factory);
            Assert.AreEqual(
                baseline.NativeImageRealizationCount + 1,
                RenderMemoryLedger.Snapshot().NativeImageRealizationCount);

            document.InvalidateRenderCaches();
            Assert.AreEqual(
                baseline.NativeImageRealizationCount,
                RenderMemoryLedger.Snapshot().NativeImageRealizationCount);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private static void Render(
        SvgDocument document,
        GdiGraphicsFactory factory,
        CancellationToken cancellationToken = default)
    {
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(96, 96, 1));
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        try
        {
            document.Render(context, new Rect(0, 0, 96, 96), cancellationToken);
        }
        finally
        {
            context.EndFrame();
        }
    }

    private static GdiGraphicsFactory GetFactory() =>
        (GdiGraphicsFactory)Application.DefaultGraphicsFactory;
}
