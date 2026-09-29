using System;
using Blocky.Data;
using Blocky.Localization;

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

        /// <summary>The label a player sees on the input, in their language: the <c>param.&lt;key&gt;</c> string, else the key.</summary>
        public string DisplayName => BlockyText.Get("param." + key, key);

        /// <summary>
        /// What a player sees for the choice <paramref name="stableId"/>: the <c>choice.&lt;key&gt;.&lt;stableId&gt;</c> string,
        /// else the entry's own name, else the id. Only ever shown — the program keeps storing the id.
        /// </summary>
        public string ChoiceDisplayName(string stableId)
        {
            var entry = Array.Find(choices, c => c.stableId == stableId);
            var fallback = entry != null && !string.IsNullOrEmpty(entry.displayNameKey) ? entry.displayNameKey : stableId;
            return BlockyText.Get($"choice.{key}.{stableId}", fallback);
        }
    }
}
