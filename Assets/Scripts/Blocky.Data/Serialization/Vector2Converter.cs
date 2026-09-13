using System;
using Newtonsoft.Json;
using UnityEngine;

namespace Blocky.Data.Serialization
{
    /// <summary>
    /// Unity's <see cref="Vector2"/> exposes a self-referencing <c>normalized</c> property that trips
    /// Newtonsoft's default reflection-based serialization. Write only x/y.
    /// </summary>
    public sealed class Vector2Converter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
            var x = obj["x"] != null ? (float)obj["x"] : 0f;
            var y = obj["y"] != null ? (float)obj["y"] : 0f;
            return new Vector2(x, y);
        }
    }
}
