namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Blur unit conversion. Filter blur is parameterized by <c>Radius</c> in DIPs = the kernel's
/// reach (the distance at which the Gaussian is treated as zero). The Gaussian standard deviation
/// is <c>sigma = Radius / KernelSigmas</c>.
/// <para/>
/// <see cref="KernelSigmas"/> (3) is the kernel half-width in sigmas used by the executors'
/// <c>BuildKernel</c> (<c>kernelRadius = ceil(sigma * 3)</c>). Keep this in sync if the kernel
/// truncation changes.
/// </summary>
public static class BlurKernel
{
    /// <summary>Kernel half-width expressed in standard deviations (matches <c>ceil(sigma * 3)</c>).</summary>
    public const double KernelSigmas = 3.0;

    /// <summary>Converts a blur radius (DIP) to the Gaussian standard deviation.</summary>
    public static double RadiusToSigma(double radius) => radius / KernelSigmas;

    /// <summary>Converts a Gaussian standard deviation to the blur radius (DIP).</summary>
    public static double SigmaToRadius(double sigma) => sigma * KernelSigmas;
}
