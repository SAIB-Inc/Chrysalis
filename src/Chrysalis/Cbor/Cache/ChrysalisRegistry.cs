using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cache;

public class ChrysalisRegistry
{
    public ConcurrentDictionary<Type, ICborConverter> Converters = [];
    public ConcurrentDictionary<Type, CborOptions> Options = [];
    public ConcurrentDictionary<Type, Delegate> Activators = [];
    public ConcurrentDictionary<Type, PropertyInfo[]> Properties = [];
    public ConcurrentDictionary<(Type Type, string Name), Delegate> Getters = [];
    public ConcurrentDictionary<(Type Type, string Name), Delegate> Setters = [];
    public ConcurrentDictionary<Type, ConstructorInfo[]> Constructors = [];
    public ConcurrentDictionary<(Type Type, string Name), MethodInfo> Methods = [];

    public void InitializeRegistry()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        RegisterConverters(assembly);
        RegisterCborOptions(assembly);
        RegisterActivators(assembly);
        RegisterGetters();
        RegisterSetters();
        RegisterPropertyInfo();
        RegisterConstructors();
        RegisterMethods();
    }

    private void RegisterConverters(Assembly assembly)
    {
        TypeInfo[] converterTypes = [..assembly.GetTypes()
            .Where(t => t.IsClass
                && !t.IsAbstract
                && typeof(ICborConverter).IsAssignableFrom(t))
            .Select(t => t.GetTypeInfo())];
        Parallel.ForEach(converterTypes, type =>
        {
            ICborConverter converter = (ICborConverter)Activator.CreateInstance(type.AsType())!;
            Converters.TryAdd(type.AsType(), converter);
        });
    }

    private void RegisterCborOptions(Assembly assembly)
    {
        Type[] types = [.. assembly.GetTypes()];
        Parallel.ForEach(types, type =>
        {
            CborOptions? options = GetCborOptionsWithInheritance(type);
            if (options != null)
            {
                Options.TryAdd(type, options);
            }
        });
    }

    private static CborOptions? GetCborOptionsWithInheritance(Type type)
    {
        int? index = null;
        Type? converterType = null;
        bool? isDefinite = null;
        int? size = null;
        int? tag = null;
        Type? currentType = type;

        while (currentType != null && currentType != typeof(object))
        {
            CborConverterAttribute? converterAttr = currentType.GetCustomAttribute<CborConverterAttribute>();
            if (converterAttr != null && converterType == null)
            {
                converterType = converterAttr.ConverterType;
            }
            CborIndexAttribute? indexAttr = currentType.GetCustomAttribute<CborIndexAttribute>();
            if (indexAttr != null && index == null)
            {
                index = indexAttr.Index;
            }
            CborTagAttribute? tagAttr = currentType.GetCustomAttribute<CborTagAttribute>();
            if (tagAttr != null && tag == null)
            {
                tag = tagAttr.Tag;
            }
            CborSizeAttribute? sizeAttr = currentType.GetCustomAttribute<CborSizeAttribute>();
            if (sizeAttr != null && size == null)
            {
                size = sizeAttr.Size;
            }
            if (isDefinite == null)
            {
                if (currentType.GetCustomAttribute<CborDefiniteAttribute>() != null)
                {
                    isDefinite = true;
                }
                else if (currentType.GetCustomAttribute<CborIndefiniteAttribute>() != null)
                {
                    isDefinite = false;
                }
            }
            currentType = currentType.BaseType;
        }
        if (index != null || converterType != null || isDefinite != null || size != null || tag != null)
        {
            return new CborOptions(
                Index: index,
                ConverterType: converterType,
                IsDefinite: isDefinite,
                Size: size,
                Tag: tag
            );
        }
        return null;
    }

    private void RegisterActivators(Assembly assembly)
    {
        Type[] cborTypes = [.. assembly.GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                !type.IsGenericTypeDefinition &&
                !type.ContainsGenericParameters
            )];

        Parallel.ForEach(cborTypes, type =>
        {
            ConstructorInfo[] constructors = type.GetConstructors();
            if (constructors.Length == 0)
            {
                return;
            }
            ConstructorInfo ctor = constructors.First();
            ParameterInfo[] ctorParams = ctor.GetParameters();
            ParameterExpression[] parameters = [.. ctorParams.Select(p => Expression.Parameter(p.ParameterType, p.Name))];
            NewExpression newExpr = Expression.New(ctor, parameters);
            LambdaExpression lambda = Expression.Lambda(newExpr, parameters);
            Delegate compiled = lambda.Compile();

            Activators.TryAdd(type, compiled);
        });
    }

    private void RegisterGetters()
    {
        Type[] cborTypes = [.. Options.Keys.Where(t => !t.IsGenericTypeDefinition && !t.ContainsGenericParameters)];
        Parallel.ForEach(cborTypes, type =>
        {
            PropertyInfo[] properties = type.GetProperties();
            foreach (PropertyInfo prop in properties)
            {
                ParameterExpression instance = Expression.Parameter(type, "instance");
                MemberExpression property = Expression.Property(instance, prop);
                LambdaExpression lambda = Expression.Lambda(property, instance);
                Delegate compiled = lambda.Compile();
                Getters.TryAdd((type, prop.Name), compiled);
            }
        });
    }

    private void RegisterSetters()
    {
        Type[] cborTypes = [.. Options.Keys.Where(t => !t.IsGenericTypeDefinition && !t.ContainsGenericParameters)];
        Parallel.ForEach(cborTypes, type =>
        {
            IEnumerable<PropertyInfo> properties = type.GetProperties().Where(p => p.CanWrite);
            foreach (PropertyInfo prop in properties)
            {
                ParameterExpression instance = Expression.Parameter(type, "instance");
                ParameterExpression value = Expression.Parameter(prop.PropertyType, "value");
                BinaryExpression assign = Expression.Assign(Expression.Property(instance, prop), value);
                LambdaExpression lambda = Expression.Lambda(assign, instance, value);
                Delegate compiled = lambda.Compile();
                Setters.TryAdd((type, prop.Name), compiled);
            }
        });
    }

    private void RegisterPropertyInfo()
    {
        Type[] cborTypes = [.. Options.Keys];

        Parallel.ForEach(cborTypes, type =>
        {
            PropertyInfo[] properties = [.. type
                .GetProperties()
                .OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()?.Index ?? int.MaxValue)
            ];
            Properties.TryAdd(type, properties);
        });
    }

    private void RegisterConstructors()
    {
        Type[] cborTypes = [.. Options.Keys];

        Parallel.ForEach(cborTypes, type =>
        {
            ConstructorInfo[] ctors = type.GetConstructors();
            Constructors.TryAdd(type, ctors);
        });
    }

    private void RegisterMethods()
    {
        Type[] cborTypes = [.. Options.Keys];

        Parallel.ForEach(cborTypes, type =>
        {
            IEnumerable<MethodInfo> methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType == type);

            foreach (MethodInfo? method in methods)
            {
                Methods.TryAdd((type, method.Name), method);
            }
        });
    }
}