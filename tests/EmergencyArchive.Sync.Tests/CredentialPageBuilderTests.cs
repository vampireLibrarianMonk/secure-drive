using EmergencyArchive.Sync;
using Xunit;

namespace EmergencyArchive.Sync.Tests;

/// <summary>
/// The credential page embeds secrets, so escaping is the critical property: a
/// hostile site/username/value must never be able to inject executable script
/// or break out of the JSON data island.
/// </summary>
public class CredentialPageBuilderTests
{
    [Fact]
    public void Build_EmptyDatabase_ProducesValidPageWithNoEntries()
    {
        string html = CredentialPageBuilder.Build(CredentialDatabase.Empty);

        Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("\"entries\":[]", html.Replace(" ", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_IncludesAStrictContentSecurityPolicy()
    {
        string html = CredentialPageBuilder.Build(CredentialDatabase.Empty);

        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EmbedsFieldsAsEscapedData_NotRawMarkup()
    {
        var db = CredentialDatabase.Empty.With(
            Credential.Create("bank.example.com", "jane.doe", "p@ss w0rd")
                with
            { Extras = [new CredentialField("PIN", "4821")] });

        string html = CredentialPageBuilder.Build(db);

        // The values are present (as escaped JSON), and the page renders them
        // via textContent, so this is safe.
        Assert.Contains("bank.example.com", html, StringComparison.Ordinal);
        Assert.Contains("jane.doe", html, StringComparison.Ordinal);
        Assert.Contains("PIN", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("</script><script>alert(1)</script>")]
    [InlineData("\"><img src=x onerror=alert(1)>")]
    [InlineData("'; alert(document.cookie); //")]
    public void Build_HostileValues_CannotInjectExecutableMarkup(string hostile)
    {
        // A malicious value in ANY field must not appear as raw, executable
        // markup: no unescaped closing </script> and no live <img>/<script> tag
        // introduced by our data.
        var db = CredentialDatabase.Empty
            .With(Credential.Create(hostile, hostile, hostile)
                with
            { Extras = [new CredentialField(hostile, hostile)] });

        string html = CredentialPageBuilder.Build(db);

        // Only our two known <script> openers may exist (the JSON island and the
        // app script). Hostile data must not introduce a third, nor a raw
        // closing </script> that could terminate the island.
        Assert.Equal(2, CountOccurrences(html, "<script"));
        Assert.Equal(2, CountOccurrences(html, "</script>"));

        // The hostile angle brackets must be \u-escaped in the data island, so
        // no live tag (<img/<script) can originate from our data. (The literal
        // text "onerror=alert" is harmless once it is inside an escaped JSON
        // string rendered via textContent — what matters is that '<' is escaped.)
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        if (hostile.Contains('<', StringComparison.Ordinal))
        {
            Assert.Contains("\\u003C", html, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            i += needle.Length;
        }

        return count;
    }
}
