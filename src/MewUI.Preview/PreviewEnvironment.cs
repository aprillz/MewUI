using System.Net;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Process-wide preview session parameters, read once from environment variables at first access.
/// </summary>
internal static class PreviewEnvironment
{
    private const string FEATURE_SWITCH = "Aprillz.MewUI.Preview.Enabled";
    private const string ENABLE_VARIABLE = "MEWUI_PREVIEW";
    private const string ENDPOINT_VARIABLE = "MEWUI_PREVIEW_ENDPOINT";
    private const string TOKEN_VARIABLE = "MEWUI_PREVIEW_TOKEN";
    private const string SESSION_VARIABLE = "MEWUI_PREVIEW_SESSION";

    static PreviewEnvironment()
    {
        // The AppContext switch is the trim-time gate (ILLink feature switch); the environment
        // variable is the runtime gate set by the IDE extension that spawned this process.
        bool featureEnabled = !(AppContext.TryGetSwitch(FEATURE_SWITCH, out bool enabled) && !enabled);
        if (!featureEnabled || Environment.GetEnvironmentVariable(ENABLE_VARIABLE) != "1")
        {
            return;
        }

        var endpointText = Environment.GetEnvironmentVariable(ENDPOINT_VARIABLE);
        if (!IPEndPoint.TryParse(endpointText ?? string.Empty, out var endpoint) || endpoint.Port == 0)
        {
            return;
        }

        Endpoint = endpoint;
        Token = Environment.GetEnvironmentVariable(TOKEN_VARIABLE) ?? string.Empty;
        SessionId = Environment.GetEnvironmentVariable(SESSION_VARIABLE) ?? Guid.NewGuid().ToString("N");
        IsActive = true;
    }

    /// <summary>Whether this process was started as a preview session.</summary>
    internal static bool IsActive { get; }

    /// <summary>Loopback endpoint the IDE extension is listening on.</summary>
    internal static IPEndPoint? Endpoint { get; }

    /// <summary>Bearer token expected in the IDE's Hello message.</summary>
    internal static string Token { get; } = string.Empty;

    /// <summary>Session id reported back to the IDE for reconnect correlation.</summary>
    internal static string SessionId { get; } = string.Empty;
}
