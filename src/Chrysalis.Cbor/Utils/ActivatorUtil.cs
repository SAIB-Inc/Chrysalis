using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Utils
{
    /// <summary>
    /// Provides utility methods for creating instances of types using reflection,
    /// tailored for CBOR serialization and deserialization.
    /// </summary>
    public static class ActivatorUtil
    {
        /// <summary>
        /// Creates an instance of the specified type using the provided value and CBOR options.
        /// Selects the constructor with the most parameters and populates its arguments.
        /// </summary>
        /// <param name="targetType">The type to instantiate.</param>
        /// <param name="value">The data used to populate constructor arguments.</param>
        /// <param name="options">CBOR options containing mapping configurations.</param>
        /// <returns>An instance of the target type.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="targetType"/> or <paramref name="options"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if no constructor is found or argument transformation fails.</exception>
        public static object CreateInstance(Type targetType, object? value, CborOptions options)
        {
            if (targetType == null) throw new ArgumentNullException(nameof(targetType));
            if (options == null) throw new ArgumentNullException(nameof(options));

            ConstructorInfo? constructor = targetType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"No constructor found for type '{targetType.FullName}'.");

            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters.Length == 0)
            {
                return Activator.CreateInstance(targetType)
                    ?? throw new InvalidOperationException($"Failed to create instance of '{targetType.FullName}'.");
            }

            object?[] arguments = PrepareArguments(parameters, value, options);
            return constructor.Invoke(arguments);
        }

        /// <summary>
        /// Prepares arguments for a constructor based on the provided value and options.
        /// </summary>
        private static object?[] PrepareArguments(ParameterInfo[] parameters, object? value, CborOptions options)
        {
            object?[] arguments = new object?[parameters.Length];

            if (parameters.Length == 1)
            {
                arguments[0] = TransformValue(value, parameters[0].ParameterType);
                return arguments;
            }

            switch (value)
            {
                case IDictionary dictionary when options.IndexPropertyMapping != null:
                    MapByIndex(dictionary, options.IndexPropertyMapping, parameters, arguments);
                    break;

                case IDictionary dictionary when options.NamedPropertyMapping != null:
                    MapByName(dictionary, parameters, arguments);
                    break;

                case IEnumerable sequence:
                    MapBySequence(sequence, parameters, arguments);
                    break;

                    // Unmapped cases leave arguments as null, relying on parameter defaults or nullability
            }

            return arguments;
        }

        /// <summary>
        /// Maps dictionary values to arguments using an index-based mapping.
        /// </summary>
        private static void MapByIndex(
            IDictionary dictionary,
            IReadOnlyDictionary<int, Type> mapping,
            ParameterInfo[] parameters,
            object?[] arguments)
        {
            int index = 0;
            foreach (int key in mapping.Keys.OrderBy(k => k))
            {
                if (index >= arguments.Length) break;
                object? matchingKey = dictionary.Keys
                    .OfType<object>()
                    .FirstOrDefault(k => TryConvertKey(k, out int intKey) && intKey == key);
                if (matchingKey != null)
                {
                    arguments[index] = TransformValue(dictionary[matchingKey], parameters[index].ParameterType);
                }
                index++;
            }
        }

        /// <summary>
        /// Maps dictionary values to arguments using parameter names from attributes.
        /// </summary>
        private static void MapByName(IDictionary dictionary, ParameterInfo[] parameters, object?[] arguments)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                string? propertyName = parameters[i].GetCustomAttribute<CborPropertyAttribute>()?.Name;
                if (propertyName != null && dictionary.Contains(propertyName))
                {
                    arguments[i] = TransformValue(dictionary[propertyName], parameters[i].ParameterType);
                }
            }
        }

        /// <summary>
        /// Maps sequence items to arguments in order.
        /// </summary>
        private static void MapBySequence(IEnumerable sequence, ParameterInfo[] parameters, object?[] arguments)
        {
            int index = 0;
            foreach (object? item in sequence)
            {
                if (index >= arguments.Length) break;
                arguments[index] = TransformValue(item, parameters[index].ParameterType);
                index++;
            }
        }

        /// <summary>
        /// Transforms a value to match the target type, handling various conversion scenarios.
        /// </summary>
        private static object? TransformValue(object? value, Type targetType)
        {
            if (value == null) return null;

            // Handle case where value is a collection but target type is not
            if (!typeof(IEnumerable).IsAssignableFrom(targetType) && value is IEnumerable enumerable)
            {
                if (enumerable is IEnumerable<object> items)
                {
                    object? firstItem = items.FirstOrDefault();
                    if (firstItem != null && targetType.IsAssignableFrom(firstItem.GetType()))
                    {
                        return firstItem;
                    }
                }
            }

            Type sourceType = value.GetType();
            if (targetType.IsAssignableFrom(sourceType)) return value;

            // Try constructor with single parameter of value's type
            ConstructorInfo? constructor = targetType.GetConstructor([sourceType]);
            if (constructor != null)
            {
                return constructor.Invoke([value]);
            }

            // Handle generic collections
            if (IsCollectionType(targetType) && targetType.IsGenericType)
            {
                return ConvertToCollection(value, targetType);
            }

            throw new InvalidOperationException(
                $"Cannot transform value of type '{sourceType.FullName}' to target type '{targetType.FullName}'.");
        }

        /// <summary>
        /// Converts a value to a generic collection type (List<> or Dictionary<,>).
        /// </summary>
        private static object ConvertToCollection(object value, Type targetType)
        {
            Type genericType = targetType.GetGenericTypeDefinition();
            Type[] typeArguments = targetType.GetGenericArguments();

            if (genericType == typeof(List<>))
            {
                Type listType = typeof(List<>).MakeGenericType(typeArguments[0]);
                IList list = (IList)Activator.CreateInstance(listType)!;
                if (value is IEnumerable items)
                {
                    foreach (object? item in items)
                    {
                        list.Add(item);
                    }
                }
                return list;
            }

            if (genericType == typeof(Dictionary<,>))
            {
                Type dictType = typeof(Dictionary<,>).MakeGenericType(typeArguments);
                IDictionary dictionary = (IDictionary)Activator.CreateInstance(dictType)!;
                if (value is IDictionary source)
                {
                    foreach (DictionaryEntry entry in source)
                    {
                        dictionary.Add(entry.Key, entry.Value);
                    }
                }
                return dictionary;
            }

            throw new InvalidOperationException($"Unsupported collection type: '{targetType.FullName}'.");
        }

        /// <summary>
        /// Determines if a type is a supported generic collection (List<> or Dictionary<,>).
        /// </summary>
        private static bool IsCollectionType(Type type) =>
            type.IsGenericType && (
                type.GetGenericTypeDefinition() == typeof(List<>) ||
                type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            );

        /// <summary>
        /// Attempts to convert a key to an integer.
        /// </summary>
        private static bool TryConvertKey(object key, out int result)
        {
            if (key is int intKey)
            {
                result = intKey;
                return true;
            }

            try
            {
                result = Convert.ToInt32(key);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }
    }
}