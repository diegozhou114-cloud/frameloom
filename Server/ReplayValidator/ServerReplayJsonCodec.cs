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
            options.Converters.Add(new ReplayBattleInitConverter());
            return options;
        }

        private sealed class ReplayBattleInitConverter : JsonConverter<ReplayBattleInit>
        {
            public override ReplayBattleInit Read(
                ref Utf8JsonReader reader, Type typeToConvert,
                JsonSerializerOptions options)
            {
                using JsonDocument document = JsonDocument.ParseValue(ref reader);
                JsonElement root = document.RootElement;
                SimPlayerIdentity[] players = ReadPlayers(root.GetProperty("Players"));
                InitialEntitySpec[] entities =
                    ReadEntities(root.GetProperty("InitialEntities"));
                return new ReplayBattleInit(
                    root.GetProperty("ProtocolVersion").GetInt32(),
                    root.GetProperty("SchemaVersion").GetInt32(),
                    root.GetProperty("MatchId").GetString(),
                    root.GetProperty("SessionId").GetString(),
                    root.GetProperty("Seed").GetInt32(),
                    root.GetProperty("TickRate").GetInt32(),
                    (SimCombatMode)root.GetProperty("Mode").GetInt32(),
                    root.GetProperty("ContentHash").Deserialize<byte[]>(options),
                    players, entities,
                    root.GetProperty("InputDeadlineTicks").GetInt32());
            }

            public override void Write(Utf8JsonWriter writer,
                ReplayBattleInit value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber(nameof(value.ProtocolVersion), value.ProtocolVersion);
                writer.WriteNumber(nameof(value.SchemaVersion), value.SchemaVersion);
                writer.WriteString(nameof(value.MatchId), value.MatchId);
                writer.WriteString(nameof(value.SessionId), value.SessionId);
                writer.WriteNumber(nameof(value.Seed), value.Seed);
                writer.WriteNumber(nameof(value.TickRate), value.TickRate);
                writer.WriteNumber(nameof(value.Mode), (int)value.Mode);
                writer.WritePropertyName(nameof(value.ContentHash));
                JsonSerializer.Serialize(writer, value.ContentHash, options);
                writer.WritePropertyName(nameof(value.Players));
                writer.WriteStartArray();
                foreach (SimPlayerIdentity player in value.Players)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber(nameof(player.PlayerId), player.PlayerId);
                    writer.WriteString(nameof(player.AccountId), player.AccountId);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WritePropertyName(nameof(value.InitialEntities));
                writer.WriteStartArray();
                foreach (InitialEntitySpec entity in value.InitialEntities)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber(nameof(entity.ArchetypeId), entity.ArchetypeId);
                    writer.WriteNumber(nameof(entity.GridX), entity.GridX);
                    writer.WriteNumber(nameof(entity.GridY), entity.GridY);
                    writer.WriteNumber(nameof(entity.TeamId), entity.TeamId);
                    writer.WriteNumber(nameof(entity.OwnerPlayerId), entity.OwnerPlayerId);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteNumber(nameof(value.InputDeadlineTicks),
                    value.InputDeadlineTicks);
                writer.WriteEndObject();
            }

            private static SimPlayerIdentity[] ReadPlayers(JsonElement array)
            {
                var players = new List<SimPlayerIdentity>();
                foreach (JsonElement player in array.EnumerateArray())
                    players.Add(new SimPlayerIdentity(
                        player.GetProperty("PlayerId").GetInt32(),
                        player.GetProperty("AccountId").GetString()));
                return players.ToArray();
            }

            private static InitialEntitySpec[] ReadEntities(JsonElement array)
            {
                var entities = new List<InitialEntitySpec>();
                foreach (JsonElement entity in array.EnumerateArray())
                    entities.Add(new InitialEntitySpec(
                        entity.GetProperty("ArchetypeId").GetInt32(),
                        entity.GetProperty("GridX").GetInt32(),
                        entity.GetProperty("GridY").GetInt32(),
                        entity.GetProperty("TeamId").GetInt32(),
                        entity.GetProperty("OwnerPlayerId").GetInt32()));
                return entities.ToArray();
            }
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
