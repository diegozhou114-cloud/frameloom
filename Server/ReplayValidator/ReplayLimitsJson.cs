using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZWebGLFrameWork.Server
{
    public static class ReplayLimitsJson
    {
        private static readonly JsonSerializerOptions Options =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                WriteIndented = false
            };

        public static string SerializeApproved() =>
            JsonSerializer.Serialize(LimitsDocument.From(
                ReplayValidationLimits.Approved), Options);

        public static ReplayValidationLimits DeserializeApproved(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException("Replay limits JSON must not be empty.");
            StrictJsonDocument.RejectDuplicateProperties(json);
            LimitsDocument document =
                JsonSerializer.Deserialize<LimitsDocument>(json, Options)
                ?? throw new JsonException("Replay limits JSON produced no value.");
            ReplayValidationLimits approved = ReplayValidationLimits.Approved;
            if (document.MaxFileBytes != approved.MaxFileBytes ||
                document.MaxTicks != approved.MaxTicks ||
                document.MaxCommandsPerTick != approved.MaxCommandsPerTick ||
                document.MaxTotalCommands != approved.MaxTotalCommands ||
                document.MaxInitialEntities != approved.MaxInitialEntities ||
                document.MaxDecompressedSnapshotBytes !=
                    approved.MaxDecompressedSnapshotBytes)
                throw new JsonException(
                    "Replay limits must exactly match the approved values.");
            return approved;
        }

        private sealed class LimitsDocument
        {
            public int? MaxFileBytes { get; set; }
            public int? MaxTicks { get; set; }
            public int? MaxCommandsPerTick { get; set; }
            public int? MaxTotalCommands { get; set; }
            public int? MaxInitialEntities { get; set; }
            public int? MaxDecompressedSnapshotBytes { get; set; }

            public static LimitsDocument From(ReplayValidationLimits limits) =>
                new LimitsDocument
                {
                    MaxFileBytes = limits.MaxFileBytes,
                    MaxTicks = limits.MaxTicks,
                    MaxCommandsPerTick = limits.MaxCommandsPerTick,
                    MaxTotalCommands = limits.MaxTotalCommands,
                    MaxInitialEntities = limits.MaxInitialEntities,
                    MaxDecompressedSnapshotBytes =
                        limits.MaxDecompressedSnapshotBytes
                };
        }
    }
}
