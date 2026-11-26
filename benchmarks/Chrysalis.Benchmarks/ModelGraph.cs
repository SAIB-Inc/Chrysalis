using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;

namespace Chrysalis.Benchmarks;

// Reflection/expression compilation happens in untimed setup. Measured property calls are
// typed, including nullable values and concrete structs; only the SDK's unions dispatch
// through an object. No PropertyInfo.GetValue, MethodInfo.Invoke or DynamicInvoke is timed.
internal static class ModelGraph
{
    private static readonly ConcurrentDictionary<Type, Func<object, long>> UnionReaders = new();
    private static readonly ConcurrentDictionary<Type, Func<object, object>> UnionRebuilders = new();
    private static readonly ConcurrentDictionary<string, byte> Coverage = new(StringComparer.Ordinal);

    internal static long Read<T>(T value) => Reader<T>.Read(value);
    internal static T Fresh<T>(T value) => Rebuilder<T>.Rebuild(value);
    internal static string[] CoveredProperties => [.. Coverage.Keys.Order(StringComparer.Ordinal)];

    private static PropertyInfo[] Properties(Type type) => [.. type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null
            && property.Name is not "Raw" and not "ConstrIndex" and not "IsIndefinite")
        .OrderBy(property => property.MetadataToken)];

    private static MethodInfo Method(string name) => typeof(ModelGraph).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static long ReadUnion(object? value)
    {
        if (value is null) { return 0; }
        return UnionReaders.GetOrAdd(value.GetType(), static type =>
        {
            ParameterExpression item = Expression.Parameter(typeof(object));
            return Expression.Lambda<Func<object, long>>(Expression.Call(typeof(ModelGraph), nameof(Read), [type],
                Expression.Convert(item, type)), item).Compile();
        })(value);
    }

    private static object? FreshUnion(object? value)
    {
        if (value is null) { return null; }
        return UnionRebuilders.GetOrAdd(value.GetType(), static type =>
        {
            ParameterExpression item = Expression.Parameter(typeof(object));
            return Expression.Lambda<Func<object, object>>(Expression.Convert(Expression.Call(typeof(ModelGraph), nameof(Fresh),
                [type], Expression.Convert(item, type)), typeof(object)), item).Compile();
        })(value);
    }

    private static long ReadNullable<T>(T? value) where T : struct => value.HasValue ? Read(value.Value) : 0;
    private static T? FreshNullable<T>(T? value) where T : struct => value.HasValue ? Fresh(value.Value) : null;
    private static long ReadItems<T>(IEnumerable<T> values)
    {
        long sum = 0;
        foreach (T value in values) { sum = unchecked(sum + Read(value)); }
        return sum;
    }

#if !CHRYSALIS_PINNED_HARNESS
    private static long ReadCborList<T>(CborList<T> values)
    {
        long sum = 0;
        foreach (T value in values) { sum = unchecked(sum + Read(value)); }
        return sum;
    }

    private static CborList<T> FreshCborList<T>(CborList<T> values)
    {
        List<T> result = [];
        foreach (T value in values) { result.Add(Fresh(value)); }
        return CborList.Create(result, values.IsIndefinite, values.HasSetTag);
    }
#endif

#if !CHRYSALIS_PINNED_HARNESS && !CHRYSALIS_NO_CBORMAP
    private static long ReadCborMap<TKey, TValue>(CborMap<TKey, TValue> values) where TKey : notnull
    {
        long sum = 0;
        foreach ((TKey key, TValue value) in values) { sum = unchecked(sum + Read(key) + Read(value)); }
        return sum;
    }

    private static CborMap<TKey, TValue> FreshCborMap<TKey, TValue>(CborMap<TKey, TValue> values) where TKey : notnull
    {
        List<KeyValuePair<TKey, TValue>> result = [];
        foreach ((TKey key, TValue value) in values) { result.Add(new(Fresh(key), Fresh(value))); }
        return CborMap<TKey, TValue>.Create(result, values.IsIndefinite);
    }
#endif

    private static List<T> FreshList<T>(IEnumerable<T> values)
    {
        List<T> result = [];
        foreach (T value in values) { result.Add(Fresh(value)); }
        return result;
    }

    private static Dictionary<TKey, TValue> FreshMap<TKey, TValue>(Dictionary<TKey, TValue> values) where TKey : notnull
    {
        Dictionary<TKey, TValue> result = new(values.Count, values.Comparer);
        foreach ((TKey key, TValue value) in values) { result.Add(Fresh(key), Fresh(value)); }
        return result;
    }

    private static long ReadMemory(ReadOnlyMemory<byte> value)
    {
        long sum = value.Length;
        foreach (byte item in value.Span) { sum += item; }
        return sum;
    }

    private static long ReadOpaque(CborAny value) => RawCbor.ReadEveryToken(value.Raw);
    private static CborAny FreshOpaque(CborAny value) => new(RawCbor.EncodeEveryToken(value.Raw));
    private static long ReadEmbedded(CborEncodedValue value) => RawCbor.ReadEveryToken(value.Value);
    private static CborEncodedValue FreshEmbedded(CborEncodedValue value) => CborEncodedValue.Create(RawCbor.EncodeEveryToken(value.Value));

    private static class Reader<T>
    {
        internal static readonly Func<T, long> Read = Create();

        private static Func<T, long> Create()
        {
            Type type = typeof(T);
            ParameterExpression item = Expression.Parameter(type);
            Expression body;
            Type? nullable = Nullable.GetUnderlyingType(type);
            Type? sequence = type.GetInterfaces().FirstOrDefault(candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (type.IsInterface || type == typeof(object))
            {
                body = Expression.Call(Method(nameof(ReadUnion)), Expression.Convert(item, typeof(object)));
            }
            else if (nullable is not null) { body = Expression.Call(Method(nameof(ReadNullable)).MakeGenericMethod(nullable), item); }
            else if (type == typeof(ReadOnlyMemory<byte>)) { body = Expression.Call(Method(nameof(ReadMemory)), item); }
            else if (type == typeof(CborAny)) { body = Expression.Call(Method(nameof(ReadOpaque)), item); }
            else if (type == typeof(CborEncodedValue)) { body = Expression.Call(Method(nameof(ReadEmbedded)), item); }
            else if (type == typeof(string)) { body = Expression.Convert(Expression.Property(item, nameof(string.Length)), typeof(long)); }
            else if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(System.Numerics.BigInteger))
            {
                body = Expression.Convert(Expression.Call(item, type.GetMethod(nameof(GetHashCode), Type.EmptyTypes)!), typeof(long));
            }
#if !CHRYSALIS_PINNED_HARNESS
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CborList<>))
            {
                body = Expression.Call(Method(nameof(ReadCborList)).MakeGenericMethod(type.GenericTypeArguments), item);
            }
