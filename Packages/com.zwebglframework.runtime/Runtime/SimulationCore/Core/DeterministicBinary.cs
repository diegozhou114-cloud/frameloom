using System;
using System.IO;
using System.Text;

namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class DeterministicBinaryWriter
    {
        private readonly MemoryStream _stream = new MemoryStream();

        public void WriteByte(byte value) => _stream.WriteByte(value);

        public void WriteUInt32(uint value)
        {
            WriteByte((byte)value);
            WriteByte((byte)(value >> 8));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 24));
        }

        public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

        public void WriteInt64(long value)
        {
            ulong bits = unchecked((ulong)value);
            WriteUInt32((uint)bits);
            WriteUInt32((uint)(bits >> 32));
        }

        public void WriteUInt64(ulong value)
        {
            WriteUInt32((uint)value);
            WriteUInt32((uint)(value >> 32));
        }

        public void WriteBytes(byte[] value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            _stream.Write(value, 0, value.Length);
        }

        public void WriteUtf8(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteUInt32((uint)bytes.Length);
            WriteBytes(bytes);
        }

        public byte[] ToArray() => _stream.ToArray();
    }

    public sealed class DeterministicBinaryReader
    {
        private readonly byte[] _bytes;
        private int _offset;

        public bool AtEnd => _offset == _bytes.Length;
        public int RemainingBytes => _bytes.Length - _offset;

        public DeterministicBinaryReader(byte[] bytes)
        {
            _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }

        public byte ReadByte()
        {
            EnsureAvailable(1);
            return _bytes[_offset++];
        }

        public uint ReadUInt32()
        {
            uint b0 = ReadByte();
            uint b1 = ReadByte();
            uint b2 = ReadByte();
            uint b3 = ReadByte();
            return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public long ReadInt64()
        {
            ulong low = ReadUInt32();
            ulong high = ReadUInt32();
            return unchecked((long)(low | (high << 32)));
        }

        public ulong ReadUInt64()
        {
            ulong low = ReadUInt32();
            ulong high = ReadUInt32();
            return low | (high << 32);
        }

        public byte[] ReadBytes(int length)
        {
            if (length < 0) throw new FormatException("Negative byte length.");
            EnsureAvailable(length);
            var result = new byte[length];
            Buffer.BlockCopy(_bytes, _offset, result, 0, length);
            _offset += length;
            return result;
        }

        public string ReadUtf8()
        {
            uint length = ReadUInt32();
            if (length > int.MaxValue) throw new FormatException("UTF-8 string is too large.");
            return Encoding.UTF8.GetString(ReadBytes((int)length));
        }

        private void EnsureAvailable(int length)
        {
            if (length < 0 || _offset > _bytes.Length - length)
                throw new FormatException("Unexpected end of deterministic binary payload.");
        }
    }
}
