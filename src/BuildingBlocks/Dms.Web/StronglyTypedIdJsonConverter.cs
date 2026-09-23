using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dms.Web;

/// <summary>
/// Writes strongly typed ids (<c>record struct XxxId(Guid Value)</c>) as plain GUID strings and
/// reads them back, so the API never exposes <c>{"value": "…"}</c> wrappers. Applies to any value
/// type whose only constructor takes a single Guid named Value.
/// </summary>
public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => Constructor(typeToConvert) is not null;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private static ConstructorInfo? Constructor(Type type)
    {
        if (!type.IsValueType || type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("Dms", StringComparison.Ordinal) != true)
        {
            return null;
        }

        var constructors = type.GetConstructors();
        return constructors.Length == 1
            && constructors[0].GetParameters() is [{ ParameterType: var parameter, Name: "Value" }]
            && parameter == typeof(Guid)
            ? constructors[0]
            : null;
    }

    private sealed class Converter<T> : JsonConverter<T>
        where T : struct
    {
        private static readonly ConstructorInfo Create = Constructor(typeof(T))!;
        private static readonly PropertyInfo Value = typeof(T).GetProperty("Value")!;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            (T)Create.Invoke([reader.GetGuid()]);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue((Guid)Value.GetValue(value)!);
    }
}
