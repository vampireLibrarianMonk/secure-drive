namespace EmergencyArchive.Core;

/// <summary>
/// Application-side rate limiting for password attempts (spec section 19):
/// a minimum interval is enforced between consecutive unlock attempts in the
/// user interface.
/// </summary>
/// <remarks>
/// This is an anti-hammering convenience only, NOT a security boundary: an
/// attacker in possession of the drive performs offline attacks against the
/// vault's scrypt key derivation directly, bypassing the application entirely.
/// The archive password strength and the KDF cost parameters remain the real
/// defense (spec section 19; see docs/THREAT-MODEL.md).
/// </remarks>
public sealed class AttemptRateLimiter
{
    /// <summary>Minimum time that must pass between two password attempts.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    private DateTimeOffset? lastAttemptUtc;

    /// <summary>
    /// Registers an attempt at the given time and returns the earliest time at
    /// which the NEXT attempt may be performed (the attempt time plus the
    /// minimum interval). A first-ever attempt is never blocked.
    /// </summary>
    public DateTimeOffset RegisterAttempt(DateTimeOffset attemptTimeUtc)
    {
        lastAttemptUtc = attemptTimeUtc;
        return attemptTimeUtc + MinimumInterval;
    }

    /// <summary>How long the caller must still wait before the next attempt.</summary>
    public TimeSpan RemainingDelay(DateTimeOffset nowUtc)
    {
        if (lastAttemptUtc is null)
        {
            return TimeSpan.Zero;
        }

        DateTimeOffset earliestNext = lastAttemptUtc.Value + MinimumInterval;
        return earliestNext > nowUtc ? earliestNext - nowUtc : TimeSpan.Zero;
    }
}
