using System;

namespace Blocky.Compiler
{
    [Serializable]
    public sealed class ChoiceEntry
    {
        public string stableId;      // never localized; matches BlockParam.text for ParamKind.Choice
        public string displayNameKey;
    }
}
