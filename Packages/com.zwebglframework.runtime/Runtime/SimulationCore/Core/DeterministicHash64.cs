namespace ZWebGLFrameWork.SimulationCore
{
    public struct DeterministicHash64
    {
        public const ulong OffsetBasis = 14695981039346656037UL;
        public const ulong Prime = 1099511628211UL;

        public ulong Value { get; private set; }

        public DeterministicHash64(ulong seed = OffsetBasis)
        {
            Value = seed;
        }

        public void AddByte(byte value)
        {
            Value ^= value;
            Value *= Prime;
        }

        public void AddBool(bool value) => AddByte(value ? (byte)1 : (byte)0);

        public void AddInt32(int value) => AddUInt32(unchecked((uint)value));

        public void AddUInt32(uint value)
        {
            AddByte((byte)value);
            AddByte((byte)(value >> 8));
            AddByte((byte)(value >> 16));
            AddByte((byte)(value >> 24));
        }

        public void AddInt64(long value) => AddUInt64(unchecked((ulong)value));

        public void AddUInt64(ulong value)
        {
            AddUInt32((uint)value);
            AddUInt32((uint)(value >> 32));
        }

        public void AddBytes(byte[] bytes)
        {
            if (bytes == null) return;
            AddBytes(bytes, 0, bytes.Length);
        }

        internal void AddBytes(byte[] bytes, int offset, int count)
        {
            if (bytes == null) return;
            int end = offset + count;
            for (int index = offset; index < end; index++) AddByte(bytes[index]);
        }
    }
}
