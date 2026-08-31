using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class SimulationArchitectureTests
    {
        [Test]
        public void ProductionAssemblies_DoNotBypassSimulationSession()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string runtime = Path.Combine(
                projectRoot, "Packages/com.zwebglframework.runtime/Runtime");
            string[] offenders = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/SimulationCore/") &&
                    Regex.IsMatch(File.ReadAllText(path), @"\b(?:SimWorld|SimOrchestrator)\b"))
                .Select(path => path.Substring(projectRoot.Length + 1)
                    .Replace('\\', '/'))
                .ToArray();

            Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
        }

        [Test]
        public void ObsoleteFrameSyncStack_IsAbsent()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string[] obsoletePaths =
            {
                "Packages/com.zwebglframework.runtime/Runtime/SimulationCore/Network/FrameInput.cs",
                "Packages/com.zwebglframework.runtime/Runtime/SimulationCore/Network/InputPacket.cs",
                "Packages/com.zwebglframework.runtime/Runtime/SimulationCore/Network/InputReceiver.cs",
                "Packages/com.zwebglframework.runtime/Runtime/SimulationCore/Network/InputSender.cs",
                "Packages/com.zwebglframework.runtime/Runtime/SimulationCore/Network/FrameSyncClient.cs",
                "Packages/com.zwebglframework.runtime/Runtime/Network/Simulation/KcpTransport.cs",
            };
            string[] obsolete = obsoletePaths
                .Where(path => File.Exists(Path.Combine(projectRoot, path)))
                .ToArray();

            Assert.That(obsolete, Is.Empty, string.Join("\n", obsolete));
        }
    }
}
