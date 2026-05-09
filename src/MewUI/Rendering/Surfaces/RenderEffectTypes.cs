using System.Collections.ObjectModel;

namespace Aprillz.MewUI.Rendering;

public enum ShaderModuleFormat
{
    Unknown = 0,
    HlslSource,
    HlslBytecode,
    GlslSource,
    SpirV,
    MetalLibrary,
    MetalSource,
}

public enum RenderEffectParameterType
{
    Unknown = 0,
    Float,
    Float2,
    Float3,
    Float4,
    Int,
    Int2,
    Int3,
    Int4,
    Matrix3x2,
    Matrix4x4,
    Bytes,
}

public readonly record struct ShaderModuleDescriptor(
    ShaderModuleFormat Format,
    string EntryPoint,
    ReadOnlyMemory<byte> Data,
    string? DebugName = null,
    string? Profile = null);

public readonly record struct RenderEffectInput(
    string Name,
    IRenderSurface? Surface = null,
    IExternalSampleSource? ExternalSample = null);

public readonly record struct RenderEffectParameter(
    string Name,
    RenderEffectParameterType Type,
    ReadOnlyMemory<byte> Data);

public sealed class RenderEffectInputSet
{
    public static RenderEffectInputSet Empty { get; } = new([]);

    private readonly ReadOnlyCollection<RenderEffectInput> _inputs;

    public RenderEffectInputSet(IEnumerable<RenderEffectInput> inputs)
    {
        _inputs = new ReadOnlyCollection<RenderEffectInput>((inputs ?? throw new ArgumentNullException(nameof(inputs))).ToArray());
    }

    public IReadOnlyList<RenderEffectInput> Inputs => _inputs;
}

public sealed class RenderEffectParameterSet
{
    public static RenderEffectParameterSet Empty { get; } = new([]);

    private readonly ReadOnlyCollection<RenderEffectParameter> _parameters;

    public RenderEffectParameterSet(IEnumerable<RenderEffectParameter> parameters)
    {
        _parameters = new ReadOnlyCollection<RenderEffectParameter>((parameters ?? throw new ArgumentNullException(nameof(parameters))).ToArray());
    }

    public IReadOnlyList<RenderEffectParameter> Parameters => _parameters;
}

public interface ICompiledRenderEffect : IDisposable
{
    ShaderModuleDescriptor Module { get; }
}

public interface IRenderEffectDevice
{
    bool Supports(ShaderModuleFormat format);

    ICompiledRenderEffect Compile(ShaderModuleDescriptor descriptor);

    IRenderOperation Execute(
        ICompiledRenderEffect effect,
        RenderEffectInputSet inputs,
        RenderEffectParameterSet parameters,
        IRenderSurface output);
}

public static class RenderEffectDeviceExtensions
{
    public static bool TryCompile(
        this IRenderEffectDevice? device,
        ShaderModuleDescriptor descriptor,
        out ICompiledRenderEffect? effect)
    {
        effect = null;
        if (device is null || !device.Supports(descriptor.Format))
        {
            return false;
        }

        try
        {
            effect = device.Compile(descriptor);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public static bool TryExecute(
        this IRenderEffectDevice? device,
        ICompiledRenderEffect effect,
        RenderEffectInputSet? inputs,
        RenderEffectParameterSet? parameters,
        IRenderSurface output,
        out IRenderOperation operation)
    {
        operation = RenderOperation.Completed;
        if (device is null || !device.Supports(effect.Module.Format))
        {
            return false;
        }

        try
        {
            operation = device.Execute(
                effect,
                inputs ?? RenderEffectInputSet.Empty,
                parameters ?? RenderEffectParameterSet.Empty,
                output);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
