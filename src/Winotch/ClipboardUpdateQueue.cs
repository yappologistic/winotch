namespace Winotch;

public sealed class ClipboardUpdateQueue
{
    private uint _ignoredSequence;
    private uint? _pendingSequence;

    public void IgnoreSequence(uint sequence)
    {
        if (sequence != 0)
        {
            _ignoredSequence = sequence;
        }
    }

    public bool Enqueue(uint sequence)
    {
        if (_ignoredSequence != 0 && sequence == _ignoredSequence)
        {
            _ignoredSequence = 0;
            return false;
        }

        _pendingSequence = sequence;
        return true;
    }

    public uint? Consume()
    {
        var sequence = _pendingSequence;
        _pendingSequence = null;
        return sequence;
    }
}
