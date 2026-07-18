using System.IO;
using UnityEngine;

namespace ZWebGLFrameWork.Data
{
    public static class LocalStore
    {
        private static readonly LocalStoreBackend Backend = new LocalStoreBackend(
            Path.Combine(Application.persistentDataPath, "local_store.json"),
            new AtomicFileWriter());

        public static int Count => Backend.Count;
        public static LocalStoreWriteResult Set<T>(string key, T value) =>
            Backend.Set(key, value);
        public static T Get<T>(string key, T defaultValue = default) =>
            Backend.Get(key, defaultValue);
        public static bool TryGet<T>(string key, out T value) =>
            Backend.TryGet(key, out value);
        public static bool Has(string key) => Backend.Has(key);
        public static LocalStoreWriteResult Remove(string key) => Backend.Remove(key);
        public static LocalStoreWriteResult ClearAll() => Backend.ClearAll();
        public static void ReloadFromDisk() => Backend.ReloadFromDisk();
    }
}
