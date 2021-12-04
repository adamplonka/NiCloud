using System.Collections.Generic;

namespace NiCloud;

public static class DictionaryExtensions
{
    public static TValue GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) =>
        dict.TryGetValue(key, out var value) ? value : default;
}
