namespace Aprillz.MewUI.Rendering;

internal interface IReusableScratchRenderTarget
{
    bool CanReturnToPool { get; }
}