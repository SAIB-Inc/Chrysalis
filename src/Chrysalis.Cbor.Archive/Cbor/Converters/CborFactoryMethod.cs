using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public static partial class CborSerializerCore
{
    public static class CborReaderFactory
    {
        // This class remains unchanged as it doesn't use Registry
        private static readonly ConcurrentQueue<CborReader> ReaderPool = new();
        private const int MaxPoolSize = 32;

        public static CborReader Create(byte[] data)
        {
            if (ReaderPool.TryDequeue(out var reader))
            {
                reader.Reset(new ReadOnlyMemory<byte>(data));
                return reader;
            }
            return new CborReader(data);
        }

        public static void Return(CborReader reader)
        {
            if (ReaderPool.Count < MaxPoolSize)
            {
                ReaderPool.Enqueue(reader);
            }
        }
    }

    private static class PerformanceOptimizations
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();
        private static readonly ConcurrentDictionary<(Type, string), Delegate> GetterCache = new();

        public static PropertyInfo[] GetCachedProperties(Type type)
            => PropertyCache.GetOrAdd(type, t => Registry.GetProperties(t));

        public static Delegate GetCachedGetter(Type type, string propertyName)
            => GetterCache.GetOrAdd((type, propertyName),
                key => Registry.GetGetter(key.Item1, key.Item2));
    }

    private static class ValidationHelper
    {
        public static void ValidateSerializationPreconditions<T>(T value) where T : CborBase
        {
            if (value is not null)
            {
                _ = Registry.GetOptions(typeof(T)) ?? throw new InvalidOperationException(
                        $"Type {typeof(T)} is not registered for CBOR serialization");
            }
            else
            {
                throw new ArgumentNullException(nameof(value));
            }
        }

        public static void ValidateDeserializationPreconditions(byte[] data)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.Length == 0)
            {
                throw new ArgumentException("Empty data cannot be deserialized", nameof(data));
            }
        }
    }

    private static class PerformanceMonitor
    {
        // This class remains unchanged as it doesn't use Registry
        private static readonly ConcurrentDictionary<string, Metrics> OperationMetrics = new();

        public static IDisposable MeasureOperation(string operationName)
            => new OperationScope(operationName);

        private class OperationScope : IDisposable
        {
            private readonly string _operationName;
            private readonly Stopwatch _stopwatch;
            private readonly long _startMemory;

            public OperationScope(string operationName)
            {
                _operationName = operationName;
                _stopwatch = Stopwatch.StartNew();
                _startMemory = GC.GetTotalMemory(false);
            }

            public void Dispose()
            {
                _stopwatch.Stop();
                var memoryUsed = GC.GetTotalMemory(false) - _startMemory;

                OperationMetrics.AddOrUpdate(
                    _operationName,
                    new Metrics(_stopwatch.Elapsed, memoryUsed),
                    (_, existing) => existing.Update(_stopwatch.Elapsed, memoryUsed));
            }
        }

        private record Metrics
        {
            public TimeSpan AverageTime { get; private set; }
            public long AverageMemory { get; private set; }
            public long OperationCount { get; private set; }

            public Metrics(TimeSpan time, long memory)
            {
                AverageTime = time;
                AverageMemory = memory;
                OperationCount = 1;
            }

            public Metrics Update(TimeSpan newTime, long newMemory)
            {
                OperationCount++;
                AverageTime = TimeSpan.FromTicks(
                    (AverageTime.Ticks * (OperationCount - 1) + newTime.Ticks) / OperationCount);
                AverageMemory = (AverageMemory * (OperationCount - 1) + newMemory) / OperationCount;
                return this;
            }
        }
    }
}