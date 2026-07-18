using System;
using System.IO;

namespace ZWebGLFrameWork.Data
{
    public readonly struct FileWriteResult
    {
        public bool Succeeded { get; }
        public string Error { get; }

        public FileWriteResult(bool succeeded, string error = null)
        {
            Succeeded = succeeded;
            Error = error;
        }
    }

    public interface IAtomicTextWriter
    {
        FileWriteResult Write(string path, string content);
    }

    public interface IFileOperations
    {
        bool Exists(string path);
        void WriteAllText(string path, string content);
        void Move(string source, string destination);
        void Replace(string source, string destination);
        void Delete(string path);
    }

    public sealed class SystemFileOperations : IFileOperations
    {
        public bool Exists(string path) => File.Exists(path);
        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination) => File.Replace(source, destination, null);
        public void Delete(string path) => File.Delete(path);
    }

    public sealed class AtomicFileWriter : IAtomicTextWriter
    {
        private readonly IFileOperations _files;

        public AtomicFileWriter() : this(new SystemFileOperations()) { }

        public AtomicFileWriter(IFileOperations files) =>
            _files = files ?? throw new ArgumentNullException(nameof(files));

        public FileWriteResult Write(string path, string content)
        {
            string temporaryPath = path + ".tmp";
            try
            {
                _files.WriteAllText(temporaryPath, content);
                if (_files.Exists(path))
                    _files.Replace(temporaryPath, path);
                else
                    _files.Move(temporaryPath, path);
                return new FileWriteResult(true);
            }
            catch (Exception exception)
            {
                try { _files.Delete(temporaryPath); } catch { }
                return new FileWriteResult(false, exception.Message);
            }
        }
    }
}
