using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class AtomicFileWriterTests
    {
        private class MemoryFiles : IFileOperations
        {
            public readonly Dictionary<string, string> Files =
                new Dictionary<string, string>();
            public bool ThrowOnReplace { get; set; }
            public bool Exists(string path) => Files.ContainsKey(path);
            public virtual void WriteAllText(string path, string content) => Files[path] = content;
            public void Move(string source, string destination)
            {
                Files[destination] = Files[source];
                Files.Remove(source);
            }
            public void Replace(string source, string destination)
            {
                if (ThrowOnReplace) throw new IOException("replace failed");
                Move(source, destination);
            }
            public void Delete(string path) => Files.Remove(path);
        }

        private sealed class ReentrantFiles : MemoryFiles
        {
            public System.Action OnFirstWrite { get; set; }
            private bool _entered;

            public override void WriteAllText(string path, string content)
            {
                base.WriteAllText(path, content);
                if (_entered) return;
                _entered = true;
                OnFirstWrite?.Invoke();
            }
        }

        [Test]
        public void SaveManager_FirstWriteAndFailedReplace_AreAtomic()
        {
            int integrationSlot = SaveManager.MaxSaveSlots + 1000;
            string integrationPath = SaveManager.GetFilePath(integrationSlot);
            byte[] previous = File.Exists(integrationPath)
                ? File.ReadAllBytes(integrationPath) : null;
            try
            {
                File.Delete(integrationPath);
                File.Delete(integrationPath + ".tmp");
                Assert.That(SaveManager.Save(
                    new PlayerSaveData { PlayerName = "FirstWrite" }, integrationSlot), Is.True);
                Assert.That(File.Exists(integrationPath), Is.True);
                Assert.That(File.ReadAllText(integrationPath), Does.Contain("FirstWrite"));
                Assert.That(File.Exists(integrationPath + ".tmp"), Is.False);
            }
            finally
            {
                File.Delete(integrationPath);
                File.Delete(integrationPath + ".tmp");
                if (previous != null) File.WriteAllBytes(integrationPath, previous);
            }

            var firstWriteFiles = new MemoryFiles();
            var firstWrite = new AtomicFileWriter(firstWriteFiles);
            FileWriteResult created = firstWrite.Write("save.json", "new");
            Assert.That(created.Succeeded, Is.True);
            Assert.That(firstWriteFiles.Files["save.json"], Is.EqualTo("new"));
            Assert.That(firstWriteFiles.Files.ContainsKey("save.json.tmp"), Is.False);

            var replaceFiles = new MemoryFiles { ThrowOnReplace = true };
            replaceFiles.Files["save.json"] = "old";
            var replacement = new AtomicFileWriter(replaceFiles);
            FileWriteResult failed = replacement.Write("save.json", "new");
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(replaceFiles.Files["save.json"], Is.EqualTo("old"));
            Assert.That(replaceFiles.Files.ContainsKey("save.json.tmp"), Is.False);
        }

        [Test]
        public void ConcurrentWrites_UseSeparateTemporaryFiles()
        {
            var files = new ReentrantFiles();
            var writer = new AtomicFileWriter(files);
            FileWriteResult nested = default;
            files.OnFirstWrite = () => nested = writer.Write("save.json", "second");

            FileWriteResult first = writer.Write("save.json", "first");

            Assert.That(first.Succeeded, Is.True);
            Assert.That(nested.Succeeded, Is.True);
            Assert.That(files.Files["save.json"], Is.EqualTo("first"));
        }
    }
}
