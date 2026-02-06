using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ClassicUO.Utility.Collections;

public class FastUlongLookupTable<T>
{
    // All, or close to all keys in the short-biased flows are expected to land in a 15 bit range or so.
    // By trading a tiny bit of performance for the outliers, we can significantly reduce allocation size.
    // Since this map is mostly used for nullable integer (expected to be 8-byte), this is a difference of 256KiB per-instance (5) so a total of 1.25MiB saved.
    private const ushort SHORT_BIAS_FAST_LOOKUP_CAPACITY = 16384;
    private const ushort LONG_BIAS_FAST_LOOKUP_CAPACITY = 16;

    private const ushort SHORT_BIAS_DICT_INIT_CAPACITY = 16;
    private const ushort LONG_BIAS_DICT_INIT_CAPACITY = 4096;

    private readonly bool _shortBiased;
    private ushort _fastLookupCount;
    private readonly T[] _fastLookup;
    private readonly Dictionary<ulong, T> _slowDict;

    private ulong maxFastKey, maxDictKey ;
    private ulong minFastKey = ulong.MaxValue, minDictKey = ulong.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsFastFlowSized(ulong key) =>
        key < (_shortBiased ? SHORT_BIAS_FAST_LOOKUP_CAPACITY : LONG_BIAS_FAST_LOOKUP_CAPACITY);

    public int Count => _fastLookupCount + _slowDict.Count;

    public FastUlongLookupTable(bool shortBiased)
    {
        _shortBiased = shortBiased;

        if (shortBiased)
        {
            _fastLookup = new T[SHORT_BIAS_FAST_LOOKUP_CAPACITY];
            _slowDict = new Dictionary<ulong, T>(SHORT_BIAS_DICT_INIT_CAPACITY);
        }
        else
        {
            _fastLookup = new T[LONG_BIAS_FAST_LOOKUP_CAPACITY];
            _slowDict = new Dictionary<ulong, T>(LONG_BIAS_DICT_INIT_CAPACITY);
        }
    }

    public T Get(ulong key)
    {
        if (IsFastFlowSized(key))
            return _fastLookup[(int)key];

        _slowDict.TryGetValue(key, out T value);
        return value;
    }

    public void Set(ulong key, T value)
    {
        if (IsFastFlowSized(key))
        {
            _fastLookup[(int)key] = value;
            ++_fastLookupCount;
            if (key < minFastKey)
                minFastKey = key;
            if (key > maxFastKey)
                maxFastKey = key;
        }
        else
        {
            _slowDict[key] = value;
            if (key < minDictKey)
                minDictKey = key;
            if (key > maxDictKey)
                maxDictKey = key;
        }
    }

    public void Remove(ulong key)
    {
        if (IsFastFlowSized(key))
        {
            _fastLookup[(int)key] = default;
            --_fastLookupCount;
        }
        else
            _slowDict.Remove(key);
    }

    public void Clear()
    {
        Array.Clear(_fastLookup);
        _slowDict.Clear();
    }
}
