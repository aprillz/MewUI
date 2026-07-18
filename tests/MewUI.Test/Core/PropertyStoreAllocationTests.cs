using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Core;

/// <summary>
/// Deterministic allocation gate for the value store's hot paths (the "Tier 1" gate from the 06-B
/// design). Reads and single-source sets of a reference-typed property must allocate nothing per op.
/// These pass against today's single-slot store and become the regression detector the multi-slot
/// rewrite must not break: a naive multi-slot store that computes the effective value on every read
/// or allocates a slot set for a single-source write would fail here.
/// Uses <see cref="GC.GetAllocatedBytesForCurrentThread"/>, which is per-thread and so unaffected by
/// other tests running in parallel.
/// </summary>
[TestClass]
public sealed class PropertyStoreAllocationTests
{
    private sealed class AllocOwner : Control
    {
        public static readonly MewProperty<Brush?> A =
            MewProperty<Brush?>.Register<AllocOwner>("A", null, MewPropertyOptions.None);
        public static readonly MewProperty<Brush?> B =
            MewProperty<Brush?>.Register<AllocOwner>("B", null, MewPropertyOptions.None);
    }

    private static readonly Brush BrushA = new SolidColorBrush(Color.FromArgb(255, 1, 2, 3));
    private static readonly Brush BrushB = new SolidColorBrush(Color.FromArgb(255, 4, 5, 6));
    private static Brush? _sink;

    private const int Iterations = 20_000;

    [TestMethod]
    public void GetValue_Read_DoesNotAllocate()
    {
        var owner = new AllocOwner();
        owner.PropertyStore.SetStyle(AllocOwner.A, BrushA);

        AssertNoPerOpAllocation(() => _sink = owner.PropertyStore.GetValue(AllocOwner.A));
    }

    [TestMethod]
    public void SetLocal_NoChange_DoesNotAllocate()
    {
        var owner = new AllocOwner();
        owner.PropertyStore.SetLocal(AllocOwner.A, BrushA);

        AssertNoPerOpAllocation(() => owner.PropertyStore.SetLocal(AllocOwner.A, BrushA));
    }

    [TestMethod]
    public void SetLocal_Changing_ReferenceValue_DoesNotAllocate()
    {
        var owner = new AllocOwner();
        bool toggle = false;

        AssertNoPerOpAllocation(() =>
        {
            toggle = !toggle;
            owner.PropertyStore.SetLocal(AllocOwner.B, toggle ? BrushA : BrushB);
        });
    }

    // Averages under one byte per op: a one-time re-JIT allocation amortizes away over the run, while
    // any genuine per-op allocation (tens of bytes) fails.
    private static void AssertNoPerOpAllocation(Action op)
    {
        for (int i = 0; i < 200; i++)
        {
            op();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Iterations; i++)
        {
            op();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsLessThan(Iterations, allocated, $"Expected no per-op allocation, saw {allocated} bytes over {Iterations} ops.");
    }
}
