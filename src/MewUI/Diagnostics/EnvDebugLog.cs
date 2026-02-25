using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Diagnostics;

/// <summary>
/// Tiny logging helper that gates logs via environment variables and avoids interpolated string formatting
/// when disabled (via <see cref="InterpolatedStringHandlerAttribute" />).
/// </summary>
public static class EnvDebugLog
{
    private static readonly ConcurrentDictionary<string, bool> _enabledByEnvVar = new(StringComparer.Ordinal);

    public static bool IsEnabled(string envVar, bool defaultValue = false)
    {
        return _enabledByEnvVar.GetOrAdd(envVar, _ =>
        {
            var v = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrWhiteSpace(v))
            {
                return defaultValue;
            }

            if (string.Equals(v, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Non-empty but unrecognized values are treated as enabled to make "set anything" convenient.
            return true;
        });
    }

    public static void Write(string envVar, string tag, string message, bool defaultEnabled = false)
    {
        if (!IsEnabled(envVar, defaultEnabled))
        {
            return;
        }

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

    public static void Write(string envVar, string tag, ref MessageHandler message, bool defaultEnabled = false)
    {
        if (!message.Enabled)
        {
            return;
        }

        try
        {
            var line = $"{tag} {DateTime.Now:HH:mm:ss.fff} {message.ToStringAndClear()}";
            Console.WriteLine(line);
            Debug.WriteLine(line);
        }
        catch
        {
        }
    }

    public readonly struct Logger
    {
        private readonly string _envVar;
        private readonly string _tag;
        private readonly bool _defaultEnabled;

        public Logger(string envVar, string tag, bool defaultEnabled = false)
        {
            _envVar = envVar;
            _tag = tag;
            _defaultEnabled = defaultEnabled;
        }

        public bool IsEnabled()
            => EnvDebugLog.IsEnabled(_envVar, _defaultEnabled);

        public void Write(string message)
            => EnvDebugLog.Write(_envVar, _tag, message, _defaultEnabled);

        public void Write([InterpolatedStringHandlerArgument("")] ref Handler message)
            => EnvDebugLog.Write(_envVar, _tag, ref message._inner, _defaultEnabled);

        [InterpolatedStringHandler]
        public ref struct Handler
        {
            internal MessageHandler _inner;

            public Handler(int literalLength, int formattedCount, Logger logger, out bool enabled)
            {
                _inner = new MessageHandler(literalLength, formattedCount, logger._envVar, logger._tag, logger._defaultEnabled, out enabled);
            }

            public void AppendLiteral(string value) => _inner.AppendLiteral(value);
            public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);
            public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);
            public void AppendFormatted(string? value) => _inner.AppendFormatted(value);
            public void AppendFormatted(string? value, int alignment = 0, string? format = null) => _inner.AppendFormatted(value, alignment, format);
        }
    }

    [InterpolatedStringHandler]
    public ref struct MessageHandler
    {
        private DefaultInterpolatedStringHandler _inner;

        public bool Enabled { get; }

        public MessageHandler(int literalLength, int formattedCount, string envVar, string tag, bool defaultEnabled, out bool enabled)
        {
            enabled = IsEnabled(envVar, defaultEnabled);
            Enabled = enabled;
            _inner = enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        public void AppendLiteral(string value)
        {
            if (Enabled)
            {
                _inner.AppendLiteral(value);
            }
        }

        public void AppendFormatted<T>(T value)
        {
            if (Enabled)
            {
                _inner.AppendFormatted(value);
            }
        }

        public void AppendFormatted<T>(T value, string? format)
        {
            if (Enabled)
            {
                _inner.AppendFormatted(value, format);
            }
        }

        public void AppendFormatted(string? value)
        {
            if (Enabled)
            {
                _inner.AppendFormatted(value);
            }
        }

        public void AppendFormatted(string? value, int alignment = 0, string? format = null)
        {
            if (Enabled)
            {
                _inner.AppendFormatted(value, alignment, format);
            }
        }

        public string ToStringAndClear()
            => Enabled ? _inner.ToStringAndClear() : string.Empty;
    }
}
