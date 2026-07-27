using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace ZWebGLFrameWork.Network.Tests
{
    [TestFixture]
    public sealed class WebGlAssemblyBoundaryTests
    {
        [Test]
        public void WebGLAssembly_HasNoSocketThreadOrNativeKcpReferences()
        {
            string[] files = Directory.GetFiles("Assets/Scripts/Network/WebGL", "*.cs",
                SearchOption.AllDirectories);
            string source = string.Join("\n", files.Select(File.ReadAllText));
            Assert.That(source, Does.Not.Contain("System.Net.Sockets"));
            Assert.That(source, Does.Not.Contain("System.Threading.Thread"));
            Assert.That(source, Does.Not.Contain("kcp2k"));
            bool handwrittenBridgeExists = Directory.Exists("Assets/Plugins/WebGL") &&
                Directory.GetFiles("Assets/Plugins/WebGL", "ZWebRealtime.jslib",
                    SearchOption.AllDirectories).Length != 0;
            Assert.That(handwrittenBridgeExists, Is.False);

            string asmdef = File.ReadAllText(
                "Assets/Scripts/Network/WebGL/ZWebGLFrameWork.Network.WebGL.asmdef");
            Assert.That(asmdef, Does.Contain("ZWebGLFrameWork.Network.Contracts"));
            Assert.That(asmdef, Does.Contain("colyseus.nativewebsocket"));
            Assert.That(asmdef, Does.Not.Contain("ZWebGLFrameWork.Network.Native"));
            Assert.That(asmdef, Does.Not.Contain("ZWebGLFrameWork.Kcp2k"));
            Assembly playerAssembly = CompilationPipeline.GetAssemblies(
                AssembliesType.PlayerWithoutTestAssemblies).Single(assembly =>
                assembly.name == "ZWebGLFrameWork.Network.WebGL");
            string[] references = playerAssembly.assemblyReferences.Select(reference =>
                reference.name).ToArray();
            Assert.That(references, Does.Not.Contain("ZWebGLFrameWork.Network.Native"));
            Assert.That(references, Does.Not.Contain("ZWebGLFrameWork.Kcp2k"));
        }
    }
}
