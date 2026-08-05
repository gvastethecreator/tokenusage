using System;
using System.Collections.Generic;
using WOpenUsage.Core.Layout;

namespace WOpenUsage.App.ViewModels;

/// <summary>
/// Bounded undo stack of dashboard layouts, oldest-to-newest.
/// </summary>
public sealed class DashboardLayoutSessionHistory
{
    private readonly List<DashboardLayout> _entries;
    private readonly int _capacity;

    public DashboardLayoutSessionHistory(int capacity = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _entries = new List<DashboardLayout>(capacity);
    }

    public int Count => _entries.Count;

    public bool CanUndo => _entries.Count > 0;

    public void Record(DashboardLayout previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        if (_entries.Count > 0 && Equals(_entries[^1], previous))
            return;

        if (_entries.Count >= _capacity)
            _entries.RemoveAt(0);

        _entries.Add(previous);
    }

    public bool TryPeek(out DashboardLayout? layout)
    {
        if (_entries.Count == 0)
        {
            layout = null;
            return false;
        }

        layout = _entries[^1];
        return true;
    }

    public bool CommitUndo(DashboardLayout expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        if (_entries.Count == 0 || !Equals(_entries[^1], expected))
            return false;

        _entries.RemoveAt(_entries.Count - 1);
        return true;
    }

    public void Clear() => _entries.Clear();
}
