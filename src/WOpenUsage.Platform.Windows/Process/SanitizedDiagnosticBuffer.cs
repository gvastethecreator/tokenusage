using System.Text;

namespace WOpenUsage.Platform.Windows.Processes;

internal sealed class SanitizedDiagnosticBuffer
{
    private const int MaximumRawLineCharacters = 2048;
    private const string RemovedLongLine = "[diagnostic line removed]";

    private readonly object _sync = new();
    private readonly int _maximumCharacters;
    private readonly StringBuilder _pendingLine = new();
    private readonly StringBuilder _sanitized = new();
    private bool _discardLongLine;

    internal SanitizedDiagnosticBuffer(int maximumCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        _maximumCharacters = maximumCharacters;
    }

    internal void Append(ReadOnlySpan<char> value)
    {
        lock (_sync)
        {
            foreach (char character in value)
            {
                if (_discardLongLine)
                {
                    if (character == '\n')
                    {
                        _discardLongLine = false;
                    }

                    continue;
                }

                _pendingLine.Append(character);
                if (character == '\n')
                {
                    FlushPendingLine();
                }
                else if (_pendingLine.Length > MaximumRawLineCharacters)
                {
                    _pendingLine.Clear();
                    AppendSanitized(RemovedLongLine + Environment.NewLine);
                    _discardLongLine = true;
                }
            }
        }
    }

    internal void Complete()
    {
        lock (_sync)
        {
            if (!_discardLongLine && _pendingLine.Length > 0)
            {
                FlushPendingLine();
            }

            _pendingLine.Clear();
            _discardLongLine = false;
        }
    }

    internal string Snapshot()
    {
        lock (_sync)
        {
            return _sanitized.ToString();
        }
    }

    private void FlushPendingLine()
    {
        string line = _pendingLine.ToString();
        _pendingLine.Clear();
        AppendSanitized(DiagnosticTextSanitizer.Sanitize(line));
    }

    private void AppendSanitized(string value)
    {
        _sanitized.Append(value);
        if (_sanitized.Length > _maximumCharacters)
        {
            _sanitized.Remove(0, _sanitized.Length - _maximumCharacters);
        }
    }
}
