using System;

namespace Blocky.Data
{
    /// <summary>
    /// A single parameter value as a tagged union. Only the field matching <see cref="kind"/> is meaningful;
    /// the others are serialized away by <c>BlockParamConverter</c>. See TDD §4.1.
    /// </summary>
    [Serializable]
    public sealed class BlockParam
    {
        public string key;
        public ParamKind kind;

        public float number;      // Number
        public string text;       // Text, ObjectRef (UID), Choice (stable id — never a localized display string)
        public bool boolean;      // Bool
        public BlockNode reporter; // Reporter — unused in v1, reserved so the schema never has to change for expression blocks
    }
}
