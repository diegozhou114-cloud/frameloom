using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public interface IRealtimeTicketStore
    {
        ProtocolError Validate(byte[] opaqueTicket, long utcUnixSeconds,
            out RealtimeSessionClaims? claims);
    }
}
