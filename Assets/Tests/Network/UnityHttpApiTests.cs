using System;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.UnityHttp;

namespace ZWebGLFrameWork.Network.Tests
{
    public sealed class UnityHttpApiTests
    {
        [Test]
        public void UnityHttpApi_ExposesPublicValidateOnlineUriContract()
        {
            Type apiType = Type.GetType(
                "ZWebGLFrameWork.Network.UnityHttp.UnityHttpApi, ZWebGLFrameWork.Network.UnityHttp");

            Assert.That(apiType, Is.Not.Null);
            MethodInfo method = apiType.GetMethod(
                "ValidateOnlineUri", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(Uri) }, null);
            Assert.That(method, Is.Not.Null);
        }

        [Test]
        public void ValidateOnlineUri_RejectsNonHttpsAndAcceptsAbsoluteHttps()
        {
            Assert.Throws<NetworkPolicyException>(() =>
                UnityHttpApi.ValidateOnlineUri(new Uri("http://game.example/v1/login")));
            Assert.Throws<NetworkPolicyException>(() => UnityHttpApi.ValidateOnlineUri(null));
            Assert.DoesNotThrow(() =>
                UnityHttpApi.ValidateOnlineUri(new Uri("https://game.example/v1/login")));
        }

        [Test]
        public void GuestLoginRequest_CreatesHttpsGuestLoginPost()
        {
            HttpRequest request = GuestLoginRequest.Create(new Uri("https://game.example"));

            Assert.That(request.Uri, Is.EqualTo(new Uri("https://game.example/v1/login/guest")));
            Assert.That(request.Method.Value, Is.EqualTo("POST"));
        }

        [Test]
        public void GuestLoginResult_MalformedInputsAreFailures()
        {
            foreach (byte[] malformed in new[]
            {
                Array.Empty<byte>(), Encoding.UTF8.GetBytes("{"),
                Encoding.UTF8.GetBytes("{\"sessionId\":\"not-a-guid\"}"),
                Encoding.UTF8.GetBytes("{\"playerId\":\"p\",\"sessionId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"ticket\":\"!\"}")
            })
            {
                Assert.That(GuestLoginResult.Parse(new HttpResponse { StatusCode = 200, Body = malformed }).Success,
                    Is.False);
            }
        }

        [Test]
        public void GuestLoginResult_ParsesEveryCredentialField()
        {
            const string json = "{\"playerId\":\"player-1\",\"sessionId\":\"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee\",\"ticket\":\"AQI=\",\"resumeToken\":\"AwQ=\",\"macKey\":\"AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=\",\"expiresAtUnixSeconds\":42}";
            GuestLoginResult result = GuestLoginResult.Parse(new HttpResponse
            {
                StatusCode = 200,
                Body = Encoding.UTF8.GetBytes(json)
            });

            Assert.That(result.Success, Is.True);
            Assert.That(result.PlayerId, Is.EqualTo("player-1"));
            Assert.That(result.Credentials.SessionId, Is.EqualTo(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));
            Assert.That(result.Credentials.OpaqueTicket, Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(result.Credentials.ResumeToken, Is.EqualTo(new byte[] { 3, 4 }));
            Assert.That(result.Credentials.MacKey.Length, Is.EqualTo(32));
            Assert.That(result.Credentials.ExpiresAtUnixSeconds, Is.EqualTo(42));
        }
    }
}
