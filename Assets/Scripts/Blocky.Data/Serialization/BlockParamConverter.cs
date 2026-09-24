using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Blocky.Data.Serialization
{
    /// <summary>Writes/reads only the union field that matches <see cref="BlockParam.kind"/> (TDD §5.1).</summary>
    public sealed class BlockParamConverter : JsonConverter<BlockParam>
    {
        public override void WriteJson(JsonWriter writer, BlockParam value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("key");
            writer.WriteValue(value.key);
            writer.WritePropertyName("kind");
            writer.WriteValue(value.kind.ToString());

            switch (value.kind)
            {
                case ParamKind.Number:
                    writer.WritePropertyName("number");
                    writer.WriteValue(value.number);
                    break;
                case ParamKind.Text:
                case ParamKind.ObjectRef:
                case ParamKind.Choice:
                    writer.WritePropertyName("text");
                    writer.WriteValue(value.text);
                    break;
                case ParamKind.Bool:
                    writer.WritePropertyName("boolean");
                    writer.WriteValue(value.boolean);
                    break;
                case ParamKind.Reporter:
                    break; // a condition slot has no literal of its own; the block in it is written below
            }

            // Any input can hold a block: a condition in a hexagonal hole, or a reporter dropped on a value oval.
            // The literal above is kept alongside it, so pulling the block back out restores the number that was
            // typed before — the same thing Scratch does with a value a reporter is covering.
            if (value.reporter != null)
            {
                writer.WritePropertyName("reporter");
                serializer.Serialize(writer, value.reporter);
            }

            writer.WriteEndObject();
        }

        public override BlockParam ReadJson(JsonReader reader, Type objectType, BlockParam existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            var param = new BlockParam
            {
                key = (string)obj["key"],
                kind = Enum.Parse<ParamKind>((string)obj["kind"])
            };

            switch (param.kind)
            {
                case ParamKind.Number:
                    param.number = obj["number"]?.Value<float>() ?? 0f;
                    break;
                case ParamKind.Text:
                case ParamKind.ObjectRef:
                case ParamKind.Choice:
                    param.text = (string)obj["text"];
                    break;
                case ParamKind.Bool:
                    param.boolean = obj["boolean"]?.Value<bool>() ?? false;
                    break;
                case ParamKind.Reporter:
                    break; // nothing but the block, read below
            }

            if (obj["reporter"] != null)
                param.reporter = obj["reporter"].ToObject<BlockNode>(serializer);

            return param;
        }
    }
}
