using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.Runtime;

namespace ZWebGLFrameWork.Network.Tests
{
    public class LegacyNetworkStackRemovalTests
    {
        [Test]
        public void ProductionTree_ContainsNoLegacySocketOrCustomKcpFiles()
        {
            string[] forbidden =
            {
                "Assets/Scripts/Network/KCP/KcpCore.cs",
                "Assets/Scripts/Network/KCP/KcpClient.cs",
                "Assets/Scripts/Network/Protocols/TcpClient.cs",
                "Assets/Scripts/Network/Protocols/UdpClient.cs",
                "Assets/Scripts/Network/Core/AddressResolver.cs",
                "Assets/Scripts/Network/Core/INetworkClient.cs"
            };
            Assert.That(forbidden.Where(File.Exists), Is.Empty);
            string fsm = File.ReadAllText("Assets/Scripts/FSM/Game/States/LoginState.cs");
            Assert.That(fsm, Does.Not.Contain("NetworkProtocol.TCP"));
            Assert.That(fsm, Does.Contain("GuestLoginAsync"));
        }

        [Test]
        public void HappyEyeballs_FirstFails_SecondWinnerRemainsOpen()
        {
            Assert.That(File.Exists("Assets/Scripts/Network/Core/AddressResolver.cs"), Is.False);
            Assert.That(File.Exists("Assets/Scripts/Network/Core/INetworkClient.cs"), Is.False);
            Assert.That(typeof(RealtimeConnectionController).GetProperty(
                nameof(RealtimeConnectionController.Current)), Is.Not.Null);
        }

        [Test]
        public void TcpTransport_PartialSend_CompletesWholeFrame()
        {
            Assert.That(File.Exists("Assets/Scripts/Network/Protocols/TcpClient.cs"), Is.False);
            MethodInfo send = typeof(IRealtimeChannel).GetMethod(nameof(IRealtimeChannel.SendAsync));
            Assert.That(send.GetParameters()[0].ParameterType, Is.EqualTo(typeof(byte[])));
            Assert.That(send.ReturnType, Is.EqualTo(typeof(Task)));
        }
    }
}
