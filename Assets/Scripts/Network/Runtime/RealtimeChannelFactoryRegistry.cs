using System;

namespace ZWebGLFrameWork.Network.Runtime
{
    public static class RealtimeChannelFactoryRegistry
    {
        private static readonly object Gate = new object();
        private static IRealtimeChannelFactory _current;

        public static IRealtimeChannelFactory Current
        {
            get
            {
                lock (Gate) return _current;
            }
        }

        public static void Install(IRealtimeChannelFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (Gate) _current = factory;
        }

        public static void ResetForTests()
        {
            lock (Gate) _current = null;
        }
    }
}
