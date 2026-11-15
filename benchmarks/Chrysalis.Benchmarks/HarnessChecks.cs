namespace Chrysalis.Benchmarks;

internal static class HarnessChecks
{
    internal static void Run()
    {
        List<Exception> failures = [];
        Check(nameof(NoBlocksIsNotCounted), NoBlocksIsNotCounted, failures);
        Check(nameof(DuplicateBodiesAreRejectedAfterDraining), DuplicateBodiesAreRejectedAfterDraining, failures);
        Check(nameof(OneBodyIsCounted), OneBodyIsCounted, failures);
        Check(nameof(OpaqueDefiniteBytesDoNotAllocate), OpaqueDefiniteBytesDoNotAllocate, failures);
        Check(nameof(OpaqueBytesRoundTrip), OpaqueBytesRoundTrip, failures);
        if (failures.Count != 0) { throw new AggregateException(failures); }
    }

    private static void Check(string name, Action test, List<Exception> failures)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (InvalidDataException exception) { failures.Add(exception); Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
    }

    private static void NoBlocksIsNotCounted()
    {
        SingleBlockCounter counter = default;
        if (counter.BodyCount != 0) { throw new InvalidDataException("A new counter already has a body."); }
        try { _ = counter.Complete(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("A zero-body single-point response was accepted.");
    }

    private static void DuplicateBodiesAreRejectedAfterDraining()
    {
        SingleBlockCounter counter = default;
        counter.Record(10);
        counter.Record(20); // Must not throw: the caller must drain the rest of the batch.
        try { _ = counter.Complete(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("A duplicate-body single-point response was accepted.");
    }

    private static void OneBodyIsCounted()
    {
        SingleBlockCounter counter = default;
        counter.Record(123);
        if (counter.Complete() != 123) { throw new InvalidDataException("One body was not counted correctly."); }
    }

    private static void OpaqueDefiniteBytesDoNotAllocate()
    {
        byte[] bytes = [0x44, 1, 2, 3, 4];
        for (int i = 0; i < 1000; i++) { ResultSink<long>.Value = RawCbor.ReadEveryToken(bytes); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { ResultSink<long>.Value = RawCbor.ReadEveryToken(bytes); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0) { throw new InvalidDataException($"Opaque definite-byte traversal allocated {allocated} bytes instead of zero."); }
    }

    private static void OpaqueBytesRoundTrip()
    {
        byte[] definite = [0x44, 1, 2, 3, 4];
        byte[] indefinite = [0x5f, 0x42, 1, 2, 0x42, 3, 4, 0xff];
        if (RawCbor.ReadEveryToken(definite) != 11 || RawCbor.ReadEveryToken(indefinite) != 11
            || !RawCbor.EncodeEveryToken(definite).Span.SequenceEqual(definite)
            || !RawCbor.EncodeEveryToken(indefinite).Span.SequenceEqual(definite))
        {
            throw new InvalidDataException("Opaque byte-string traversal or re-encoding changed contents.");
        }
    }
}
