using System;

namespace Blocky.Data
{
    /// <summary>
    /// Immutable-style insert/remove for the plain arrays the data model uses (TDD §4: no MonoBehaviour,
    /// no UnityEngine.Object — and no List&lt;T&gt; either, to keep serialized shape exactly as documented).
    /// </summary>
    internal static class ArrayUtil
    {
        public static T[] Insert<T>(T[] array, int index, T item)
        {
            var result = new T[array.Length + 1];
            Array.Copy(array, 0, result, 0, index);
            result[index] = item;
            Array.Copy(array, index, result, index + 1, array.Length - index);
            return result;
        }

        public static T[] InsertRange<T>(T[] array, int index, T[] items)
        {
            var result = new T[array.Length + items.Length];
            Array.Copy(array, 0, result, 0, index);
            Array.Copy(items, 0, result, index, items.Length);
            Array.Copy(array, index, result, index + items.Length, array.Length - index);
            return result;
        }

        public static T[] Slice<T>(T[] array, int start, int length)
        {
            var result = new T[length];
            Array.Copy(array, start, result, 0, length);
            return result;
        }

        public static T[] RemoveAt<T>(T[] array, int index, out T removed)
        {
            removed = array[index];
            var result = new T[array.Length - 1];
            Array.Copy(array, 0, result, 0, index);
            Array.Copy(array, index + 1, result, index, array.Length - index - 1);
            return result;
        }
    }
}
