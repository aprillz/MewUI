using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Win32;

/// <summary>
/// Puts a visual of the system compositor under a transparent window: what stands behind the window through a blur,
/// or the blurred wallpaper. One per window; the compositor is shared by the windows of a thread.
/// </summary>
/// <remarks>
/// The Windows Runtime objects are reached through their vtables and the blur graph is a native object built here, so
/// the path needs no runtime COM interop and works under Native AOT.
/// </remarks>
internal sealed unsafe partial class Win32WindowBackdrop : IDisposable
{
    private const float BLUR_DEVIATION = 12f;
    private const float ACRYLIC_DEVIATION = 30f;
    private const float ACRYLIC_SATURATION = 1.6f;

    // Slots count from IUnknown's three; a Windows Runtime interface has three more of IInspectable before its own.
    private const int UNKNOWN_SLOTS = 3;
    private const int INSPECTABLE_SLOTS = 6;

    private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

    private static readonly Guid IID_ICompositor = new("B403CA50-7F8C-4E83-985F-CC45060036D8");
    private static readonly Guid IID_ICompositor3 = new("C9DD8EF0-6EB1-4E3C-A658-675D9C64D4AB");
    private static readonly Guid IID_ICompositor7 = new("D3483FAD-9A12-53BA-BFC8-88B7FF7977C6");
    private static readonly Guid IID_ICompositionClip = new("1CCD2A52-CFC7-4ACE-9983-146BB8EB6A3C");
    private static readonly Guid IID_ICompositorWithBlurredWallpaperBackdropBrush = new("0D8FB190-F122-5B8D-9FDD-543B0D8EB7F3");
    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    private static readonly Guid IID_ICompositionTarget = new("A1BEA8BA-D726-4663-8129-6B5E7927FFA6");
    private static readonly Guid IID_IVisual = new("117E202D-A859-4C89-873B-C2AA566788E3");
    private static readonly Guid IID_IVisual2 = new("3052B611-56C3-4C3E-8BF3-F6E1AD473F06");
    private static readonly Guid IID_ICompositionBrush = new("AB0D7608-30C0-40E9-B568-B60A6BD1FB46");
    private static readonly Guid IID_ICompositionEffectSourceParameterFactory = new("B3D9F276-ABA3-4724-ACF3-D0397464DB1C");
    private static readonly Guid IID_IPropertyValueStatics = new("629BDBC8-D932-4FF4-96B9-8D96C5C1E858");

    // The compositor lives on the thread whose windows it composes, as long as that thread.
    [ThreadStatic] private static nint _compositor;
    [ThreadStatic] private static bool _compositorFailed;
    [ThreadStatic] private static nint _dispatcherQueueController;

    private readonly nint _hwnd;
    private nint _target;
    private nint _sprite;
    private bool _usesHostBackdrop;
    private bool _disposed;
    private SystemBackdrop _requested;
    private SystemBackdrop _shown;
    // In device pixels, the units of the composition target; null fills the window.
    private Rect? _areaPixels;
    private float _cornerPixels;

    public Win32WindowBackdrop(nint hwnd) => _hwnd = hwnd;

    /// <summary>
    /// Shows <paramref name="material"/> under the window, or the nearest lower one this system offers, and returns
    /// the one shown, on <paramref name="area"/> or the whole window; <see cref="SystemBackdrop.None"/> removes it.
    /// </summary>
    public SystemBackdrop Apply(SystemBackdrop material, Platform.SystemBackdropArea? area, double dpiScale)
    {
        SetArea(area, dpiScale);
        if (_sprite != 0 && material == _requested)
        {
            // The same material on another area keeps its visual, so a resize does not rebuild the effect.
            Place(_sprite);
            return _shown;
        }

        Clear();
        _requested = material;
        _shown = SystemBackdrop.None;
        if (_disposed || material == SystemBackdrop.None || !EnsureCompositor())
        {
            return SystemBackdrop.None;
        }

        for (var candidate = material; candidate != SystemBackdrop.None; candidate = Lower(candidate))
        {
            if (TryShow(candidate))
            {
                _shown = candidate;
                return candidate;
            }
        }

        return SystemBackdrop.None;
    }

