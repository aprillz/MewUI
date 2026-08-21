using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Diagnostics;

internal static class EnvironmentDebugLoggingGate
{
    private const string ENABLED_SWITCH = "Aprillz.MewUI.EnvironmentDebugLogging.Enabled";

    private static readonly bool _isSupported =
        !AppContext.TryGetSwitch(ENABLED_SWITCH, out bool enabled) || enabled;

    internal static bool IsSupported => _isSupported;
}

/// <summary>
/// Environment-variable-backed on/off switches for debug logging.
/// </summary>
internal static class EnvDebugSwitches
{
    private static readonly ConcurrentDictionary<string, bool> _values = new(StringComparer.Ordinal);

    public static bool IsOn(string envVar)
    {
        return _values.GetOrAdd(envVar, static key =>
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        });
    }
}

/// <summary>
/// Tagged debug logger controlled by an environment variable.
/// Interpolated string logging avoids formatting work when disabled.
/// </summary>
internal sealed class EnvDebugLogger
{
    private readonly string _envVar;
    private readonly string _tag;

    public EnvDebugLogger(string envVar, string tag)
    {
        _envVar = envVar;
        _tag = tag;
    }

    public bool Enabled => EnvironmentDebugLoggingGate.IsSupported && EnvDebugSwitches.IsOn(_envVar);

    public void Write(string message)
    {
        if (!EnvironmentDebugLoggingGate.IsSupported || !EnvDebugSwitches.IsOn(_envVar))
        {
            return;
        }

        Emit(_tag, message);
    }

    public void Write([InterpolatedStringHandlerArgument("")] ref Handler message)
    {
        if (!EnvironmentDebugLoggingGate.IsSupported || !message.IsActive)
        {
            return;
        }

        Emit(_tag, message.ToStringAndClear());
    }

    private static void Emit(string tag, string message)
    {
        try
        {
            var line = $"{tag} {DateTime.Now:HH:mm:ss.fff} {message}";
            Console.WriteLine(line);
            Debug.WriteLine(line);
        }
        catch
        {
        }
    }

    [InterpolatedStringHandler]
    public ref struct Handler
    {
        private DefaultInterpolatedStringHandler _builder;

        public bool IsActive { get; }

        public Handler(int literalLength, int formattedCount, EnvDebugLogger logger, out bool enabled)
        {
            enabled = EnvironmentDebugLoggingGate.IsSupported && logger.Enabled;
            IsActive = enabled;
            _builder = enabled
                ? new DefaultInterpolatedStringHandler(literalLength, formattedCount)
                : default;
        }

        public void AppendLiteral(string value)
        {
            if (IsActive)
            {
                _builder.AppendLiteral(value);
            }
        }

        public void AppendFormatted<T>(T value)
        {
            if (IsActive)
            {
                _builder.AppendFormatted(value);
            }
        }

        public void AppendFormatted<T>(T value, string? format)
        {
            if (IsActive)
            {
                _builder.AppendFormatted(value, format);
            }
        }

        public void AppendFormatted(string? value)
        {
            if (IsActive)
            {
                _builder.AppendFormatted(value);
            }
        }

        public void AppendFormatted(string? value, int alignment = 0, string? format = null)
        {
            if (IsActive)
            {
                _builder.AppendFormatted(value, alignment, format);
            }
        }

        public string ToStringAndClear()
            => IsActive ? _builder.ToStringAndClear() : string.Empty;
    }
}
