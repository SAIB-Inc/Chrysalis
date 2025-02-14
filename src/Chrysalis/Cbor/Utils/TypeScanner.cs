using System.Collections.Concurrent;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Utils;

internal static class TypeScanner
{
    private static readonly ConcurrentDictionary<Type, bool> _cborTypeCache = new();

    internal static IEnumerable<Type> GetCborTypes()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .AsParallel()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(IsCborType);
    }

    internal static bool IsCborType(Type type)
    {
        return _cborTypeCache.GetOrAdd(type, t =>
        {
            if (t == null) return false;

            var current = t;
            while (current != null && current != typeof(object))
            {
                if (current == typeof(CborBase))
                    return true;
                current = current.BaseType;
            }
            return false;
        });
    }
}