    private void SetArea(Platform.SystemBackdropArea? area, double dpiScale)
    {
        if (area is Platform.SystemBackdropArea shape)
        {
            double left = Math.Round(shape.Bounds.X * dpiScale);
            double top = Math.Round(shape.Bounds.Y * dpiScale);
            double right = Math.Round(shape.Bounds.Right * dpiScale);
            double bottom = Math.Round(shape.Bounds.Bottom * dpiScale);
            _areaPixels = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
            _cornerPixels = (float)(shape.CornerRadius * dpiScale);
        }
        else
        {
            _areaPixels = null;
            _cornerPixels = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        Release(ref _target);
    }

    private static SystemBackdrop Lower(SystemBackdrop material) => material switch
    {
        SystemBackdrop.Mica => SystemBackdrop.Acrylic,
        SystemBackdrop.Acrylic => SystemBackdrop.Blur,
        _ => SystemBackdrop.None,
    };

    private bool TryShow(SystemBackdrop material)
    {
        nint brush = material switch
        {
            SystemBackdrop.Mica => CreateWallpaperBrush(),
            SystemBackdrop.Acrylic => CreateBackdropBrush(ACRYLIC_DEVIATION, ACRYLIC_SATURATION),
            _ => CreateBackdropBrush(BLUR_DEVIATION, saturation: 0),
        };

        if (brush == 0)
        {
            return false;
        }

        try
        {
            if (!EnsureTarget())
            {
                return false;
            }

            const int CREATE_SPRITE_VISUAL = INSPECTABLE_SLOTS + 16;
            nint sprite;
            if (Call(_compositor, CREATE_SPRITE_VISUAL, &sprite) < 0 || sprite == 0)
            {
                return false;
            }

            const int SPRITE_PUT_BRUSH = INSPECTABLE_SLOTS + 1;
            if (Call(sprite, SPRITE_PUT_BRUSH, brush) < 0 || !Place(sprite) || !SetRoot(sprite))
            {
                Release(ref sprite);
                return false;
            }

            _sprite = sprite;
            // The host backdrop brush draws what is behind the window only once the window opts in; the wallpaper brush needs nothing.
            _usesHostBackdrop = material != SystemBackdrop.Mica;
            if (_usesHostBackdrop)
            {
                SetHostBackdropAttribute(true);
            }

            return true;
        }
        finally
        {
            Release(ref brush);
        }
    }

    private void Clear()
    {
        if (_sprite != 0)
        {
            SetRoot(0);
            Release(ref _sprite);
        }

        if (_usesHostBackdrop)
        {
            SetHostBackdropAttribute(false);
            _usesHostBackdrop = false;
        }
    }

    private void SetHostBackdropAttribute(bool enabled)
    {
        int value = enabled ? 1 : 0;
        Dwmapi.DwmSetWindowAttribute(_hwnd, Dwmapi.DwmWindowAttribute.DWMWA_USE_HOSTBACKDROPBRUSH, ref value, sizeof(int));
    }

    private bool EnsureTarget()
    {
        if (_target != 0)
        {
            return true;
        }

        nint interop = Query(_compositor, IID_ICompositorDesktopInterop);
        if (interop == 0)
        {
            return false;
        }

        nint desktopTarget;
        // Not topmost: the window's own content keeps the topmost place, over this visual.
        int created = ((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Slot(interop, UNKNOWN_SLOTS))(interop, _hwnd, 0, &desktopTarget);
        Release(ref interop);
        if (created < 0 || desktopTarget == 0)
        {
            return false;
        }

        _target = Query(desktopTarget, IID_ICompositionTarget);
        Release(ref desktopTarget);
        return _target != 0;
    }

    private bool SetRoot(nint sprite)
    {
        nint visual = sprite == 0 ? 0 : Query(sprite, IID_IVisual);
        if (sprite != 0 && visual == 0)
        {
            return false;
        }

        const int TARGET_PUT_ROOT = INSPECTABLE_SLOTS + 1;
        int result = Call(_target, TARGET_PUT_ROOT, visual);
        Release(ref visual);
        return result >= 0;
    }

    /// <summary>Puts the visual on the area, or makes it follow the window's size when there is none.</summary>
    private bool Place(nint sprite)
    {
        nint visual = Query(sprite, IID_IVisual);
        nint visual2 = Query(sprite, IID_IVisual2);
        nint clip = 0;
        try
        {
            if (visual == 0 || visual2 == 0)
            {
                return false;
            }

            var relativeSize = new Vector2(1, 1);
            var offset = Vector3.Zero;
            var size = Vector2.Zero;
            if (_areaPixels is Rect area)
            {
                relativeSize = Vector2.Zero;
                offset = new Vector3((float)area.X, (float)area.Y, 0);
                size = new Vector2((float)area.Width, (float)area.Height);
                if (_cornerPixels > 0)
                {
                    clip = CreateRoundedClip(size, _cornerPixels);
                }
            }

            const int PUT_CLIP = INSPECTABLE_SLOTS + 9;
            const int PUT_OFFSET = INSPECTABLE_SLOTS + 15;
            const int PUT_SIZE = INSPECTABLE_SLOTS + 30;
            const int PUT_RELATIVE_SIZE_ADJUSTMENT = INSPECTABLE_SLOTS + 5;
            return ((delegate* unmanaged[Stdcall]<nint, Vector2, int>)Slot(visual2, PUT_RELATIVE_SIZE_ADJUSTMENT))(visual2, relativeSize) >= 0
                && ((delegate* unmanaged[Stdcall]<nint, Vector3, int>)Slot(visual, PUT_OFFSET))(visual, offset) >= 0
                && ((delegate* unmanaged[Stdcall]<nint, Vector2, int>)Slot(visual, PUT_SIZE))(visual, size) >= 0
                && Call(visual, PUT_CLIP, clip) >= 0;
        }
        finally
        {
            Release(ref clip);
            Release(ref visual2);
            Release(ref visual);
        }
    }

    /// <summary>A clip with rounded corners, or none on a system before these clips (Windows 10 2004): the corners then stay square.</summary>
    private static nint CreateRoundedClip(Vector2 size, float radius)
    {
        nint compositor7 = Query(_compositor, IID_ICompositor7);
        if (compositor7 == 0)
        {
            return 0;
        }

        const int CREATE_RECTANGLE_CLIP_WITH_SIDES_AND_RADIUS = INSPECTABLE_SLOTS + 4;
        var corner = new Vector2(radius, radius);
        nint rectangleClip;
        int created = ((delegate* unmanaged[Stdcall]<nint, float, float, float, float, Vector2, Vector2, Vector2, Vector2, nint*, int>)
            Slot(compositor7, CREATE_RECTANGLE_CLIP_WITH_SIDES_AND_RADIUS))(compositor7, 0, 0, size.X, size.Y, corner, corner, corner, corner, &rectangleClip);
        Release(ref compositor7);
        if (created < 0 || rectangleClip == 0)
        {
            return 0;
        }

        nint clip = Query(rectangleClip, IID_ICompositionClip);
        Release(ref rectangleClip);
        return clip;
    }

    private static nint CreateWallpaperBrush()
    {
        nint wallpaperCompositor = Query(_compositor, IID_ICompositorWithBlurredWallpaperBackdropBrush);
        if (wallpaperCompositor == 0)
        {
            return 0;
        }

        nint backdropBrush;
        int created = Call(wallpaperCompositor, INSPECTABLE_SLOTS, &backdropBrush);
        Release(ref wallpaperCompositor);
        if (created < 0 || backdropBrush == 0)
        {
            return 0;
        }

        nint brush = Query(backdropBrush, IID_ICompositionBrush);
        Release(ref backdropBrush);
        return brush;
    }

    /// <summary>What stands behind the window through a Gaussian blur, then a saturation when <paramref name="saturation"/> is above zero.</summary>
    private static nint CreateBackdropBrush(float deviation, float saturation)
    {
        nint compositor3 = Query(_compositor, IID_ICompositor3);
        if (compositor3 == 0)
        {
            return 0;
        }

        nint hostBackdrop = 0;
        nint hostBrush = 0;
        nint parameterFactory = 0;
        nint parameter = 0;
        nint parameterSource = 0;
        nint effect = 0;
        nint effectFactory = 0;
        nint effectBrush = 0;
        nint name = 0;
        try
        {
            if (Call(compositor3, INSPECTABLE_SLOTS, &hostBackdrop) < 0 || hostBackdrop == 0)
            {
                return 0;
            }

            hostBrush = Query(hostBackdrop, IID_ICompositionBrush);
            parameterFactory = ActivationFactory("Windows.UI.Composition.CompositionEffectSourceParameter", IID_ICompositionEffectSourceParameterFactory);
            name = CreateString("backdrop");
            if (hostBrush == 0 || parameterFactory == 0 || name == 0
                || Call(parameterFactory, INSPECTABLE_SLOTS, name, &parameter) < 0 || parameter == 0)
            {
                return 0;
            }

            parameterSource = Query(parameter, CompositionEffect.IID_IGraphicsEffectSource);
            if (parameterSource == 0)
            {
                return 0;
            }

            effect = CompositionEffect.CreateGaussianBlur(parameterSource, deviation);
            if (saturation > 0)
            {
                nint blur = effect;
                effect = CompositionEffect.CreateSaturation(blur, saturation);
                Release(ref blur);
            }

            const int CREATE_EFFECT_FACTORY = INSPECTABLE_SLOTS + 5;
            const int EFFECT_BRUSH_SET_SOURCE_PARAMETER = INSPECTABLE_SLOTS + 1;
            if (Call(_compositor, CREATE_EFFECT_FACTORY, effect, &effectFactory) < 0 || effectFactory == 0
                || Call(effectFactory, INSPECTABLE_SLOTS, &effectBrush) < 0 || effectBrush == 0
                || ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(effectBrush, EFFECT_BRUSH_SET_SOURCE_PARAMETER))(effectBrush, name, hostBrush) < 0)
            {
                return 0;
            }

            return Query(effectBrush, IID_ICompositionBrush);
        }
        finally
        {
            Release(ref effectBrush);
            Release(ref effectFactory);
            Release(ref effect);
            Release(ref parameterSource);
            Release(ref parameter);
            Release(ref parameterFactory);
            Release(ref hostBrush);
            Release(ref hostBackdrop);
            Release(ref compositor3);
            if (name != 0)
            {
                Native.WindowsDeleteString(name);
            }
        }
    }

    private static bool EnsureCompositor()
    {
        if (_compositor != 0)
        {
            return true;
        }

        if (_compositorFailed)
        {
            return false;
        }

        // A system without the compositor (before Windows 10 1809) fails here once and is not asked again.
        _compositorFailed = true;
        if (!EnsureDispatcherQueue() || !CompositionEffect.EnsurePropertyValues())
        {
            return false;
        }

        nint instance = Activate("Windows.UI.Composition.Compositor");
        if (instance == 0)
        {
            return false;
        }

        _compositor = Query(instance, IID_ICompositor);
        Release(ref instance);
        _compositorFailed = _compositor == 0;
        return _compositor != 0;
    }

    private static bool EnsureDispatcherQueue()
    {
        if (_dispatcherQueueController != 0)
        {
            return true;
        }

        const int DQTYPE_THREAD_CURRENT = 2;
        const int DQTAT_COM_NONE = 0;
        const int DQTAT_COM_STA = 2;
        var options = new Native.DispatcherQueueOptions { Size = sizeof(Native.DispatcherQueueOptions), ThreadType = DQTYPE_THREAD_CURRENT, ApartmentType = DQTAT_COM_STA };
        nint controller;
        int result = Native.CreateDispatcherQueueController(options, &controller);
        if (result == RPC_E_CHANGED_MODE)
        {
            // The thread already joined an apartment of its own; the queue then leaves COM as it is.
            options.ApartmentType = DQTAT_COM_NONE;
            result = Native.CreateDispatcherQueueController(options, &controller);
        }

        if (result < 0 || controller == 0)
        {
            return false;
        }

        _dispatcherQueueController = controller;
        return true;
    }

    internal static nint Activate(string className)
    {
        nint name = CreateString(className);
        if (name == 0)
        {
            return 0;
        }

        nint instance;
        int result = Native.RoActivateInstance(name, &instance);
        Native.WindowsDeleteString(name);
        return result < 0 ? 0 : instance;
    }

    internal static nint ActivationFactory(string className, Guid iid)
    {
        nint name = CreateString(className);
        if (name == 0)
        {
            return 0;
        }

        nint factory;
        int result = Native.RoGetActivationFactory(name, &iid, &factory);
        Native.WindowsDeleteString(name);
        return result < 0 ? 0 : factory;
    }

    private static nint CreateString(string text)
    {
        nint handle;
        fixed (char* chars = text)
        {
            return Native.WindowsCreateString(chars, (uint)text.Length, &handle) < 0 ? 0 : handle;
        }
    }

    internal static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    private static int Call(nint instance, int slot, nint* result)
        => ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(instance, slot))(instance, result);

