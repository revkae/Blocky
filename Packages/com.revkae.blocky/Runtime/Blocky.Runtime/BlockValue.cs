using System.Globalization;

namespace Blocky.Runtime
{
    /// <summary>
    /// What a reporter block answers. Scratch has no types a learner can see: <c>join</c> happily takes the result
    /// of <c>+</c>, and <c>move</c> takes the result of <c>join</c> if it looks like a number. So a value carries
    /// what it actually is and converts on demand, rather than refusing. A struct with one float, one string and
    /// one bool — no boxing, nothing allocated unless a conversion has to build a string.
    /// </summary>
    public readonly struct BlockValue
    {
        public enum ValueKind : byte
        {
            Number,
            Text,
            Boolean
        }

        public readonly ValueKind Kind;
        private readonly float _number;
        private readonly string _text;
        private readonly bool _boolean;

        private BlockValue(ValueKind kind, float number, string text, bool boolean)
        {
            Kind = kind;
            _number = number;
            _text = text;
            _boolean = boolean;
        }

        public static BlockValue Number(float value) => new(ValueKind.Number, value, null, false);
        public static BlockValue Text(string value) => new(ValueKind.Text, 0f, value, false);
        public static BlockValue Boolean(bool value) => new(ValueKind.Boolean, 0f, null, value);

        /// <summary>Empty text, which reads as 0 and as false — what an empty slot answers.</summary>
        public static BlockValue Empty => Text(string.Empty);

        /// <summary>Text that is not a number reads as 0, as it does in Scratch; true is 1.</summary>
        public float AsNumber() => Kind switch
        {
            ValueKind.Number => _number,
            ValueKind.Boolean => _boolean ? 1f : 0f,
            _ => float.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f
        };

        /// <summary>
        /// Numbers print without a trailing <c>.0</c> and always with a <c>.</c> for a decimal point, whatever the
        /// player's locale — a program that reads "1.5" on one machine must not read "1,5" on another.
        /// </summary>
        public string AsText() => Kind switch
        {
            ValueKind.Text => _text ?? string.Empty,
            ValueKind.Boolean => _boolean ? "true" : "false",
            _ => _number.ToString("0.###############", CultureInfo.InvariantCulture)
        };

        /// <summary>Anything but 0, empty text and the words "false"/"0" is true — Scratch's own rule.</summary>
        public bool AsBool() => Kind switch
        {
            ValueKind.Boolean => _boolean,
            ValueKind.Number => _number != 0f,
            _ => !string.IsNullOrEmpty(_text)
                 && !string.Equals(_text, "false", System.StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(_text, "0", System.StringComparison.Ordinal)
        };

        /// <summary>True when this value and <paramref name="other"/> can both be read as numbers — how comparisons decide.</summary>
        public bool IsNumericWith(in BlockValue other) => LooksNumeric(this) && LooksNumeric(other);

        private static bool LooksNumeric(in BlockValue value) =>
            value.Kind == ValueKind.Number ||
            (value.Kind == ValueKind.Text && float.TryParse(value._text, NumberStyles.Float, CultureInfo.InvariantCulture, out _));

        public override string ToString() => AsText();
    }
}
