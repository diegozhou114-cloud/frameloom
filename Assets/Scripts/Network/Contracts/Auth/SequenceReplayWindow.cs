using System;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Network.Contracts.Auth
{
    public sealed class SequenceReplayWindow
    {
        private const int WindowBits = RealtimeProtocol.NativeReplayWindowPackets;
        private readonly object _gate = new object();
        private readonly ulong[] _words = new ulong[WindowBits / 64];
        private bool _initialized;
        private ulong _highest;

        public bool TryAccept(ulong sequence)
        {
            lock (_gate)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    _highest = sequence;
                    _words[0] = 1;
                    return true;
                }
                if (sequence > _highest)
                {
                    ulong delta = sequence - _highest;
                    ShiftWindow(delta >= WindowBits ? WindowBits : (int)delta);
                    _highest = sequence;
                    _words[0] |= 1;
                    return true;
                }

                ulong distance64 = _highest - sequence;
                if (distance64 >= WindowBits) return false;
                int distance = (int)distance64;
                int word = distance / 64;
                ulong mask = 1UL << (distance % 64);
                if ((_words[word] & mask) != 0) return false;
                _words[word] |= mask;
                return true;
            }
        }

        private void ShiftWindow(int distance)
        {
            if (distance >= WindowBits)
            {
                Array.Clear(_words, 0, _words.Length);
                return;
            }

            int wordShift = distance / 64;
            int bitShift = distance % 64;
            for (int destination = _words.Length - 1; destination >= 0; destination--)
            {
                int source = destination - wordShift;
                ulong value = source >= 0 ? _words[source] << bitShift : 0;
                if (bitShift != 0 && source > 0)
                    value |= _words[source - 1] >> (64 - bitShift);
                _words[destination] = value;
            }
        }
    }
}
