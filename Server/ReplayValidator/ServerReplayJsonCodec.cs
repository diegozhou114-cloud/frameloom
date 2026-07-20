using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public static class ServerReplayJsonCodec
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        public static string Serialize(ReplayFile replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            return JsonSerializer.Serialize(replay, Options);
        }

        public static ReplayFile Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Replay JSON must not be empty.", nameof(json));
            StrictJsonDocument.ValidateReplayShape(json);
            return JsonSerializer.Deserialize<ReplayFile>(json, Options)
                ?? throw new JsonException("Replay JSON produced no value.");
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                WriteIndented = true
            };
            options.Converters.Add(new SimEntitySpawnInfoConverter());
            return options;
        }

        private sealed class SimEntitySpawnInfoConverter : JsonConverter<SimEntitySpawnInfo>
        {
            public override SimEntitySpawnInfo Read(
                ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException("Spawn info must be an object.");

                int archetypeId = 0;
                int gridX = 0;
                int gridY = 0;
                int teamId = 0;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        throw new JsonException("Spawn info contains an invalid token.");
                    string propertyName = reader.GetString();
                    if (!seen.Add(propertyName))
                        throw new JsonException("Spawn info contains a duplicate property.");
                    if (!reader.Read()) throw new JsonException("Spawn info ended unexpectedly.");
                    switch (propertyName)
                    {
                        case "ArchetypeId": archetypeId = reader.GetInt32(); break;
                        case "GridX": gridX = reader.GetInt32(); break;
                        case "GridY": gridY = reader.GetInt32(); break;
                        case "TeamId": teamId = reader.GetInt32(); break;
                        default: throw new JsonException(
                            "Spawn info contains an unknown property.");
                    }
                }
                if (seen.Count != 4 ||
                    !seen.Contains("ArchetypeId") || !seen.Contains("GridX") ||
                    !seen.Contains("GridY") || !seen.Contains("TeamId"))
                    throw new JsonException("Spawn info is missing a required property.");
                return new SimEntitySpawnInfo(archetypeId, gridX, gridY, teamId);
            }

            public override void Write(
                Utf8JsonWriter writer, SimEntitySpawnInfo value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber(nameof(value.ArchetypeId), value.ArchetypeId);
                writer.WriteNumber(nameof(value.GridX), value.GridX);
                writer.WriteNumber(nameof(value.GridY), value.GridY);
                writer.WriteNumber(nameof(value.TeamId), value.TeamId);
                writer.WriteEndObject();
            }
        }
    }
}
