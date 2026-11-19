using System.Runtime.ExceptionServices;

namespace Chrysalis.Codec.Test;

internal static class AllocationAssert
{
    private const int MaxGen2Retries = 5;

    public static long Measure(Action warmUp, Action measure, Action? verify = null) =>
        MeasureMedian(warmUp, () =>
        {
            long allocated = MeasureOnce(measure);
            verify?.Invoke();
            return allocated;
        });

    // A cold-path sample can create and dispose fresh state around MeasureOnce.
    public static long MeasureCold(Func<long> sample) => MeasureMedian(null, sample);

    private static long MeasureMedian(Action? warmUp, Func<long> sample)
    {
        long median = 0;
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                int gen2 = GC.CollectionCount(2);
                GC.WaitForPendingFinalizers();
                warmUp?.Invoke();
                // Ignore one noisy attempt without letting one clean attempt hide recurring allocations.
                Span<long> samples = stackalloc long[3];
                for (int attempt = 0; attempt < samples.Length; attempt++)
                {
                    long measured;
                    for (int retry = 0; ; retry++)
                    {
                        measured = sample();
                        int afterGen2 = GC.CollectionCount(2);
                        if (afterGen2 == gen2 || retry == MaxGen2Retries)
                        {
                            gen2 = afterGen2;
                            break;
                        }
                        // A gen-2 GC under memory pressure can trim shared pools on the finalizer thread.
                        gen2 = afterGen2;
                        GC.WaitForPendingFinalizers();
                        warmUp?.Invoke();
                    }
                    samples[attempt] = measured;
                }
                samples.Sort();
                median = samples[1];
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
        return median;
    }

    public static long MeasureOnce(Action measure)
    {
        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        measure();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
