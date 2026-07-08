namespace Aprillz.MewUI.Animation;

/// <summary>
/// Manages animated transitions for <see cref="PropertyValueStore"/> entries.
/// Owns <see cref="AnimationClock"/> instances and interpolates via <see cref="TypeLerp"/>.
/// This keeps animation concerns out of the core property store.
/// </summary>
internal sealed class PropertyAnimator
{
    private readonly PropertyValueStore _store;
    private Dictionary<int, AnimState>? _states;

    internal PropertyAnimator(PropertyValueStore store)
    {
        _store = store;
        store.StopAnimationCallback = StopAnimation;
        store.StopAllAnimationsCallback = StopAll;
    }

    /// <summary>
    /// Animates a property to a new target value with the given transition parameters.
    /// If the type supports interpolation (via <see cref="TypeLerp"/>),
    /// starts a smooth transition from the current visual value.
    /// Falls back to snap if the type cannot lerp or this is the first target.
    /// </summary>
    public void Animate(MewProperty property, object value, TimeSpan duration, Func<double, double> easing, ValueSource source = ValueSource.Trigger)
    {
        int id = property.Id;

        // First time this property is set - snap (no animation from default value)
        if (!_store.HasTargetValue(id))
        {
            _store.SetValue(property, value, source);
            return;
        }

        // Capture the current visual appearance (animated overlay if running, otherwise target).
        object? currentVisual = _store.GetCurrentVisualValue(id);
        if (Equals(currentVisual, value))
        {
            // The visual already shows this value, so no visible animation is needed.
            _store.SetTargetDirect(property, value, source);
            if (_states != null && _states.TryGetValue(id, out var existing))
            {
                existing.Clock?.Stop();
                _states.Remove(id);
            }
            _store.ClearAnimatedValue(id);
            return;
        }

        // Capture the "from" value (current visual state)
        object from = currentVisual!;

        // Type cannot lerp - snap immediately
        if (!TypeLerp.CanLerp(property.ValueType))
        {
            _store.SetValue(property, value, source);
            return;
        }

        _states ??= new();
        if (!_states.TryGetValue(id, out var state))
        {
            state = new AnimState();
            _states[id] = state;
            state.Clock = new AnimationClock(duration, easing);
            state.Clock.TickCallback = progress => OnTick(id, progress);
            // Drop the state once the clock finishes on its own so a property that animated
            // once doesn't keep its (boxed) from/target values alive for the element's lifetime.
            state.Clock.CompletedCallback = () => _states?.Remove(id);
        }
        else
        {
            state.Clock!.Stop();
            state.Clock.Duration = duration;
            state.Clock.EasingFunction = easing;
        }

        state.FromValue = from;
        state.TargetValue = value;
        state.PropertyType = property.ValueType;
        // Resolved once per animation start instead of per tick, so OnTick never touches
        // TypeLerp's Dictionary<Type> for the fallback (non-fast-pathed) types.
        state.LerpDelegate = TypeLerp.GetDelegate(state.PropertyType);

        // Set animated overlay first (so the store shows "from" value),
        // then update the underlying target silently.
        _store.SetAnimatedValue(id, from);
        _store.SetTargetDirect(property, value, source);

        state.Clock.Start();
    }

    /// <summary>
    /// Stops all running animations and clears animated overlays.
    /// </summary>
    public void StopAll()
    {
        if (_states == null) return;

        foreach (var kv in _states)
        {
            kv.Value.Clock?.Stop();
            _store.ClearAnimatedValue(kv.Key);
        }
        _states.Clear();
    }

    private void StopAnimation(int propertyId)
    {
        if (_states == null || !_states.TryGetValue(propertyId, out var state))
            return;

        state.Clock?.Stop();
        // Animated value clearing is handled by the caller (PropertyValueStore.SetTarget)
        _states.Remove(propertyId);
    }

    private void OnTick(int propertyId, double progress)
    {
        if (_states == null || !_states.TryGetValue(propertyId, out var state))
            return;

        if (state.FromValue == null || state.TargetValue == null || state.PropertyType == null)
            return;

        var interpolated = Interpolate(state, progress);

        if (progress >= 1.0)
        {
            // Animation complete - clear animated overlay, target value takes effect
            _store.ClearAnimatedValue(propertyId);
        }
        else
        {
            _store.SetAnimatedValue(propertyId, interpolated);
        }
    }

    /// <summary>
    /// Interpolates the animation's current value. The common visual types are computed
    /// directly (no dictionary lookup, no delegate call); anything else registered via
    /// <see cref="TypeLerp.Register{T}"/> falls back to the delegate cached at animation start.
    /// The result still has to be boxed once here since <see cref="PropertyValueStore"/> stores
    /// the animated overlay as <c>object</c>.
    /// </summary>
    private static object Interpolate(AnimState state, double progress)
    {
        var propertyType = state.PropertyType!;
        var from = state.FromValue!;
        var to = state.TargetValue!;

        if (propertyType == typeof(double))
            return Lerp.Double((double)from, (double)to, progress);
        if (propertyType == typeof(Color))
            return Lerp.Color((Color)from, (Color)to, progress);
        if (propertyType == typeof(Thickness))
            return Lerp.Thickness((Thickness)from, (Thickness)to, progress);
        if (propertyType == typeof(Point))
            return Lerp.Point((Point)from, (Point)to, progress);
        if (propertyType == typeof(float))
            return Lerp.Float((float)from, (float)to, progress);

        return state.LerpDelegate!(from, to, progress);
    }

    private sealed class AnimState
    {
        public AnimationClock? Clock;
        public object? FromValue;
        public object? TargetValue;
        public Type? PropertyType;
        public Func<object, object, double, object>? LerpDelegate;
    }
}
