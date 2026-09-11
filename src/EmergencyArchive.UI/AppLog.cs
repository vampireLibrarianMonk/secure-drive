using System.Diagnostics;

namespace EmergencyArchive.UI;

/// <summary>
/// Diagnostic logging for handled/degraded exceptions. Fans out to two sinks:
///
///  1. <see cref="Trace"/> (always) — FULL detail (exception type, message,
///     stack) for developers. Never persisted, never shown to the user.
///  2. The activity log (when a sink is registered and the vault is unlocked)
///     — a SANITIZED, human event: the context label and exception TYPE only,
///     never the exception message, stack, paths, or any content. This honors
///     the rule that the encrypted operation log holds metadata only
///     (spec section 20 — no secrets, keys, or document text).
///
/// The activity sink is registered by <see cref="MainWindowViewModel"/>, which
/// owns the operation log across the session lifecycle. Events raised with no
/// unlocked session (e.g. a failed unlock) still reach Trace.
/// </summary>
internal static class AppLog
{
    private const string Category = "EmergencyArchive";

    /// <summary>Receives sanitized (category, message) diagnostics for the activity log.</summary>
    public static Action<string, string>? ActivitySink { get; set; }

    /// <summary>Records a handled exception: full detail to trace, a sanitized event to the activity log.</summary>
    public static void Handled(string context, Exception ex)
    {
        Trace.TraceWarning($"[{Category}] Handled exception in {context}: {ex.GetType().Name}: {ex.Message}\n{ex}");

        // Sanitized: exception TYPE + context only — no message/stack/paths.
        TryActivity("Diagnostics", $"Handled a {ex.GetType().Name} during {context}.");
    }

    /// <summary>Records a non-exception diagnostic note (already sanitized by the caller).</summary>
    public static void Note(string context, string message)
    {
        Trace.TraceInformation($"[{Category}] {context}: {message}");
        TryActivity("Diagnostics", $"{context}: {message}");
    }

    private static void TryActivity(string category, string message)
    {
        Action<string, string>? sink = ActivitySink;
        if (sink is null)
        {
            return;
        }

        try
        {
            sink(category, message);
        }
        catch (Exception e)
        {
            // The activity sink must never itself throw into a catch handler;
            // fall back to trace only.
            Trace.TraceError($"[{Category}] Activity sink failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
