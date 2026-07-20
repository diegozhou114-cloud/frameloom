using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public sealed class JsonReplayMatchProvider : IReplayMatchProvider
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        public async Task<AuthoritativeMatchRecord> LoadAsync(
            string path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Match path is required.", nameof(path));
            string json = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                StrictJsonDocument.ValidateMatchShape(json);
                AuthoritativeMatchRecord match =
                    JsonSerializer.Deserialize<AuthoritativeMatchRecord>(json, Options);
                ValidateRequired(match);
                return match;
            }
            catch (JsonException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                throw new JsonException("Authoritative match JSON is invalid.", exception);
            }
        }

        public static string Serialize(AuthoritativeMatchRecord match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            return JsonSerializer.Serialize(match, Options);
        }

        private static void ValidateRequired(AuthoritativeMatchRecord match)
        {
            if (match == null || string.IsNullOrWhiteSpace(match.MatchId) ||
                string.IsNullOrWhiteSpace(match.InitialStateHash) ||
                string.IsNullOrWhiteSpace(match.SubmissionNonce) ||
                string.IsNullOrWhiteSpace(match.SimulationSchemaHash) ||
                string.IsNullOrWhiteSpace(match.ContentConfigHash) ||
                match.CanonicalManifestBytes == null ||
                match.BattleInit == null || match.InitialSnapshot == null ||
                match.ExpectedResult == null ||
                match.AllowedSubmitterPlayerIds == null)
                throw new JsonException(
                    "Authoritative match JSON is missing a required field.");
        }

        private static JsonSerializerOptions CreateOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                WriteIndented = false
            };
            options.Converters.Add(new SimPlayerIdentityConverter());
            options.Converters.Add(new InitialEntitySpecConverter());
            return options;
        }

        private sealed class SimPlayerIdentityConverter :
            JsonConverter<SimPlayerIdentity>
        {
            public override SimPlayerIdentity Read(ref Utf8JsonReader reader,
                Type typeToConvert, JsonSerializerOptions options)
            {
                using JsonDocument document = JsonDocument.ParseValue(ref reader);
                JsonElement root = document.RootElement;
                RequireExactProperties(root, "PlayerId", "AccountId");
                return new SimPlayerIdentity(
                    root.GetProperty("PlayerId").GetInt32(),
                    root.GetProperty("AccountId").GetString());
            }

            public override void Write(Utf8JsonWriter writer,
                SimPlayerIdentity value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber("PlayerId", value.PlayerId);
                writer.WriteString("AccountId", value.AccountId);
                writer.WriteEndObject();
            }
        }

        private sealed class InitialEntitySpecConverter :
            JsonConverter<InitialEntitySpec>
        {
            public override InitialEntitySpec Read(ref Utf8JsonReader reader,
                Type typeToConvert, JsonSerializerOptions options)
            {
                using JsonDocument document = JsonDocument.ParseValue(ref reader);
                JsonElement root = document.RootElement;
                RequireExactProperties(root, "ArchetypeId", "GridX", "GridY",
                    "TeamId", "OwnerPlayerId");
                return new InitialEntitySpec(
                    root.GetProperty("ArchetypeId").GetInt32(),
                    root.GetProperty("GridX").GetInt32(),
                    root.GetProperty("GridY").GetInt32(),
                    root.GetProperty("TeamId").GetInt32(),
                    root.GetProperty("OwnerPlayerId").GetInt32());
            }

            public override void Write(Utf8JsonWriter writer,
                InitialEntitySpec value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber("ArchetypeId", value.ArchetypeId);
                writer.WriteNumber("GridX", value.GridX);
                writer.WriteNumber("GridY", value.GridY);
                writer.WriteNumber("TeamId", value.TeamId);
                writer.WriteNumber("OwnerPlayerId", value.OwnerPlayerId);
                writer.WriteEndObject();
            }
        }

        private static void RequireExactProperties(
            JsonElement element, params string[] required)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected a JSON object.");
            var seen = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name) ||
                    Array.IndexOf(required, property.Name) < 0)
                    throw new JsonException(
                        "JSON object contains an unknown or duplicate property.");
            }
            if (seen.Count != required.Length)
                throw new JsonException("JSON object is missing a required property.");
        }
    }
}
