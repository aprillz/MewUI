using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

using BenchmarkDotNet.Attributes;

namespace MewUI.Benchmarks;

/// <summary>
/// Baselines the multi-slot <see cref="PropertyValueStore"/> for reads, sets and the element-trigger
/// on/off cycle.
/// MemoryDiagnoser reports allocations per op: the read and no-change paths must stay at zero, and
/// the trigger cycle records the cost of preserving and revealing a shadowed style value.
/// </summary>
[MemoryDiagnoser]
public class PropertyStoreBenchmarks
{
    private sealed class BenchOwner : Control
    {
        // A spread of style-sourced properties, so the store's sparse/dense storage is exercised the
        // way a heavily-styled control uses it rather than a single-entry best case.
        public static readonly MewProperty<Brush?>[] Fill = CreateFill();

        // Dedicated to the trigger on/off cycle so it does not perturb the read benchmarks.
        public static readonly MewProperty<Brush?> Cycle =
            MewProperty<Brush?>.Register<BenchOwner>("Cycle", null, MewPropertyOptions.None);

        private static MewProperty<Brush?>[] CreateFill()
        {
            var props = new MewProperty<Brush?>[12];
            for (int i = 0; i < props.Length; i++)
            {
                props[i] = MewProperty<Brush?>.Register<BenchOwner>("Fill" + i, null, MewPropertyOptions.None);
            }
            return props;
        }
    }

    private static readonly Brush BrushA = new SolidColorBrush(Color.FromArgb(255, 10, 20, 30));
    private static readonly Brush BrushB = new SolidColorBrush(Color.FromArgb(255, 40, 50, 60));

    private BenchOwner _owner = null!;
    private PropertyValueStore _store = null!;
    private bool _toggle;

    [GlobalSetup]
    public void Setup()
    {
        _owner = new BenchOwner();
        _store = _owner.PropertyStore;

        foreach (var property in BenchOwner.Fill)
        {
            _store.SetStyle(property, BrushA);
        }
        _store.SetStyle(BenchOwner.Cycle, BrushA);
    }

    // Hottest path: resolve the effective value. Returning it keeps the JIT from eliding the read.
    [Benchmark]
    public Brush? GetValue_Read() => _store.GetValue(BenchOwner.Fill[0]);

    // No-change set: hits the equality fast-path, must not allocate.
    [Benchmark]
    public void SetLocal_NoChange() => _store.SetLocal(BenchOwner.Fill[1], BrushA);

    // Real change through the Local tier, alternating so each op does work and the state stays steady.
    [Benchmark]
    public void SetLocal_Changing()
    {
        _toggle = !_toggle;
        _store.SetLocal(BenchOwner.Fill[2], _toggle ? BrushA : BrushB);
    }

    // Element-trigger cycle at the store level: style present, trigger over it, then clear.
    // The style value is preserved under the element-trigger slot and revealed on clear.
    [Benchmark]
    public void ElementTriggerOverStyle_Cycle()
    {
        _store.SetStyle(BenchOwner.Cycle, BrushA);
        _store.SetElementTrigger(BenchOwner.Cycle, BrushB);
        _store.ClearSource(BenchOwner.Cycle.Id, ValueSource.ElementTrigger);
    }
}
