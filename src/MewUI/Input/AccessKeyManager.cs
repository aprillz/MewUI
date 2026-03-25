using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Manages access key (mnemonic) registration, Alt mode, and key matching for a Window.
/// Disabled on macOS (Option key is for special character input).
/// </summary>
internal sealed class AccessKeyManager
{
    private readonly Window _window;
    private readonly Dictionary<char, List<AccessKeyEntry>> _registry = new();
    private bool _altPressed;
    private int _lastCycleIndex = -1;

    public AccessKeyManager(Window window) => _window = window;

    /// <summary>
    /// Registers an access key for a target element.
    /// </summary>
    public void Register(char key, UIElement target, Action activate)
    {
        key = char.ToUpperInvariant(key);
        if (!_registry.TryGetValue(key, out var list))
        {
            list = new List<AccessKeyEntry>();
            _registry[key] = list;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Target, target))
                return;
        }

        list.Add(new AccessKeyEntry(target, activate));
    }

    /// <summary>
    /// Unregisters all access keys for a target element.
    /// </summary>
    public void Unregister(UIElement target)
    {
        foreach (var list in _registry.Values)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(list[i].Target, target))
                    list.RemoveAt(i);
            }
        }
    }

    public void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled) return;

        // macOS: access keys disabled
        if (!PlatformKeyConfiguration.Current.SupportsAccessKeys)
            return;

        var showAccessKeys = _window.ShowAccessKeys;

        if (e.Key == Key.Escape && showAccessKeys)
        {
            SetShowAccessKeys(false);
            e.Handled = true;
            return;
        }

        if (IsAltOnlyDown(e))
        {
            if (!e.IsRepeat)
            {
                _altPressed = true;
                SetShowAccessKeys(!_window.ShowAccessKeys);
            }
            e.Handled = true;
            return;
        }

        if (e.AltKey && _altPressed)
        {
            _altPressed = false;
            var ch = CharFromKey(e.Key);
            if (ch != default)
                ProcessKey(ch);
            e.Handled = true; // Always suppress Alt+key to prevent OS beep
            return;
        }

        _altPressed = false;

        if (showAccessKeys)
        {
            if (!e.AltKey && !e.ControlKey && !e.MetaKey)
            {
                var ch = CharFromKey(e.Key);
                if (ch != default && ProcessKey(ch))
                    e.Handled = true;
                else
                    SetShowAccessKeys(false);
            }
            else
            {
                SetShowAccessKeys(false);
            }
        }
    }

    public void OnKeyUp(KeyEventArgs e)
    {
        if (!IsAltOnlyUp(e)) return;

        // Alt released — just clear the pending flag.
        // Access keys remain visible until Escape, mouse click, or another action dismisses them.
        _altPressed = false;
    }

    public void OnPointerDown()
    {
        if (_window.ShowAccessKeys)
            SetShowAccessKeys(false);
        _altPressed = false;
    }

    private void SetShowAccessKeys(bool value)
    {
        _window.ShowAccessKeys = value;
        if (!value) _lastCycleIndex = -1;
    }

    private bool ProcessKey(char ch)
    {
        ch = char.ToUpperInvariant(ch);
        if (!_registry.TryGetValue(ch, out var list))
            return false;

        var targets = new List<AccessKeyEntry>();
        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (entry.Target.IsVisible && entry.Target.IsEffectivelyEnabled && IsEffectivelyVisible(entry.Target))
                targets.Add(entry);
        }

        if (targets.Count == 0)
            return false;

        if (targets.Count == 1)
        {
            targets[0].Activate();
        }
        else
        {
            _lastCycleIndex = (_lastCycleIndex + 1) % targets.Count;
            _window.FocusManager.SetFocus(targets[_lastCycleIndex].Target);
        }

        SetShowAccessKeys(false);
        return true;
    }

    /// <summary>
    /// Checks if the element is effectively visible by walking the parent chain.
    /// Returns false if any ancestor has IsVisible=false or if the element has zero-size bounds.
    /// </summary>
    private static bool IsEffectivelyVisible(UIElement element)
    {
        // Zero-size bounds means the element hasn't been arranged or is collapsed
        if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
            return false;

        for (Element? current = element.Parent; current != null; current = current.Parent)
        {
            if (current is UIElement ui && !ui.IsVisible)
                return false;
        }

        return true;
    }

    private static bool IsAltOnlyDown(KeyEventArgs e)
        => e.Key == Key.None && e.Modifiers == ModifierKeys.Alt;

    private static bool IsAltOnlyUp(KeyEventArgs e)
        => e.Key == Key.None && !e.AltKey;

    private static char CharFromKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (char)('A' + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
        _ => default,
    };

    private readonly record struct AccessKeyEntry(UIElement Target, Action Activate);
}
