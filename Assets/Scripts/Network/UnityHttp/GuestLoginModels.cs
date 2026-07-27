using System;
using System.Text;
using UnityEngine;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;

namespace ZWebGLFrameWork.Network.UnityHttp
{
    [Serializable]
    internal sealed class GuestLoginDto
    {
        public string playerId;
        public string sessionId;
        public string ticket;
        public string resumeToken;
        public string macKey;
        public long expiresAtUnixSeconds;
    }

    public static class GuestLoginRequest
    {
        public static HttpRequest Create(Uri onlineBaseUri)
        {
            UnityHttpApi.ValidateOnlineUri(onlineBaseUri);
            return new HttpRequest(HttpMethod.Post, new Uri(onlineBaseUri, "/v1/login/guest"), Array.Empty<byte>());
        }
    }

    public sealed class GuestLoginResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string PlayerId { get; set; }
        public RealtimeCredentials Credentials { get; set; }

        public static GuestLoginResult Parse(HttpResponse response)
        {
            try
            {
                if (response == null || response.StatusCode < 200 || response.StatusCode >= 300)
                    return Failure();
                string json = new UTF8Encoding(false, true).GetString(response.Body ?? Array.Empty<byte>());
                GuestLoginDto dto = JsonUtility.FromJson<GuestLoginDto>(json);
                if (dto == null || string.IsNullOrWhiteSpace(dto.playerId) || string.IsNullOrWhiteSpace(dto.sessionId) ||
                    string.IsNullOrWhiteSpace(dto.ticket) || string.IsNullOrWhiteSpace(dto.resumeToken) ||
                    string.IsNullOrWhiteSpace(dto.macKey) || dto.expiresAtUnixSeconds <= 0)
                    return Failure();
                byte[] macKey = Convert.FromBase64String(dto.macKey);
                byte[] ticket = Convert.FromBase64String(dto.ticket);
                byte[] resumeToken = Convert.FromBase64String(dto.resumeToken);
                Guid sessionId = Guid.Parse(dto.sessionId);
                if (sessionId == Guid.Empty || macKey.Length != 32 || ticket.Length == 0 || resumeToken.Length == 0)
                    return Failure();
                return new GuestLoginResult
                {
                    Success = true,
                    PlayerId = dto.playerId,
                    Credentials = new RealtimeCredentials
                    {
                        SessionId = sessionId,
                        OpaqueTicket = ticket,
                        ResumeToken = resumeToken,
                        MacKey = macKey,
                        ExpiresAtUnixSeconds = dto.expiresAtUnixSeconds
                    }
                };
            }
            catch (DecoderFallbackException) { return Failure(); }
            catch (ArgumentException) { return Failure(); }
            catch (FormatException) { return Failure(); }
            catch (OverflowException) { return Failure(); }
        }

        private static GuestLoginResult Failure() => new GuestLoginResult
        {
            Success = false,
            Error = "Malformed guest login response."
        };
    }
}
