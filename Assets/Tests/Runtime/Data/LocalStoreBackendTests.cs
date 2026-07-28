using System;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class LocalStoreBackendTests : RuntimeTestBase
    {
        [Serializable]
        private sealed class Profile
        {
            public int Level;
        }

        private sealed class ToggleWriter : IAtomicTextWriter
        {
            private readonly IAtomicTextWriter _inner = new AtomicFileWriter();
            public bool Fail { get; set; }

            public FileWriteResult Write(string path, string content) => Fail
                ? new FileWriteResult(false, "disk full")
                : _inner.Write(path, content);
        }

        [Test]
        public void LocalStore_FirstWriteFailureAndSupportedTypes_AreObservableAndAtomic()
        {
            string path = System.IO.Path.Combine(
                CreateTempDirectory("local-store"), "store.json");
            var writer = new ToggleWriter();
            var first = new LocalStoreBackend(path, writer);
            Assert.That(first.Set("name", "Alice").Code,
                Is.EqualTo(LocalStoreWriteCode.Success));
            Assert.That(first.Set("level", 7).Code,
                Is.EqualTo(LocalStoreWriteCode.Success));
            Assert.That(first.Set("profile", new Profile { Level = 9 }).Code,
                Is.EqualTo(LocalStoreWriteCode.Success));

            var reloaded = new LocalStoreBackend(path, new AtomicFileWriter());
            Assert.That(reloaded.Get("name", string.Empty), Is.EqualTo("Alice"));
            Assert.That(reloaded.Get("level", 0), Is.EqualTo(7));
            Assert.That(reloaded.Get("profile", new Profile()).Level, Is.EqualTo(9));

            writer.Fail = true;
            Assert.That(first.Set("unsaved", 1).Code,
                Is.EqualTo(LocalStoreWriteCode.WriteFailed));
            Assert.That(first.Has("unsaved"), Is.False);
            Assert.That(first.Remove("missing").Code,
                Is.EqualTo(LocalStoreWriteCode.NotFound));
            Assert.That(first.Remove("name").Code,
                Is.EqualTo(LocalStoreWriteCode.WriteFailed));
            Assert.That(first.Get("name", string.Empty), Is.EqualTo("Alice"));
            int countBeforeClear = first.Count;
            Assert.That(first.ClearAll().Code,
                Is.EqualTo(LocalStoreWriteCode.WriteFailed));
            Assert.That(first.Count, Is.EqualTo(countBeforeClear));
        }

        [Test]
        public void LocalStore_MismatchedSerializedColumns_RejectsTheEntireStore()
        {
            string path = System.IO.Path.Combine(
                CreateTempDirectory("local-store-corrupt"), "store.json");
            System.IO.File.WriteAllText(path,
                "{\"Keys\":[\"first\",\"lost\"],\"TypeNames\":[\"System.Int32\"]," +
                "\"JsonValues\":[\"{\\\"Value\\\":1}\",\"{\\\"Value\\\":2}\"]}");

            var store = new LocalStoreBackend(path, new AtomicFileWriter());

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(store.Has("first"), Is.False);
        }

        [Test]
        public void LocalStore_DuplicateSerializedKeys_RejectsTheEntireStore()
        {
            string path = System.IO.Path.Combine(
                CreateTempDirectory("local-store-duplicates"), "store.json");
            System.IO.File.WriteAllText(path,
                "{\"Keys\":[\"same\",\"same\"],\"TypeNames\":[\"System.Int32\",\"System.Int32\"]," +
                "\"JsonValues\":[\"{\\\"Value\\\":1}\",\"{\\\"Value\\\":2}\"]}");

            var store = new LocalStoreBackend(path, new AtomicFileWriter());

            Assert.That(store.Count, Is.EqualTo(0));
            Assert.That(store.Has("same"), Is.False);
        }

        [Test]
        public void LocalStore_EmptySerializedKey_RejectsTheEntireStore()
        {
            string path = System.IO.Path.Combine(
                CreateTempDirectory("local-store-empty-key"), "store.json");
            System.IO.File.WriteAllText(path,
                "{\"Keys\":[\"\"],\"TypeNames\":[\"System.Int32\"]," +
                "\"JsonValues\":[\"{\\\"Value\\\":1}\"]}");

            var store = new LocalStoreBackend(path, new AtomicFileWriter());

            Assert.That(store.Count, Is.EqualTo(0));
        }
    }
}
