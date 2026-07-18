using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    public enum LocalStoreWriteCode
    {
        Success,
        NotFound,
        WriteFailed,
    }

    public readonly struct LocalStoreWriteResult
    {
        public LocalStoreWriteCode Code { get; }
        public string Error { get; }
        public bool Succeeded => Code == LocalStoreWriteCode.Success;

        public LocalStoreWriteResult(LocalStoreWriteCode code, string error = null)
        {
            Code = code;
            Error = error;
        }
    }

    public sealed class LocalStoreBackend
    {
        [Serializable]
        private sealed class ValueBox<T>
        {
            public T Value;
        }

        [Serializable]
        private sealed class StoreEntry
        {
            public string TypeName;
            public string JsonData;
        }

        [Serializable]
        private sealed class StoreFileData
        {
            public List<string> Keys = new List<string>();
            public List<string> TypeNames = new List<string>();
            public List<string> JsonValues = new List<string>();

            public static StoreFileData FromDictionary(
                IReadOnlyDictionary<string, StoreEntry> entries)
            {
                var data = new StoreFileData();
                foreach (KeyValuePair<string, StoreEntry> pair in entries)
                {
                    data.Keys.Add(pair.Key);
                    data.TypeNames.Add(pair.Value.TypeName);
                    data.JsonValues.Add(pair.Value.JsonData);
                }
                return data;
            }

            public Dictionary<string, StoreEntry> ToDictionary()
            {
                var result = new Dictionary<string, StoreEntry>(Keys.Count);
                int count = Math.Min(Keys.Count, Math.Min(TypeNames.Count, JsonValues.Count));
                for (int index = 0; index < count; index++)
                {
                    result[Keys[index]] = new StoreEntry
                    {
                        TypeName = TypeNames[index],
                        JsonData = JsonValues[index],
                    };
                }
                return result;
            }
        }

        private readonly string _filePath;
        private readonly IAtomicTextWriter _writer;
        private Dictionary<string, StoreEntry> _entries;
        private bool _loaded;

        public LocalStoreBackend(string filePath, IAtomicTextWriter writer)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? throw new ArgumentException("Path is required", nameof(filePath))
                : filePath;
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public int Count
        {
            get
            {
                EnsureLoaded();
                return _entries.Count;
            }
        }

        public LocalStoreWriteResult Set<T>(string key, T value)
        {
            ValidateKey(key);
            EnsureLoaded();
            if (value == null) return Remove(key);

            bool hadPrevious = _entries.TryGetValue(key, out StoreEntry previous);
            _entries[key] = new StoreEntry
            {
                TypeName = typeof(T).AssemblyQualifiedName,
                JsonData = JsonUtility.ToJson(new ValueBox<T> { Value = value }),
            };
            FileWriteResult write = Flush();
            if (write.Succeeded)
                return new LocalStoreWriteResult(LocalStoreWriteCode.Success);
            if (hadPrevious) _entries[key] = previous;
            else _entries.Remove(key);
            return new LocalStoreWriteResult(LocalStoreWriteCode.WriteFailed, write.Error);
        }

        public T Get<T>(string key, T defaultValue = default) =>
            TryGet(key, out T value) ? value : defaultValue;

        public bool TryGet<T>(string key, out T value)
        {
            ValidateKey(key);
            EnsureLoaded();
            value = default;
            if (!_entries.TryGetValue(key, out StoreEntry entry) ||
                entry.TypeName != typeof(T).AssemblyQualifiedName)
                return false;
            try
            {
                ValueBox<T> box = JsonUtility.FromJson<ValueBox<T>>(entry.JsonData);
                if (box == null) return false;
                value = box.Value;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LocalStore] Read failed for {key}: {exception.Message}");
                return false;
            }
        }

        public bool Has(string key)
        {
            ValidateKey(key);
            EnsureLoaded();
            return _entries.ContainsKey(key);
        }

        public LocalStoreWriteResult Remove(string key)
        {
            ValidateKey(key);
            EnsureLoaded();
            if (!_entries.TryGetValue(key, out StoreEntry previous))
                return new LocalStoreWriteResult(LocalStoreWriteCode.NotFound);
            _entries.Remove(key);
            FileWriteResult write = Flush();
            if (write.Succeeded)
                return new LocalStoreWriteResult(LocalStoreWriteCode.Success);
            _entries[key] = previous;
            return new LocalStoreWriteResult(LocalStoreWriteCode.WriteFailed, write.Error);
        }

        public LocalStoreWriteResult ClearAll()
        {
            EnsureLoaded();
            var previous = new Dictionary<string, StoreEntry>(_entries);
            _entries.Clear();
            FileWriteResult write = Flush();
            if (write.Succeeded)
                return new LocalStoreWriteResult(LocalStoreWriteCode.Success);
            _entries = previous;
            return new LocalStoreWriteResult(LocalStoreWriteCode.WriteFailed, write.Error);
        }

        public void ReloadFromDisk()
        {
            _loaded = false;
            _entries = null;
            EnsureLoaded();
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(_filePath))
                {
                    _entries = new Dictionary<string, StoreEntry>();
                    return;
                }
                StoreFileData data = JsonUtility.FromJson<StoreFileData>(
                    File.ReadAllText(_filePath));
                _entries = data?.ToDictionary() ?? new Dictionary<string, StoreEntry>();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[LocalStore] Load failed: {exception.Message}");
                _entries = new Dictionary<string, StoreEntry>();
            }
        }

        private FileWriteResult Flush()
        {
            StoreFileData data = StoreFileData.FromDictionary(_entries);
            return _writer.Write(_filePath, JsonUtility.ToJson(data, false));
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Key is required", nameof(key));
        }
    }
}
