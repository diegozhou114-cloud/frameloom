using System;
using System.Collections.Generic;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;

namespace ZWebGLFrameWork.Network.Runtime
{
    public static class RealtimeChannelFactoryRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<RealtimeTransport, IRealtimeChannelFactory> Factories =
            new Dictionary<RealtimeTransport, IRealtimeChannelFactory>();
        private static readonly IRealtimeChannelFactory Router = new RegisteredFactoryRouter();

        public static IRealtimeChannelFactory Current => Router;

        public static void Install(
            RealtimeTransport transport, IRealtimeChannelFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (!Enum.IsDefined(typeof(RealtimeTransport), transport))
                throw new ArgumentOutOfRangeException(nameof(transport));
            lock (Gate)
            {
                if (Factories.ContainsKey(transport))
                    throw new InvalidOperationException(
                        $"A realtime factory is already registered for {transport}.");
                Factories.Add(transport, factory);
            }
        }

        public static void ResetForTests()
        {
            lock (Gate) Factories.Clear();
        }

        private sealed class RegisteredFactoryRouter : IRealtimeChannelFactory
        {
            public IRealtimeChannel Create(RealtimeTransport transport)
            {
                IRealtimeChannelFactory factory;
                lock (Gate)
                {
                    if (!Factories.TryGetValue(transport, out factory))
                        throw new InvalidOperationException(
                            $"No realtime factory is registered for {transport}.");
                }
                return factory.Create(transport);
            }
        }
    }
}
