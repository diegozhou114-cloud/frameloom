using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    [Serializable]
    public sealed class BuildArtifactRecord
    {
        public string path;
        public long bytes;
        public string sha256;
    }

    [Serializable]
    public sealed class BuildReportDocument
    {
        public int schemaVersion = 1;
        public string editorVersion;
        public string target;
        public string[] enabledScenes;
        public BuildArtifactRecord[] artifacts;
        public string result;
        public int warnings;
        public int errors;
    }

    public static class BuildReportJsonWriter
    {
        public static string Write(BuildReport report, string outputPath,
            string[] enabledScenes, string evidenceRoot)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (enabledScenes == null) throw new ArgumentNullException(nameof(enabledScenes));
            if (evidenceRoot == null) throw new ArgumentNullException(nameof(evidenceRoot));

            string target = report.summary.platform.ToString();
            var document = new BuildReportDocument
            {
                editorVersion = Application.unityVersion,
                target = target,
                enabledScenes = (string[])enabledScenes.Clone(),
                artifacts = CollectArtifacts(outputPath, evidenceRoot),
                result = report.summary.result.ToString(),
                warnings = report.summary.totalWarnings,
                errors = report.summary.totalErrors,
            };
            string reportDirectory = Path.Combine(evidenceRoot, "builds", "Reports");
            Directory.CreateDirectory(reportDirectory);
            string reportPath = Path.Combine(reportDirectory, target + ".json");
            File.WriteAllText(reportPath, JsonUtility.ToJson(document, true) + "\n");
            return reportPath;
        }

        public static BuildArtifactRecord[] CollectArtifacts(string outputPath,
            string evidenceRoot)
        {
            if (outputPath == null) throw new ArgumentNullException(nameof(outputPath));
            if (evidenceRoot == null) throw new ArgumentNullException(nameof(evidenceRoot));

            string fullOutputPath = Path.GetFullPath(outputPath);
            string[] files;
            if (Directory.Exists(fullOutputPath))
            {
                files = Directory.GetFiles(fullOutputPath, "*", SearchOption.AllDirectories);
            }
            else if (File.Exists(fullOutputPath))
            {
                string directory = Path.GetDirectoryName(fullOutputPath);
                files = Directory.Exists(directory)
                    ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                    : Array.Empty<string>();
            }
            else
            {
                files = Array.Empty<string>();
            }

            return files.OrderBy(file => file, StringComparer.Ordinal)
                .Select(file => new BuildArtifactRecord
                {
                    path = ToEvidenceRelative(file, evidenceRoot),
                    bytes = new FileInfo(file).Length,
                    sha256 = ComputeSha256(file),
                })
                .ToArray();
        }

        private static string ToEvidenceRelative(string file, string evidenceRoot)
        {
            string root = Path.GetFullPath(evidenceRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(file);
            if (!full.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException($"Build artifact escaped evidence root: {full}");
            return full.Substring(root.Length).Replace('\\', '/');
        }

        private static string ComputeSha256(string file)
        {
            using (FileStream stream = File.OpenRead(file))
            using (SHA256 hash = SHA256.Create())
                return string.Concat(hash.ComputeHash(stream)
                    .Select(value => value.ToString("x2")));
        }
    }
}
