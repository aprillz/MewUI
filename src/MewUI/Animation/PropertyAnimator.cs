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

        // First time this property is set — snap (no animation from default value)
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
            }
            _store.ClearAnimatedValue(id);
            return;
        }

        // Capture the "from" value (current visual state)
        object from = currentVisual!;

        // Type cannot lerp — snap immediately
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
    }

    private void StopAnimation(int propertyId)
    {
        if (_states == null || !_states.TryGetValue(propertyId, out var state))
            return;

        state.Clock?.Stop();
        // Animated value clearing is handled by the caller (PropertyValueStore.SetTarget)
    }

    private void OnTick(int propertyId, double progress)
    {
        if (_states == null || !_states.TryGetValue(propertyId, out var state))
            return;

        if (state.FromValue == null || state.TargetValue == null || state.PropertyType == null)
            return;

        var interpolated = TypeLerp.Interpolate(state.PropertyType, state.FromValue, state.TargetValue, progress);

        if (progress >= 1.0)
        {
            // Animation complete — clear animated overlay, target value takes effect
            _store.ClearAnimatedValue(propertyId);
        }
        else
        {
            _store.SetAnimatedValue(propertyId, interpolated);
        }
    }

    private sealed class AnimState
    {
        public AnimationClock? Clock;
        public object? FromValue;
        public object? TargetValue;
        public Type? PropertyType;
    }
}
