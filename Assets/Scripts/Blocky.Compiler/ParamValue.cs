using Blocky.Data;

namespace Blocky.Compiler
{
    /// <summary>A resolved, flat param slot in <see cref="CompiledProgram.ParamTable"/> — no key string at runtime.</summary>
    public readonly struct ParamValue
    {
        public readonly ParamKind Kind;
        public readonly float Number;
        public readonly string Text;
        public readonly bool Boolean;
        public readonly int ChoiceIndex; // index into the ParamSpec's choices array, -1 if not a Choice

        public ParamValue(ParamKind kind, float number, string text, bool boolean, int choiceIndex)
        {
            Kind = kind;
            Number = number;
            Text = text;
            Boolean = boolean;
            ChoiceIndex = choiceIndex;
        }
    }
}
