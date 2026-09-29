using System;
using System.Collections.Generic;
using Unity.Localization;
using Unity.Localization.Providers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Blocky.Localization
{
    /// <summary>
    /// Serves the <see cref="BlockyText.Table"/> string table of each locale, built from that language's file
    /// (<see cref="LanguageFiles"/>). It sits in the localization settings' asset provider chain like any other
    /// source, so lookups, fallback to English, Smart String formatting and the saved language choice are all
    /// Unity Localization's own. Synchronous, so a view can ask for a word and put it on a label straight away.
    /// Each table carries its own shared key data, the way Unity's file-table providers rebuild one from a
    /// single file.
    /// </summary>
    [Serializable]
    public sealed class LanguageFileProvider : IAssetProvider, ISynchronousAssetProvider
    {
        private const string AddressPrefix = BlockyText.Table + "_";

        [NonSerialized] private HashSet<ResourceTable> _built;

        public bool TryLoadAsset<T>(AssetKey key, out T asset) where T : Object
        {
            asset = null;
            if (!typeof(T).IsAssignableFrom(typeof(ResourceTable)) || !typeof(ResourceTable).IsAssignableFrom(key.Type)) return false;
            if (key.Address == null || !key.Address.StartsWith(AddressPrefix, StringComparison.Ordinal)) return false;

            var file = LanguageFiles.Find(key.Address.Substring(AddressPrefix.Length));
            if (file == null) return false;

            var table = Build(file);
            (_built ??= new HashSet<ResourceTable>()).Add(table);
            asset = table as T;
            return asset != null;
        }

        public void Release(Object asset)
        {
            if (asset is not ResourceTable table || _built == null || !_built.Remove(table)) return;

            var shared = table.SharedData;
            Destroy(table);
            if (shared != null) Destroy(shared);
        }

        private static ResourceTable Build(LanguageFile file)
        {
            var shared = ScriptableObject.CreateInstance<SharedTableData>();
            shared.hideFlags = HideFlags.HideAndDontSave;
            shared.TableCollectionName = BlockyText.Table;

            var table = ScriptableObject.CreateInstance<ResourceTable>();
            table.hideFlags = HideFlags.HideAndDontSave;
            table.name = $"{BlockyText.Table}_{file.Code}";
            table.LocaleIdentifier = file.Code;
            table.SharedData = shared;

            // Only text with a {placeholder} goes through the Smart String formatter; everything else is shown as written.
            foreach (var pair in file.Strings)
                table.AddStringEntry(pair.Key, pair.Value, pair.Value.IndexOf('{') >= 0);

            return table;
        }

        private static void Destroy(Object obj)
        {
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
