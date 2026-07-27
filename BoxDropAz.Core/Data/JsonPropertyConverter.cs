using System.Text.Json;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;

namespace BoxDropAz.Core.Data;

/// <summary>
/// Stores a complex property as a JSON string. Used for embedded collections such as order notes
/// and damage lines, which are always read as part of the parent item and never queried on
/// directly, so a document-mapped shape would buy nothing.
/// </summary>
public sealed class JsonPropertyConverter<T> : IPropertyConverter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DynamoDBEntry ToEntry(object? value)
    {
        if (value is null)
        {
            return new DynamoDBNull();
        }

        var json = JsonSerializer.Serialize((T)value, Options);
        return new Primitive(json);
    }

    public object? FromEntry(DynamoDBEntry entry)
    {
        if (entry is null || entry is DynamoDBNull)
        {
            return default(T);
        }

        var json = entry.AsString();
        if (string.IsNullOrWhiteSpace(json))
        {
            return default(T);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return default(T);
        }
    }
}
