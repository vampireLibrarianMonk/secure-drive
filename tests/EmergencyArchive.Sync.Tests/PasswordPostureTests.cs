using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>
/// Red/blue cycle (2026-11) — password-related attacks: an exported KeePass
/// file leaves the encrypted vault, so its KDF must not be the weakest link.
/// The exporter pins a posture matching the archive (1 GiB Argon2id, 4 passes)
/// and stays within the importer's hardening caps (≤ 2 GiB / ≤ 64 passes), so
/// our own export is always re-importable by our own app.
/// </summary>
public sealed class PasswordPostureTests
{
    [Fact]
    public void ExportArgon2Cost_MatchesTheArchivePosture_AndImporterCaps()
    {
        var cost = KdbxCredentialMapper.ExportArgon2Cost;

        Assert.True(cost.MemoryBytes >= 512UL * 1024 * 1024, "export memory too weak");
        Assert.True(cost.Iterations >= 2, "export iterations too weak");
        Assert.True(cost.MemoryBytes <= 2UL * 1024 * 1024 * 1024, "export memory exceeds the importer cap");
        Assert.True(cost.Iterations <= 64, "export iterations exceed the importer cap");
    }
}
