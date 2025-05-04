namespace Maple2.Server.Launcher.Utils;

public static class DictionaryUtils
{
    public static TValue GetValueOrDefault<TKey,TValue>(this IDictionary<TKey,TValue> dict, TKey key, TValue defaultValue = default!)
        => dict.TryGetValue(key, out TValue? value) ? value : defaultValue;
}