#endif
#if !CHRYSALIS_PINNED_HARNESS && !CHRYSALIS_NO_CBORMAP
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CborMap<,>))
            {
                body = Expression.Call(Method(nameof(ReadCborMap)).MakeGenericMethod(type.GenericTypeArguments), item);
            }
#endif
            else if (sequence is not null)
            {
                body = Expression.Call(Method(nameof(ReadItems)).MakeGenericMethod(sequence.GenericTypeArguments), item);
            }
            else
            {
                PropertyInfo[] properties = Properties(type);
                if (properties.Length == 0) { throw new NotSupportedException("Unwalked model: " + type.FullName); }
                body = Expression.Constant(0L);
                foreach (PropertyInfo property in properties)
                {
                    _ = Coverage.TryAdd(type.FullName + "." + property.Name, 0);
                    body = Expression.Add(body, Expression.Call(typeof(ModelGraph), nameof(Read), [property.PropertyType],
                        Expression.Property(item, property)));
                }
            }
            if (!type.IsValueType)
            {
                body = Expression.Condition(Expression.Equal(item, Expression.Constant(null, type)), Expression.Constant(0L), body);
            }
            return Expression.Lambda<Func<T, long>>(body, item).Compile();
        }
    }

    private static class Rebuilder<T>
    {
        internal static readonly Func<T, T> Rebuild = Create();

        private static Func<T, T> Create()
        {
            Type type = typeof(T);
            ParameterExpression item = Expression.Parameter(type);
            Expression body;
            Type? nullable = Nullable.GetUnderlyingType(type);
            if (type.IsInterface || type == typeof(object))
            {
                body = Expression.Convert(Expression.Call(Method(nameof(FreshUnion)), Expression.Convert(item, typeof(object))), type);
            }
            else if (nullable is not null) { body = Expression.Call(Method(nameof(FreshNullable)).MakeGenericMethod(nullable), item); }
            else if (type == typeof(CborAny)) { body = Expression.Call(Method(nameof(FreshOpaque)), item); }
            else if (type == typeof(CborEncodedValue)) { body = Expression.Call(Method(nameof(FreshEmbedded)), item); }
#if !CHRYSALIS_PINNED_HARNESS
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CborList<>))
            {
                body = Expression.Call(Method(nameof(FreshCborList)).MakeGenericMethod(type.GenericTypeArguments), item);
            }
#endif
#if !CHRYSALIS_PINNED_HARNESS && !CHRYSALIS_NO_CBORMAP
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CborMap<,>))
            {
                body = Expression.Call(Method(nameof(FreshCborMap)).MakeGenericMethod(type.GenericTypeArguments), item);
            }
#endif
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                body = Expression.Call(Method(nameof(FreshList)).MakeGenericMethod(type.GenericTypeArguments), item);
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                body = Expression.Call(Method(nameof(FreshMap)).MakeGenericMethod(type.GenericTypeArguments), item);
            }
            else if (!typeof(ICborType).IsAssignableFrom(type)) { body = item; }
            else
            {
                MethodInfo factory = type.GetMethods(BindingFlags.Public | BindingFlags.Static).SingleOrDefault(method =>
                    method.Name == "Create" && method.ReturnType == type) ?? throw new NotSupportedException("No fresh factory: " + type.FullName);
                Expression[] arguments = [.. factory.GetParameters().Select(parameter =>
                {
                    string name = parameter.Name!;
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
                    PropertyInfo property = type.GetProperty(name, flags)
                        ?? (name.EndsWith("Value", StringComparison.Ordinal) ? type.GetProperty(name[..^5], flags) : null)
                        ?? throw new NotSupportedException(type.FullName + "." + name);
                    return (Expression)Expression.Call(typeof(ModelGraph), nameof(Fresh), [parameter.ParameterType], Expression.Property(item, property));
                })];
                body = Expression.Call(factory, arguments);
            }
            if (!type.IsValueType)
            {
                body = Expression.Condition(Expression.Equal(item, Expression.Constant(null, type)), Expression.Constant(null, type), body);
            }
            return Expression.Lambda<Func<T, T>>(body, item).Compile();
        }
    }
}
