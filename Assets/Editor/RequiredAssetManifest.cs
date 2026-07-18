using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using ZWebGLFrameWork.Combat;

namespace ZWebGLFrameWork.Editor
{
    [Serializable]
    public sealed class RequiredAssetManifestDocument
    {
        public int version;
        public string[] supportedModes;
        public int[] archetypeIds;
        public int[] unitIds;
        public string[] uiPanels;
        public RequiredAssetEntry[] entries;
    }

    [Serializable]
    public sealed class RequiredAssetEntry
    {
        public string key;
        public string assetPath;
        public string expectedType;
    }

    public readonly struct RequiredAssetValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<string> Errors { get; }

        public RequiredAssetValidationResult(List<string> errors)
        {
            Errors = errors;
            IsValid = errors.Count == 0;
        }
    }

    public static class RequiredAssetManifestValidator
    {
        public const string ManifestPath =
            "Assets/Resources/Config/RequiredAssets.json";

        private static readonly string[] ApprovedModes =
        {
            "builder_base", "campaign", "clan_war", "village_attack"
        };

        public static RequiredAssetValidationResult Validate(string manifestPath)
        {
            var errors = new List<string>();
            if (!File.Exists(manifestPath))
            {
                errors.Add($"Missing required asset manifest: {manifestPath}");
                return new RequiredAssetValidationResult(errors);
            }

            string json = File.ReadAllText(manifestPath);
            RequiredAssetManifestDocument document;
            try
            {
                document = JsonUtility.FromJson<RequiredAssetManifestDocument>(
                    json);
            }
            catch (Exception exception)
            {
                errors.Add($"Invalid manifest JSON: {exception.Message}");
                return new RequiredAssetValidationResult(errors);
            }

            if (document == null || document.version != 1)
                errors.Add("Required asset manifest version must equal 1.");

            ValidateUnique(document?.supportedModes, "supported mode", errors);
            ValidateUnique(document?.uiPanels, "UI panel", errors);
            ValidateUnique(document?.archetypeIds, "archetype id", errors);
            ValidateUnique(document?.unitIds, "unit id", errors);

            if (document?.supportedModes == null ||
                !document.supportedModes.OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(ApprovedModes))
            {
                errors.Add(
                    "Supported modes must be builder_base, campaign, clan_war, and village_attack.");
            }

            var entries = document?.entries ?? Array.Empty<RequiredAssetEntry>();
            if (entries.Length == 0)
                errors.Add("Manifest contains no required asset entries.");

            if (Regex.IsMatch(
                json,
                "\\\"entries\\\"\\s*:\\s*\\[[^\\]]*\\bnull\\b",
                RegexOptions.Singleline))
            {
                errors.Add("Manifest contains a null required asset entry.");
            }

            foreach (RequiredAssetEntry entry in entries)
            {
                if (entry == null)
                {
                    errors.Add("Manifest contains a null required asset entry.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.key))
                    errors.Add("Required asset entry has an empty key.");
                if (string.IsNullOrWhiteSpace(entry.assetPath))
                    errors.Add(
                        $"Required asset entry {entry.key ?? "<empty>"} has an empty path.");
                if (string.IsNullOrWhiteSpace(entry.expectedType))
                    errors.Add(
                        $"Required asset entry {entry.key ?? "<empty>"} has an empty expected type.");
            }

            var duplicateKeys = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.key))
                .GroupBy(entry => entry.key)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key);
            foreach (string key in duplicateKeys)
                errors.Add($"Duplicate required asset key: {key}");

            var duplicatePaths = entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.assetPath))
                .GroupBy(entry => entry.assetPath)
                .Where(group => group.Count() != 1)
                .Select(group => group.Key);
            foreach (string path in duplicatePaths)
                errors.Add($"Duplicate required asset path: {path}");

            foreach (RequiredAssetEntry entry in
                entries.OrderBy(value => value?.key, StringComparer.Ordinal))
            {
                ValidateEntry(entry, errors);
            }

            var viewConfig = AssetDatabase.LoadAssetAtPath<ViewConfig>(
                "Assets/Resources/Config/ViewConfig.asset");
            if (viewConfig == null || viewConfig.Entries == null ||
                viewConfig.Entries.Length != 5)
            {
                errors.Add("ViewConfig must contain exactly five non-null archetype mappings.");
            }
            else
            {
                foreach (ViewConfig.ArchetypeEntry entry in viewConfig.Entries)
                {
                    if (entry.Prefab == null)
                        errors.Add($"ViewConfig archetype {entry.ArchetypeId} has a null prefab.");
                }

                if (viewConfig.Entries.Select(entry => entry.ArchetypeId)
                    .Distinct().Count() != 5)
                {
                    errors.Add("ViewConfig contains duplicate archetype ids.");
                }
            }

            return new RequiredAssetValidationResult(errors);
        }

        private static void ValidateEntry(RequiredAssetEntry entry, List<string> errors)
        {
            if (entry == null || string.IsNullOrEmpty(entry.assetPath)) return;

            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(entry.assetPath);
            if (asset == null)
            {
                errors.Add(
                    $"Required asset does not resolve: {entry.key} -> {entry.assetPath}");
                return;
            }

            bool typeMatches = entry.expectedType switch
            {
                "GameObject" => asset is GameObject,
                "SceneAsset" => asset is SceneAsset,
                "ScriptableObject" => asset is ScriptableObject,
                "TextAsset" => asset is TextAsset,
                _ => false,
            };
            if (!typeMatches)
            {
                errors.Add(
                    $"Required asset has wrong type: {entry.key} expected {entry.expectedType}");
            }
        }

        private static void ValidateUnique<T>(T[] values, string label, List<string> errors)
        {
            if (values == null || values.Length == 0)
            {
                errors.Add($"Manifest contains no {label} values.");
                return;
            }

            if (values.Distinct().Count() != values.Length)
                errors.Add($"Manifest contains duplicate {label} values.");
        }
    }
}
