using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ZWebGLFrameWork.Server
{
    internal static class StrictJsonDocument
    {
        public static void RejectDuplicateProperties(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Visit(document.RootElement);
        }

        public static void ValidateReplayShape(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            RequireObject(root,
                new[]
                {
                    "BattleInit",
                    "FormatVersion", "SimulationSchemaHash", "ContentConfigHash",
                    "TickRate", "MatchId", "InitialStateHash", "SubmissionNonce",
                    "FinalTick", "FinalResult", "FinalChecksum", "Version", "Seed",
                    "InitialLayout", "Records", "TotalTicks"
                },
                new[]
                {
                    "FormatVersion", "SimulationSchemaHash", "ContentConfigHash",
                    "TickRate", "MatchId", "InitialStateHash", "SubmissionNonce",
                    "FinalTick", "FinalResult", "FinalChecksum", "Version", "Seed",
                    "InitialLayout", "Records"
                });
            if (root.TryGetProperty("BattleInit", out JsonElement battleInit) &&
                battleInit.ValueKind != JsonValueKind.Null)
            {
                RequireObject(battleInit, new[]
                {
                    "ProtocolVersion", "SchemaVersion", "MatchId", "SessionId",
                    "Seed", "TickRate", "Mode", "ContentHash", "Players",
                    "InitialEntities", "InputDeadlineTicks"
                });
                ValidateArray(battleInit.GetProperty("Players"), player =>
                    RequireObject(player, new[] { "PlayerId", "AccountId" }));
                ValidateArray(battleInit.GetProperty("InitialEntities"), entity =>
                    RequireObject(entity, new[]
                    {
                        "ArchetypeId", "GridX", "GridY", "TeamId",
                        "OwnerPlayerId"
                    }));
            }
            JsonElement finalResult = root.GetProperty("FinalResult");
            if (finalResult.ValueKind != JsonValueKind.Null)
                ValidateResult(finalResult);
            ValidateArray(root.GetProperty("InitialLayout"), element =>
                RequireObject(element, new[]
                {
                    "ArchetypeId", "GridX", "GridY", "TeamId"
                }));
            ValidateArray(root.GetProperty("Records"), record =>
            {
                RequireObject(record, new[] { "Tick", "Commands", "Checksum" });
                ValidateArray(record.GetProperty("Commands"), command =>
                    RequireObject(command, new[]
                    {
                        "Type", "Tick", "PlayerId", "Sequence", "UnitTypeId",
                        "EntityId", "CasterId", "SkillId", "GridX", "TargetX",
                        "GridY", "TargetY"
                    }));
            });
        }

        public static void ValidateMatchShape(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            RequireObject(root, new[]
            {
                "MatchId", "InitialStateHash", "SubmissionNonce",
                "SimulationSchemaHash", "ContentConfigHash",
                "CanonicalManifestBytes", "BattleInit", "InitialSnapshot",
                "ExpectedResult", "AllowedSubmitterPlayerIds"
            });
            JsonElement init = root.GetProperty("BattleInit");
            RequireObject(init, new[]
            {
                "ProtocolVersion", "SchemaVersion", "MatchId", "SessionId", "Seed",
                "TickRate", "Mode", "ContentHash", "Players", "InitialEntities",
                "InputDeadlineTicks"
            });
            ValidateArray(init.GetProperty("Players"), player =>
                RequireObject(player, new[] { "PlayerId", "AccountId" }));
            ValidateArray(init.GetProperty("InitialEntities"), entity =>
                RequireObject(entity, new[]
                {
                    "ArchetypeId", "GridX", "GridY", "TeamId", "OwnerPlayerId"
                }));
            RequireObject(root.GetProperty("InitialSnapshot"), new[]
            {
                "MatchId", "SessionId", "SchemaVersion", "ContentHash",
                "CompletedTick", "NextTick", "TimelineRevision",
                "UncompressedLength", "StateChecksum", "Payload", "PayloadSha256"
            });
            ValidateResult(root.GetProperty("ExpectedResult"));
            ValidateArray(root.GetProperty("AllowedSubmitterPlayerIds"), _ => { });
        }

        private static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new JsonException(
                            $"Duplicate JSON property '{property.Name}'.");
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray()) Visit(item);
            }
        }

        private static void ValidateResult(JsonElement result)
        {
            RequireObject(result, new[]
            {
                "MatchId", "ResultId", "WinnerTeamId", "Stars", "LootGold",
                "LootElixir", "FinalTick", "FinalChecksum"
            });
        }

        private static void ValidateArray(
            JsonElement element, Action<JsonElement> validateItem)
        {
            if (element.ValueKind != JsonValueKind.Array)
                throw new JsonException("Expected a JSON array.");
            foreach (JsonElement item in element.EnumerateArray()) validateItem(item);
        }

        private static void RequireObject(
            JsonElement element, string[] required) =>
            RequireObject(element, required, required);

        private static void RequireObject(
            JsonElement element, string[] allowed, string[] required)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected a JSON object.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name) ||
                    Array.IndexOf(allowed, property.Name) < 0)
                    throw new JsonException(
                        $"Unknown or duplicate JSON property '{property.Name}'.");
            }
            foreach (string property in required)
                if (!seen.Contains(property))
                    throw new JsonException(
                        $"Missing required JSON property '{property}'.");
        }
    }
}
