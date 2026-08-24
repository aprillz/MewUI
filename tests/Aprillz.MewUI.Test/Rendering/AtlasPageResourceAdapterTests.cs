using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class AtlasPageResourceAdapterTests
{
    [TestMethod]
    public void Adapter_ReusesExactPageAcrossOpaqueContentClasses()
    {
        using var device = new FakeDevice();
        var adapter = new AtlasPageResourceAdapter(device);
        var before = RenderMemoryLedger.Snapshot();
        Assert.IsTrue(adapter.TryAcquire("Vector", out var first));
        Assert.AreEqual(before.AtlasPageCount + 1, RenderMemoryLedger.Snapshot().AtlasPageCount);
        var allocation = ((IBackendSurfaceProvider)first!.Surface).BackendSurface;
        first.Dispose();

        Assert.IsTrue(adapter.TryAcquire("TextRun", out var second));

        Assert.AreEqual(AtlasPageResourceAdapter.DefaultPageExtent, second!.Surface.PixelWidth);
        Assert.AreEqual(AtlasPageResourceAdapter.DefaultPageExtent, second.Surface.PixelHeight);
        Assert.AreSame(allocation, ((IBackendSurfaceProvider)second.Surface).BackendSurface);
        second.Dispose();
        Assert.AreEqual(before.AtlasPageCount, RenderMemoryLedger.Snapshot().AtlasPageCount);
    }

    [TestMethod]
    public void Adapter_FallsBackWhenGenerationChanges()
    {
        using var device = new FakeDevice();
        var adapter = new AtlasPageResourceAdapter(device);
        device.Generation++;

        Assert.IsFalse(adapter.TryAcquire("Vector", out var lease));
        Assert.IsNull(lease);
    }

    [TestMethod]
    public void Adapter_RejectsAdmissionBeyondNativeBudget()
    {
        using var device = new FakeDevice();
        var adapter = new AtlasPageResourceAdapter(device);
        var leases = new List<AtlasPageLease>();
        try
        {
            for (int i = 0; i < 64; i++)
            {
                Assert.IsTrue(adapter.TryAcquire("Vector", out var lease));
                leases.Add(lease!);
            }
            Assert.IsFalse(adapter.TryAcquire("Vector", out var rejected));
            Assert.IsNull(rejected);
        }
        finally
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }
        }
    }

    private sealed class FakeDevice : IRenderDevice, IDisposable
    {
        private readonly ulong _id = RenderDeviceIdentity.AllocateDeviceId();
        private readonly RenderResourceCache _cache = new();

        public uint Generation { get; set; }
        public RenderDeviceIdentity RenderIdentity => new(_id, Generation);
        public IRenderResourceCache ResourceCache => _cache;
        public IRenderEffectDevice? Effects => null;

        public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor)
            => new FakeSurface(descriptor);

        public IGraphicsContext CreateContext(IRenderSurface surface) => throw new NotSupportedException();
        public IImage CreateImageView(IRenderSurface surface) => throw new NotSupportedException();
        public IImage CreateImageView(IPixelBufferSource source) => throw new NotSupportedException();
        public IImage CreateImageView(IExternalRasterSource source) => throw new NotSupportedException();
        public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes) => false;
        public IRenderOperation RequestReadback(IRenderSurface source) => RenderOperation.Completed;
        public IRenderOperation FlushAsyncWork() => RenderOperation.Completed;
        public void Dispose() => _cache.Dispose();
    }

    private sealed class FakeSurface(RenderSurfaceDescriptor descriptor) : IRenderSurface
    {
        public int PixelWidth => descriptor.PixelWidth;
        public int PixelHeight => descriptor.PixelHeight;
        public double DpiScale => descriptor.DpiScale;
        public RenderPixelFormat Format => descriptor.Format;
        public SurfaceUsage Usage => descriptor.Usage;
        public SurfaceCapabilities Capabilities => descriptor.RequiredCapabilities;
        public ulong Version => 0;
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
