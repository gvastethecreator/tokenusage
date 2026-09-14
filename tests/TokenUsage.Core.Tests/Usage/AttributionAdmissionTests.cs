using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class AttributionAdmissionTests
{
    [Fact]
    public void DisabledConsentDoesNotSelectAHistoricalEpoch()
    {
        AttributionConsent consent = new(
            AttributionCapability.CodexSession,
            AttributionConsentState.DisabledAfterPurge,
            Epoch: 2,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        Assert.Equal(0, consent.ActiveLinkEpoch);
        Assert.False(consent.AllowsLinks);
    }

    [Fact]
    public void HistoricalEventKeysAndEnableTimeBlockAutomaticLinks()
    {
        DateTimeOffset enabled = new(2026, 9, 13, 16, 0, 0, TimeSpan.Zero);
        AttributionConsent consent = new(
            AttributionCapability.CodexSession,
            AttributionConsentState.Enabled,
            Epoch: 1,
            UpdatedAtUtc: enabled,
            EnabledAtUtc: enabled);
        var historical = new HashSet<string>(StringComparer.Ordinal) { "old" };
        Assert.False(AttributionAdmission.AdmitsAutomaticLink(
            consent,
            enabled.AddHours(-1),
            "new",
            historical));
        Assert.False(AttributionAdmission.AdmitsAutomaticLink(
            consent,
            enabled.AddHours(1),
            "old",
            historical));
        Assert.True(AttributionAdmission.AdmitsAutomaticLink(
            consent,
            enabled.AddHours(1),
            "new",
            historical));
        Assert.True(AttributionAdmission.AdmitsAutomaticLink(
            consent,
            enabled.AddHours(-1),
            "old",
            historical,
            backfillFromInclusive: new DateOnly(2026, 9, 12),
            backfillToInclusive: new DateOnly(2026, 9, 13)));
        Assert.True(AttributionAdmission.AdmitsAutomaticLink(
            consent,
            enabled.AddHours(-1),
            "old",
            historical,
            committedEventKeys: historical));
        Assert.False(AttributionAdmission.AdmitsAutomaticLink(
            consent,
            enabled.AddHours(-1),
            "old",
            historical,
            committedEventKeys: new HashSet<string>(StringComparer.Ordinal) { "other" }));
    }

    [Fact]
    public void LiveFilterDropsLinksAfterRevoke()
    {
        UsageSessionLink link = new(
            new UsageEventKey(new string('a', 64)),
            new OpaqueAttributionKey(new string('b', 64)),
            parentSessionKey: null,
            consentEpoch: 1);
        AttributionConsent enabled = new(
            AttributionCapability.CodexSession,
            AttributionConsentState.Enabled,
            Epoch: 1,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            EnabledAtUtc: DateTimeOffset.UtcNow);
        AttributionConsent revoked = enabled with
        {
            State = AttributionConsentState.PurgePending,
            Epoch = 2,
            EnabledAtUtc = null,
        };
        Assert.Single(AttributionAdmission.FilterByLiveConsent([link], [enabled]));
        Assert.Empty(AttributionAdmission.FilterByLiveConsent([link], [revoked]));
    }
}
