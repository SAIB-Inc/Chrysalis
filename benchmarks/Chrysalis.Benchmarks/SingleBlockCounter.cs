// Shared by the legacy client and the harness regression controls. Record never
// throws, so even an invalid duplicate response is drained through BatchDone.
internal record struct SingleBlockCounter
{
    private int bytes;

    internal int BodyCount { get; private set; }

    internal void Record(int length)
    {
        BodyCount++;
        bytes = length;
    }

    internal readonly int Complete()
    {
        if (BodyCount != 1) { throw new InvalidDataException($"Expected exactly one BlockBody for a single-point request, received {BodyCount}."); }
        return bytes;
    }
}
