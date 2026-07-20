using System;
using System.IO;
using System.Text;

namespace ZWebGLFrameWork.Server
{
    public sealed class ReplayInputLimitException : Exception
    {
        public ReplayInputLimitException(string message) : base(message)
        {
        }
    }

    public readonly struct ReplayInputText
    {
        public string Text { get; }
        public int ByteCount { get; }

        public ReplayInputText(string text, int byteCount)
        {
            Text = text;
            ByteCount = byteCount;
        }
    }

    public static class ReplayInput
    {
        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static ReplayInputText ReadUtf8(Stream stream, int maxBytes)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

            var bytes = new byte[checked(maxBytes + 1)];
            int total = 0;
            while (total < bytes.Length)
            {
                int read = stream.Read(bytes, total, bytes.Length - total);
                if (read == 0) break;
                total += read;
            }
            if (total > maxBytes)
                throw new ReplayInputLimitException(
                    $"Replay exceeds the {maxBytes}-byte input limit.");

            int offset = total >= 3 && bytes[0] == 0xef &&
                bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            return new ReplayInputText(
                StrictUtf8.GetString(bytes, offset, total - offset), total);
        }
    }
}
