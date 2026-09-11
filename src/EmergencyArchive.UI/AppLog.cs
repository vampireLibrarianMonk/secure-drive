using System.Diagnostics;

namespace EmergencyArchive.UI;

/// <summary>
/// Lightweight diagnostic logging for handled/degraded exceptions.
///
/// Writes to <see cref="Trace"/> — the sink already wired by
/// <c>BuildAvaloniaApp().LogToTrace()</c> — so a caught exception is never
/// silently lost, yet the detail does NOT go into the encrypted operation log
/// (which is metadata-only, spec section 20) or onto the UI. Use this at every
/// point where an exception is intentionally handled/degraded rather than
/// surfaced, so failures remain diagnosable.
/// </summary>
internal static class AppLog
{
    private const string Category = "EmergencyArchive";

    /// <summary>Records a handled exception with a short context string.</summary>
    public static void Handled(string context, Exception ex)
    {
        // Full type + message + stack go to trace only. Callers keep context
        // free of secrets (no passwords, keys, or document contents).
        Trace.TraceWarning($"[{Category}] Handled exception in {context}: {ex.GetType().Name}: {ex.Message}\n{ex}");
    }

    /// <summary>Records a non-exception diagnostic note (e.g. a degraded fallback).</summary>
    public static void Note(string context, string message)
    {
        Trace.TraceInformation($"[{Category}] {context}: {message}");
    }
}
