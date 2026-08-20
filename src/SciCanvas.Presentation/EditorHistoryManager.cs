using System.Text.Json;

namespace SciCanvas.Presentation;

internal sealed class EditorHistoryManager
{
    private static readonly JsonSerializerOptions FingerprintOptions = new()
    {
        WriteIndented = false,
    };

    private readonly int _capacity;
    private readonly TimeSpan _mergeWindow;
    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];
    private HistoryEntry? _current;
    private string? _savedFingerprint;
    private DateTimeOffset _lastRecordAt;

    public EditorHistoryManager(int capacity, TimeSpan? mergeWindow = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _mergeWindow = mergeWindow ?? TimeSpan.FromMilliseconds(750);
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public EditorHistorySnapshot? CurrentSnapshot => _current?.Snapshot;

    public bool IsDirty => _current is not null &&
                           !string.Equals(
                               _current.Fingerprint,
                               _savedFingerprint,
                               StringComparison.Ordinal);

    public void Reset(EditorHistorySnapshot snapshot, bool markSaved)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _undo.Clear();
        _redo.Clear();
        _current = CreateEntry(snapshot);
        if (markSaved)
        {
            _savedFingerprint = _current.Fingerprint;
        }
        else
        {
            _savedFingerprint = null;
        }

        BreakCoalescing();
    }

    public void ResetPreservingSavedState(EditorHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _undo.Clear();
        _redo.Clear();
        _current = CreateEntry(snapshot);
        BreakCoalescing();
    }

    public bool Record(EditorHistorySnapshot snapshot, bool canCoalesce)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        HistoryEntry next = CreateEntry(snapshot);
        if (_current is null)
        {
            _current = next;
            return true;
        }

        if (string.Equals(_current.Fingerprint, next.Fingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool merge = canCoalesce &&
                     _undo.Count > 0 &&
                     now - _lastRecordAt <= _mergeWindow;
        if (!merge)
        {
            _undo.Add(_current);
            if (_undo.Count > _capacity)
            {
                _undo.RemoveAt(0);
            }
        }

        _current = next;
        _redo.Clear();
        _lastRecordAt = now;
        return true;
    }

    public EditorHistorySnapshot? Undo()
    {
        if (_current is null || _undo.Count == 0)
        {
            return null;
        }

        _redo.Add(_current);
        _current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        BreakCoalescing();
        return _current.Snapshot;
    }

    public EditorHistorySnapshot? Redo()
    {
        if (_current is null || _redo.Count == 0)
        {
            return null;
        }

        _undo.Add(_current);
        _current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        BreakCoalescing();
        return _current.Snapshot;
    }

    public void MarkSaved(EditorHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        HistoryEntry entry = CreateEntry(snapshot);
        _current = entry;
        _savedFingerprint = entry.Fingerprint;
        BreakCoalescing();
    }

    public void BreakCoalescing() => _lastRecordAt = DateTimeOffset.MinValue;

    private static HistoryEntry CreateEntry(EditorHistorySnapshot snapshot) =>
        new(snapshot, JsonSerializer.Serialize(snapshot, FingerprintOptions));

    private sealed record HistoryEntry(EditorHistorySnapshot Snapshot, string Fingerprint);
}
