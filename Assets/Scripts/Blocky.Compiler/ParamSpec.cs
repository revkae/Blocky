using System;
using Blocky.Data;

namespace Blocky.Compiler
{
    [Serializable]
    public sealed class ParamSpec
    {
        public string key;
        public ParamKind kind;
        public string defaultText;
        public float defaultNumber;
        public float min;
        public float max;
        public ChoiceEntry[] choices = Array.Empty<ChoiceEntry>();
    }
}
