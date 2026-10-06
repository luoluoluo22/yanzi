using System.Text.Json;

namespace OpenQuickHost;

/// <summary>MVP JSON Schema subset; validation runs before provider side effects.</summary>
public static class YanziCapabilitySchema
{
    public static JsonElement Any { get; } = Parse("{}");
    public static JsonElement EmptyObject { get; } = Parse("{\"type\":\"object\",\"additionalProperties\":false}");
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static void ValidateDefinition(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) throw new ArgumentException("Schema 必须是对象");
        foreach (var keyword in schema.EnumerateObject())
        {
            var value = keyword.Value;
            switch (keyword.Name)
            {
                case "type":
                    if (value.ValueKind != JsonValueKind.String ||
                        !new[] { "object", "array", "string", "number", "integer", "boolean", "null" }.Contains(value.GetString()))
                        throw new ArgumentException("Schema type 必须为支持的单一类型");
                    break;
                case "properties":
                    if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Schema properties 必须是对象");
                    foreach (var property in value.EnumerateObject()) ValidateDefinition(property.Value);
                    break;
                case "items": ValidateDefinition(value); break;
                case "required":
                    if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                        throw new ArgumentException("Schema required 必须是字符串数组");
                    break;
                case "enum":
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                        throw new ArgumentException("Schema enum 必须是非空数组");
                    break;
                case "additionalProperties":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new ArgumentException("Schema additionalProperties 必须是布尔值");
                    break;
                case "minimum": case "maximum":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out _))
                        throw new ArgumentException("Schema 数值边界必须为有限数值");
                    break;
                case "minLength":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var min) || min < 0)
                        throw new ArgumentException("Schema minLength 必须是非负整数");
                    break;
                case "title": case "description": case "default": case "examples": case "$schema":
                    break; // Metadata annotations do not affect validation.
                default: throw new ArgumentException($"MVP 暂不支持 Schema 关键字：{keyword.Name}");
            }
        }
    }

    public static void Validate(JsonElement schema, JsonElement value, string path = "$")
    {
        if (schema.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("能力 Schema 必须是 JSON 对象");
        if (schema.TryGetProperty("type", out var type))
        {
            var valid = type.GetString() switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "number" => value.ValueKind == JsonValueKind.Number,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && decimal.Truncate(number) == number,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => value.ValueKind == JsonValueKind.Null,
                _ => throw new ArgumentException($"不支持的 Schema 类型：{type}")
            };
            if (!valid) throw new ArgumentException($"{path} 必须为 {type.GetString()}");
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var numeric))
        {
            if (schema.TryGetProperty("minimum", out var minimum) && numeric < minimum.GetDecimal())
                throw new ArgumentException($"{path} 小于允许下限");
            if (schema.TryGetProperty("maximum", out var maximum) && numeric > maximum.GetDecimal())
                throw new ArgumentException($"{path} 大于允许上限");
        }
        if (schema.TryGetProperty("enum", out var choices) &&
            !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value)))
            throw new ArgumentException($"{path} 不在允许值中");
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var key in required.EnumerateArray())
                    if (!value.TryGetProperty(key.GetString()!, out _))
                        throw new ArgumentException($"{path}.{key.GetString()} 是必填参数");
            var hasProperties = schema.TryGetProperty("properties", out var properties);
            foreach (var property in value.EnumerateObject())
            {
                if (hasProperties && properties.TryGetProperty(property.Name, out var child))
                    Validate(child, property.Value, $"{path}.{property.Name}");
                else if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False)
                    throw new ArgumentException($"{path}.{property.Name} 是未声明参数");
            }
        }
        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray()) Validate(items, item, $"{path}[{index++}]");
        }
        if (value.ValueKind == JsonValueKind.String && schema.TryGetProperty("minLength", out var minLength)
            && value.GetString()!.Length < minLength.GetInt32())
            throw new ArgumentException($"{path} 字符数不足");
    }
}
