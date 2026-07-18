using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class AtomicFileWriterTests
    {
        private sealed class MemoryFiles : IFileOperations
        {
            public readonly Dictionary<string, string> Files =
                new Dictionary<string, string>();
            public bool ThrowOnReplace { get; set; }
            public bool Exists(string path) => Files.ContainsKey(path);
            public void WriteAllText(string path, string content) => Files[path] = content;
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
    }
}
