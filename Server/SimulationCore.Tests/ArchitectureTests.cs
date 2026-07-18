using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class ArchitectureTests
    {
        [Test]
        public void SimulationCore_HasNoUnityEngineReferences()
        {
            string root = RepositoryRoot();
            string core = Path.Combine(root, "Assets/Scripts/SimulationCore");
            string[] forbidden = { "UnityEngine", "System.Text.Json" };
            string[] offenders = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
                .Where(path => forbidden.Any(token => File.ReadAllText(path).Contains(token)))
                .Select(path => Path.GetRelativePath(root, path))
                .OrderBy(path => path)
                .ToArray();

            Assert.That(offenders, Is.Empty, string.Join("\n", offenders));
        }

        [Test]
        public void ReplayValidator_DotNet8_BuildsAgainstCurrentSimulationCore()
        {
            string root = RepositoryRoot();
            ProcessResult result = RunDotNet(root, "build",
                "Server/ReplayValidator/ReplayValidator.csproj",
                "--configuration", "Release",
                "--artifacts-path", "/tmp/zweb-sim-migration/task-03/contract-build",
                "--warnaserror");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.Output);
        }

        [Test]
        public void ReplayValidator_ReferencesSharedSimulationCoreProject()
        {
            string root = RepositoryRoot();
            string projectPath = Path.Combine(root, "Server/ReplayValidator/ReplayValidator.csproj");
            string project = File.ReadAllText(projectPath);

            Assert.That(project, Does.Contain("../SimulationCore/ZWebGLFrameWork.SimulationCore.csproj"));
            Assert.That(project, Does.Not.Contain("Assets\\Scripts\\SimulationCore"));
            Assert.That(project, Does.Not.Contain("Assets/Scripts/SimulationCore"));
        }

        private static string RepositoryRoot()
        {
            return Path.GetFullPath(System.Environment.GetEnvironmentVariable("ZWEB_REPO_ROOT")
                ?? Directory.GetCurrentDirectory());
        }

        private static ProcessResult RunDotNet(string root, params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            using Process process = Process.Start(start)
                ?? throw new System.InvalidOperationException("failed to start dotnet");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            return new ProcessResult(process.ExitCode, stdout.Result + stderr.Result);
        }

        private sealed class ProcessResult
        {
            public int ExitCode { get; }
            public string Output { get; }

            public ProcessResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output;
            }
        }
    }
}
