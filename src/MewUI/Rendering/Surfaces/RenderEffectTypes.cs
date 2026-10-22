using System.Collections.ObjectModel;

namespace Aprillz.MewUI.Rendering;

/// <summary>Format of a shader module handed to <see cref="IRenderEffectDevice.Compile"/>.</summary>
public enum ShaderModuleFormat
{
    /// <summary>Not specified.</summary>
    Unknown = 0,
    /// <summary>HLSL source text.</summary>
    HlslSource,
    /// <summary>Compiled HLSL bytecode.</summary>
    HlslBytecode,
    /// <summary>GLSL source text.</summary>
    GlslSource,
    /// <summary>SPIR-V binary.</summary>
    SpirV,
    /// <summary>Compiled Metal library.</summary>
    MetalLibrary,
    /// <summary>Metal Shading Language source text.</summary>
    MetalSource,
}

/// <summary>Type of a <see cref="RenderEffectParameter"/> value.</summary>
public enum RenderEffectParameterType
{
    /// <summary>Not specified.</summary>
    Unknown = 0,
    /// <summary>One 32-bit float.</summary>
    Float,
    /// <summary>Two 32-bit floats.</summary>
    Float2,
    /// <summary>Three 32-bit floats.</summary>
    Float3,
    /// <summary>Four 32-bit floats.</summary>
    Float4,
    /// <summary>One 32-bit integer.</summary>
    Int,
    /// <summary>Two 32-bit integers.</summary>
    Int2,
    /// <summary>Three 32-bit integers.</summary>
    Int3,
    /// <summary>Four 32-bit integers.</summary>
    Int4,
    /// <summary>A 3x2 matrix of 32-bit floats.</summary>
    Matrix3x2,
    /// <summary>A 4x4 matrix of 32-bit floats.</summary>
    Matrix4x4,
    /// <summary>Raw bytes laid out as the shader expects.</summary>
    Bytes,
}

/// <summary>Describes a shader module to compile.</summary>
/// <param name="Format">Format of <paramref name="Data"/>.</param>
/// <param name="EntryPoint">Name of the entry point function.</param>
/// <param name="Data">Source text or binary in <paramref name="Format"/>.</param>
/// <param name="DebugName">Optional name for diagnostics.</param>
/// <param name="Profile">Optional compiler profile, such as a shader model.</param>
public readonly record struct ShaderModuleDescriptor(
    ShaderModuleFormat Format,
    string EntryPoint,
    ReadOnlyMemory<byte> Data,
    string? DebugName = null,
    string? Profile = null);

/// <summary>An input the effect samples, bound by name.</summary>
/// <param name="Name">Name the shader binds the input under.</param>
/// <param name="Surface">Surface to sample, or <see langword="null"/> when <paramref name="ExternalRaster"/> supplies the input.</param>
/// <param name="ExternalRaster">External raster to sample, or <see langword="null"/> when <paramref name="Surface"/> supplies the input.</param>
public readonly record struct RenderEffectInput(
    string Name,
    IRenderSurface? Surface = null,
    IExternalRasterSource? ExternalRaster = null);

/// <summary>A named constant passed to the effect.</summary>
/// <param name="Name">Name the shader binds the constant under.</param>
/// <param name="Type">Type of the value.</param>
/// <param name="Data">Value bytes laid out for <paramref name="Type"/>.</param>
public readonly record struct RenderEffectParameter(
    string Name,
    RenderEffectParameterType Type,
    ReadOnlyMemory<byte> Data);

/// <summary>Immutable set of inputs for one effect execution.</summary>
public sealed class RenderEffectInputSet
{
    /// <summary>Gets a set with no inputs.</summary>
    public static RenderEffectInputSet Empty { get; } = new([]);

    private readonly ReadOnlyCollection<RenderEffectInput> _inputs;

    /// <summary>Copies <paramref name="inputs"/> into an immutable set.</summary>
    public RenderEffectInputSet(IEnumerable<RenderEffectInput> inputs)
    {
        _inputs = new ReadOnlyCollection<RenderEffectInput>((inputs ?? throw new ArgumentNullException(nameof(inputs))).ToArray());
    }

    /// <summary>Gets the inputs in binding order.</summary>
    public IReadOnlyList<RenderEffectInput> Inputs => _inputs;
}

/// <summary>Immutable set of parameters for one effect execution.</summary>
public sealed class RenderEffectParameterSet
{
    /// <summary>Gets a set with no parameters.</summary>
    public static RenderEffectParameterSet Empty { get; } = new([]);

    private readonly ReadOnlyCollection<RenderEffectParameter> _parameters;

    /// <summary>Copies <paramref name="parameters"/> into an immutable set.</summary>
    public RenderEffectParameterSet(IEnumerable<RenderEffectParameter> parameters)
    {
        _parameters = new ReadOnlyCollection<RenderEffectParameter>((parameters ?? throw new ArgumentNullException(nameof(parameters))).ToArray());
    }

    /// <summary>Gets the parameters in binding order.</summary>
    public IReadOnlyList<RenderEffectParameter> Parameters => _parameters;
}

/// <summary>A compiled effect owned by the device that compiled it; disposing it releases the shader.</summary>
public interface ICompiledRenderEffect : IDisposable
{
    /// <summary>Gets the descriptor the effect was compiled from.</summary>
    ShaderModuleDescriptor Module { get; }
}

/// <summary>Optional device capability that compiles and runs custom shader effects.</summary>
public interface IRenderEffectDevice
{
    /// <summary>Returns whether the device accepts modules in <paramref name="format"/>.</summary>
    bool Supports(ShaderModuleFormat format);

    /// <summary>
    /// Compiles <paramref name="descriptor"/>; throws <see cref="NotSupportedException"/> when the format
    /// or the shader is not supported.
    /// </summary>
    ICompiledRenderEffect Compile(ShaderModuleDescriptor descriptor);

    /// <summary>
    /// Runs <paramref name="effect"/> with the given inputs and parameters into <paramref name="output"/>
    /// and returns the operation that completes when the output is ready.
    /// </summary>
    IRenderOperation Execute(
        ICompiledRenderEffect effect,
        RenderEffectInputSet inputs,
        RenderEffectParameterSet parameters,
        IRenderSurface output);
}

/// <summary>
/// Non-throwing wrappers over <see cref="IRenderEffectDevice"/> that answer <see langword="false"/> for a
/// missing device or an unsupported format.
/// </summary>
public static class RenderEffectDeviceExtensions
{
    /// <summary>
    /// Compiles <paramref name="descriptor"/> when <paramref name="device"/> supports its format; otherwise
    /// returns <see langword="false"/> with <paramref name="effect"/> set to <see langword="null"/>.
    /// </summary>
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

    /// <summary>
    /// Runs <paramref name="effect"/> when <paramref name="device"/> supports its format; otherwise returns
    /// <see langword="false"/> with <paramref name="operation"/> already completed.
    /// </summary>
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
