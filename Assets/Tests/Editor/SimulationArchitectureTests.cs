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
            string scripts = Path.Combine(Application.dataPath, "Scripts");
            string[] offenders = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/SimulationCore/") &&
                    Regex.IsMatch(File.ReadAllText(path), @"\b(?:SimWorld|SimOrchestrator)\b"))
                .Select(path => path.Substring(Application.dataPath.Length + 1)
                    .Replace('\\', '/'))
                .ToArray();

            Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
        }
    }
}