    private static int Call(nint instance, int slot, nint argument)
        => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(instance, slot))(instance, argument);

    private static int Call(nint instance, int slot, nint argument, nint* result)
        => ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(instance, slot))(instance, argument, result);

    internal static nint Query(nint instance, in Guid iid)
    {
        if (instance == 0)
        {
            return 0;
        }

        nint result = 0;
        fixed (Guid* pointer = &iid)
        {
            int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(instance, 0))(instance, pointer, &result);
            return hr < 0 ? 0 : result;
        }
    }

    internal static void AddRef(nint instance)
    {
        if (instance != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 1))(instance);
        }
    }

    internal static void Release(ref nint instance)
    {
        if (instance != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(instance, 2))(instance);
            instance = 0;
        }
    }

    private static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct DispatcherQueueOptions
        {
            public int Size;
            public int ThreadType;
            public int ApartmentType;
        }

        [LibraryImport("CoreMessaging.dll")]
        internal static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, nint* controller);

        [LibraryImport("combase.dll")]
        internal static partial int RoActivateInstance(nint activatableClassId, nint* instance);

        [LibraryImport("combase.dll")]
        internal static partial int RoGetActivationFactory(nint activatableClassId, Guid* iid, nint* factory);

        [LibraryImport("combase.dll")]
        internal static partial int WindowsCreateString(char* sourceString, uint length, nint* handle);

        [LibraryImport("combase.dll")]
        internal static partial int WindowsDeleteString(nint handle);
    }

    /// <summary>
    /// An effect of the system compositor's effect graph, as a native object the compositor reads: its effect id,
    /// its properties and its one source. It answers as IGraphicsEffect and IGraphicsEffectSource through one vtable
    /// and as IGraphicsEffectD2D1Interop through a second one right after it.
    /// </summary>
    private static class CompositionEffect
    {
        internal static readonly Guid IID_IGraphicsEffectSource = new("2D8F9DDC-4339-4EB9-9216-F9DEB75658A2");
        private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
        private static readonly Guid IID_IInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
        private static readonly Guid IID_IAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
        private static readonly Guid IID_IGraphicsEffect = new("CB51C0CE-8FE6-4636-B202-861FAA07D8F3");
        private static readonly Guid IID_IGraphicsEffectD2D1Interop = new("2FC57384-A068-44D7-A331-30982FCF7177");
        private static readonly Guid CLSID_D2D1GaussianBlur = new("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");
        private static readonly Guid CLSID_D2D1Saturation = new("5CB2D9CF-327D-459F-A0CE-40C0B2086BF7");

        private const int S_OK = 0;
        private const int E_NOINTERFACE = unchecked((int)0x80004002);
        private const int E_NOTIMPL = unchecked((int)0x80004001);
        private const int E_INVALIDARG = unchecked((int)0x80070057);
        private const int E_POINTER = unchecked((int)0x80004003);

        private const uint KIND_FLOAT = 0;
        private const uint KIND_UINT32 = 1;
        private const uint OPTIMIZATION_BALANCED = 1;
        // Edge pixels extend outward instead of fading to transparent at the window border.
        private const uint BORDER_MODE_HARD = 1;
        private const int MAX_PROPERTIES = 3;

        private static nint _effectVTable;
        private static nint _interopVTable;
        private static nint _propertyValueStatics;

        [StructLayout(LayoutKind.Sequential)]
        private struct Effect
        {
            public nint EffectVTable;
            public nint InteropVTable;
            public int RefCount;
            public int PropertyCount;
            public Guid EffectId;
            public nint Source;
            public fixed uint Kinds[MAX_PROPERTIES];
            public fixed uint Values[MAX_PROPERTIES];
        }

        internal static bool EnsurePropertyValues()
        {
            if (_propertyValueStatics == 0)
            {
                _propertyValueStatics = ActivationFactory("Windows.Foundation.PropertyValue", IID_IPropertyValueStatics);
            }

            return _propertyValueStatics != 0;
        }

        internal static nint CreateGaussianBlur(nint source, float deviation)
        {
            var effect = Allocate(CLSID_D2D1GaussianBlur, source, 3);
            effect->Kinds[0] = KIND_FLOAT;
            effect->Values[0] = BitConverter.SingleToUInt32Bits(deviation);
            effect->Kinds[1] = KIND_UINT32;
            effect->Values[1] = OPTIMIZATION_BALANCED;
            effect->Kinds[2] = KIND_UINT32;
            effect->Values[2] = BORDER_MODE_HARD;
            return (nint)effect;
        }

        internal static nint CreateSaturation(nint source, float saturation)
        {
            var effect = Allocate(CLSID_D2D1Saturation, source, 1);
            effect->Kinds[0] = KIND_FLOAT;
            effect->Values[0] = BitConverter.SingleToUInt32Bits(saturation);
            return (nint)effect;
        }

        private static Effect* Allocate(Guid effectId, nint source, int propertyCount)
        {
            EnsureVTables();
            var effect = (Effect*)NativeMemory.AllocZeroed((nuint)sizeof(Effect));
            effect->EffectVTable = _effectVTable;
            effect->InteropVTable = _interopVTable;
            effect->RefCount = 1;
            effect->PropertyCount = propertyCount;
            effect->EffectId = effectId;
            AddRef(source);
            effect->Source = source;
            return effect;
        }

        private static void EnsureVTables()
        {
            if (_effectVTable != 0)
            {
                return;
            }

            var effectVTable = (nint*)NativeMemory.Alloc((nuint)(sizeof(nint) * 8));
            effectVTable[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&EffectQueryInterface;
            effectVTable[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&EffectAddRef;
            effectVTable[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&EffectRelease;
            effectVTable[3] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, nint*, int>)&GetIids;
            effectVTable[4] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetRuntimeClassName;
            effectVTable[5] = (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetTrustLevel;
            effectVTable[6] = (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetName;
            effectVTable[7] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&SetName;

            var interopVTable = (nint*)NativeMemory.Alloc((nuint)(sizeof(nint) * 9));
            interopVTable[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&InteropQueryInterface;
            interopVTable[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&InteropAddRef;
            interopVTable[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&InteropRelease;
            interopVTable[3] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, int>)&GetEffectId;
            interopVTable[4] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, uint*, int*, int>)&GetNamedPropertyMapping;
            interopVTable[5] = (nint)(delegate* unmanaged[Stdcall]<nint, uint*, int>)&GetPropertyCount;
            interopVTable[6] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)&GetProperty;
            interopVTable[7] = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)&GetSource;
            interopVTable[8] = (nint)(delegate* unmanaged[Stdcall]<nint, uint*, int>)&GetSourceCount;

            _interopVTable = (nint)interopVTable;
            _effectVTable = (nint)effectVTable;
        }

        // The interop interface pointer sits one field after the object's start.
        private static Effect* FromInterop(nint pointer) => (Effect*)(pointer - sizeof(nint));

        private static int QueryInterface(Effect* effect, Guid* iid, nint* result)
        {
            if (iid == null || result == null)
            {
                return E_POINTER;
            }

            if (*iid == IID_IUnknown || *iid == IID_IInspectable || *iid == IID_IGraphicsEffect
                || *iid == IID_IGraphicsEffectSource || *iid == IID_IAgileObject)
            {
                *result = (nint)effect;
            }
            else if (*iid == IID_IGraphicsEffectD2D1Interop)
            {
                *result = (nint)effect + sizeof(nint);
            }
            else
            {
                *result = 0;
                return E_NOINTERFACE;
            }

            Interlocked.Increment(ref effect->RefCount);
            return S_OK;
        }

        private static uint ReleaseEffect(Effect* effect)
        {
            int remaining = Interlocked.Decrement(ref effect->RefCount);
            if (remaining > 0)
            {
                return (uint)remaining;
            }

            nint source = effect->Source;
            effect->Source = 0;
            NativeMemory.Free(effect);
            Release(ref source);
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int EffectQueryInterface(nint self, Guid* iid, nint* result) => QueryInterface((Effect*)self, iid, result);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint EffectAddRef(nint self) => (uint)Interlocked.Increment(ref ((Effect*)self)->RefCount);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint EffectRelease(nint self) => ReleaseEffect((Effect*)self);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int InteropQueryInterface(nint self, Guid* iid, nint* result) => QueryInterface(FromInterop(self), iid, result);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint InteropAddRef(nint self) => (uint)Interlocked.Increment(ref FromInterop(self)->RefCount);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint InteropRelease(nint self) => ReleaseEffect(FromInterop(self));

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetIids(nint self, int* count, nint* iids)
        {
            *count = 0;
            *iids = 0;
            return S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetRuntimeClassName(nint self, nint* name)
        {
            *name = 0;
            return S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetTrustLevel(nint self, int* level)
        {
            *level = 0;
            return S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetName(nint self, nint* name)
        {
            *name = 0;
            return S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int SetName(nint self, nint name) => S_OK;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetEffectId(nint self, Guid* id)
        {
            *id = FromInterop(self)->EffectId;
            return S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetNamedPropertyMapping(nint self, nint name, uint* index, int* mapping)
        {
            // Names matter only to animations, which this graph has none of.
            *index = 0;
            *mapping = 0;
            return E_NOTIMPL;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetPropertyCount(nint self, uint* count)
        {
            *count = (uint)FromInterop(self)->PropertyCount;
            return S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetProperty(nint self, uint index, nint* value)
        {
            *value = 0;
            var effect = FromInterop(self);
            if (index >= (uint)effect->PropertyCount || _propertyValueStatics == 0)
            {
                return E_INVALIDARG;
            }

            const int CREATE_UINT32 = INSPECTABLE_SLOTS + 5;
            const int CREATE_SINGLE = INSPECTABLE_SLOTS + 8;
            uint bits = effect->Values[index];
            nint boxed;
            int result = effect->Kinds[index] == KIND_FLOAT
                ? ((delegate* unmanaged[Stdcall]<nint, float, nint*, int>)Slot(_propertyValueStatics, CREATE_SINGLE))(_propertyValueStatics, BitConverter.UInt32BitsToSingle(bits), &boxed)
                : ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(_propertyValueStatics, CREATE_UINT32))(_propertyValueStatics, bits, &boxed);
            if (result < 0)
            {
                return result;
            }

            *value = boxed;
            return S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetSource(nint self, uint index, nint* source)
        {
            var effect = FromInterop(self);
            if (index != 0 || effect->Source == 0)
            {
                *source = 0;
                return E_INVALIDARG;
            }

            AddRef(effect->Source);
            *source = effect->Source;
            return S_OK;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int GetSourceCount(nint self, uint* count)
        {
            *count = 1;
            return S_OK;
        }
    }
}
