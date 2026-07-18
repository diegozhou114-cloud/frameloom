using System;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            return JsonSerializer.Deserialize<ReplayFile>(json, Options)
                ?? throw new JsonException("Replay JSON produced no value.");
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
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
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        throw new JsonException("Spawn info contains an invalid token.");
                    string propertyName = reader.GetString();
                    if (!reader.Read()) throw new JsonException("Spawn info ended unexpectedly.");
                    switch (propertyName?.ToLowerInvariant())
                    {
                        case "archetypeid": archetypeId = reader.GetInt32(); break;
                        case "gridx": gridX = reader.GetInt32(); break;
                        case "gridy": gridY = reader.GetInt32(); break;
                        case "teamid": teamId = reader.GetInt32(); break;
                        default: reader.Skip(); break;
                    }
                }
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
