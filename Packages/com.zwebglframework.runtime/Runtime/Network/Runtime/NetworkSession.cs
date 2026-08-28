using System;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Network
{
    public sealed class NetworkSession
    {
        public static NetworkSession Current { get; set; }

        public string PlayerId { get; }
        public RealtimeCredentials Credentials { get; }

        public NetworkSession(string playerId, RealtimeCredentials credentials)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("Player ID is required.", nameof(playerId));
            PlayerId = playerId;
            Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }
    }
}
