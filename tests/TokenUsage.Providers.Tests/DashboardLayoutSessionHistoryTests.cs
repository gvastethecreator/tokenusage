using TokenUsage.App.ViewModels;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Tests;

public sealed class DashboardLayoutSessionHistoryTests
{
    [Fact]
    public void StartsEmptyAndRejectsInvalidInputs()
    {
        var history = new DashboardLayoutSessionHistory();

        Assert.Equal(0, history.Count);
        Assert.False(history.CanUndo);
        Assert.False(history.TryPeek(out DashboardLayout? empty));
        Assert.Null(empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DashboardLayoutSessionHistory(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DashboardLayoutSessionHistory(-1));
        Assert.Throws<ArgumentNullException>(() => history.Record(null!));
        Assert.Throws<ArgumentNullException>(() => history.CommitUndo(null!));
    }

    [Fact]
    public void RecordsAndCommitsLayoutsInLifoOrder()
    {
        var history = new DashboardLayoutSessionHistory();
        DashboardLayout first = Layout("first");
        DashboardLayout second = Layout("second");

        history.Record(first);
        history.Record(second);

        Assert.True(history.TryPeek(out DashboardLayout? newest));
        Assert.Equal(second, newest);
        Assert.True(history.CommitUndo(second));
        Assert.True(history.TryPeek(out DashboardLayout? remaining));
        Assert.Equal(first, remaining);
        Assert.True(history.CommitUndo(first));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void EqualNewestEntryIsSuppressed()
    {
        var history = new DashboardLayoutSessionHistory();
        DashboardLayout first = Layout("first");

        history.Record(first);
        history.Record(Layout("first"));

        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void CapacityEvictsOldestEntry()
    {
        var history = new DashboardLayoutSessionHistory(capacity: 2);
        DashboardLayout first = Layout("first");
        DashboardLayout second = Layout("second");
        DashboardLayout third = Layout("third");

        history.Record(first);
        history.Record(second);
        history.Record(third);

        Assert.Equal(2, history.Count);
        Assert.True(history.CommitUndo(third));
        Assert.True(history.CommitUndo(second));
        Assert.False(history.TryPeek(out _));
    }

    [Fact]
    public void MismatchedCommitPreservesHistory()
    {
        var history = new DashboardLayoutSessionHistory();
        DashboardLayout recorded = Layout("recorded");

        history.Record(recorded);

        Assert.False(history.CommitUndo(Layout("other")));
        Assert.Equal(1, history.Count);
        Assert.True(history.TryPeek(out DashboardLayout? remaining));
        Assert.Equal(recorded, remaining);
    }

    [Fact]
    public void ClearRemovesEveryEntry()
    {
        var history = new DashboardLayoutSessionHistory();
        history.Record(Layout("first"));
        history.Record(Layout("second"));

        history.Clear();

        Assert.Equal(0, history.Count);
        Assert.False(history.CanUndo);
    }

    private static DashboardLayout Layout(string providerId) => new(
    [
        new ProviderLayoutPreference(
            new ProviderId(providerId),
            isVisible: true,
            isHighlighted: false,
            metrics: []),
    ]);
}
