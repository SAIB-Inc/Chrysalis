using System.Linq.Expressions;
using System.Reflection;

namespace Chrysalis.Cbor.Utils;

internal static class ExpressionUtils
{
    public static Delegate CreateGetter(Type type, PropertyInfo prop)
    {
        var instance = Expression.Parameter(type, "instance");
        return Expression.Lambda(Expression.Property(instance, prop), instance).Compile();
    }

    public static Delegate CreateSetter(Type type, PropertyInfo prop)
    {
        var instance = Expression.Parameter(type, "instance");
        var value = Expression.Parameter(prop.PropertyType, "value");
        return Expression.Lambda(
            Expression.Assign(Expression.Property(instance, prop), value),
            instance, value).Compile();
    }

    public static Delegate CreateActivator(Type type)
    {
        // Skip activator creation for generic parameters or generic type definitions
        if (type.IsGenericParameter || type.IsGenericTypeDefinition)
        {
            return CreateDummyActivator(type);
        }

        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (!ctors.Any())
        {
            return CreateDummyActivator(type);
        }

        var ctor = ctors.OrderBy(c => c.GetParameters().Length).First();
        var parameters = ctor.GetParameters();

        if (parameters.Length == 0)
        {
            var newExp = Expression.New(ctor);
            var lambda = Expression.Lambda(newExp);
            return lambda.Compile();
        }
        else
        {
            var parameterExpressions = parameters.Select(p =>
                Expression.Parameter(p.ParameterType, p.Name)).ToArray();

            var newExp = Expression.New(ctor, parameterExpressions);
            var lambda = Expression.Lambda(newExp, parameterExpressions);
            return lambda.Compile();
        }
    }

    public static Delegate CreateDummyActivator(Type type)
    {
        // Create a dummy activator that returns null or throws an appropriate exception
        var returnType = typeof(Func<>).MakeGenericType(type);
        return Delegate.CreateDelegate(returnType,
            typeof(ExpressionUtils).GetMethod(nameof(DummyActivator),
                BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type));
    }

    private static T? DummyActivator<T>() => default;
}