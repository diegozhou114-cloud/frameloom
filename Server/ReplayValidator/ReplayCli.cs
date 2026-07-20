using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public static class ReplayCli
    {
        public static async Task<int> RunAsync(
            string[] args, TextWriter stdout, TextWriter stderr,
            CancellationToken cancellationToken)
        {
            if (stdout == null) throw new ArgumentNullException(nameof(stdout));
            if (stderr == null) throw new ArgumentNullException(nameof(stderr));
            if (!TryParse(args, out CliArguments parsed))
                return Write(stdout, 1, "io", null,
                    "Invalid command arguments.");

            try
            {
                string limitsJson = await File.ReadAllTextAsync(
                    parsed.LimitsPath, cancellationToken).ConfigureAwait(false);
                ReplayValidationLimits limits =
                    ReplayLimitsJson.DeserializeApproved(limitsJson);
                ReplayInputText input;
                using (FileStream replayStream = File.OpenRead(parsed.ReplayPath))
                    input = ReplayInput.ReadUtf8(replayStream, limits.MaxFileBytes);

                ReplayFile replay;
                try
                {
                    replay = ServerReplayJsonCodec.Deserialize(input.Text);
                }
                catch (Exception exception) when (
                    exception is JsonException ||
                    exception is ArgumentException)
                {
                    return Write(stdout, 2, "invalid-replay",
                        new ReplayValidationResult(
                            ReplayValidationCode.MalformedJson), exception.Message);
                }

                AuthoritativeMatchRecord match;
                try
                {
                    match = await new JsonReplayMatchProvider().LoadAsync(
                        parsed.MatchPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is JsonException ||
                    exception is IOException ||
                    exception is UnauthorizedAccessException)
                {
                    return Write(stdout, 4, "authority", null, exception.Message);
                }

                var validator = new ReplayValidator(
                    new SimBattleSessionFactory(), limits,
                    new UnlimitedReplayValidationBudget());
                ReplayValidationResult result = await validator.ValidateAsync(
                    replay, match, parsed.Submitter, input.ByteCount,
                    cancellationToken).ConfigureAwait(false);
                int exitCode = ReplayCliExitCodes.For(result.Code);
                return Write(stdout, exitCode, Category(exitCode), result,
                    result.IsValid ? "Replay is valid." : "Replay was rejected.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ReplayInputLimitException exception)
            {
                return Write(stdout, 5, "resource-limit",
                    new ReplayValidationResult(ReplayValidationCode.FileTooLarge),
                    exception.Message);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException ||
                exception is ArgumentException)
            {
                return Write(stdout, 1, "io", null, exception.Message);
            }
            catch (Exception exception)
            {
                await stderr.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
                return Write(stdout, 1, "internal", null,
                    "Replay validation failed internally.");
            }
        }

        private static int Write(TextWriter stdout, int exitCode,
            string category, ReplayValidationResult result, string message)
        {
            string json = JsonSerializer.Serialize(new
            {
                isValid = exitCode == 0,
                exitCode,
                category,
                code = (result?.Code ?? ReplayValidationCode.MalformedJson).ToString(),
                failedTick = result?.FailedTick ?? -1,
                expectedChecksum = result?.ExpectedChecksum ?? 0,
                actualChecksum = result?.ActualChecksum ?? 0,
                finalResult = result?.FinalResult,
                message
            });
            stdout.WriteLine(json);
            return exitCode;
        }

        private static string Category(int exitCode)
        {
            switch (exitCode)
            {
                case 0: return "valid";
                case 2: return "invalid-replay";
                case 3: return "unsupported";
                case 4: return "authority";
                case 5: return "resource-limit";
                default: return "internal";
            }
        }

        private static bool TryParse(string[] args, out CliArguments parsed)
        {
            parsed = null;
            if (args == null || args.Length != 9 || args[0] != "validate")
                return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 1; index < args.Length; index += 2)
            {
                if (!values.TryAdd(args[index], args[index + 1])) return false;
            }
            if (!values.TryGetValue("--replay", out string replay) ||
                !values.TryGetValue("--match", out string match) ||
                !values.TryGetValue("--limits", out string limits) ||
                !values.TryGetValue("--submitter", out string submitter) ||
                values.Count != 4 ||
                string.IsNullOrWhiteSpace(replay) ||
                string.IsNullOrWhiteSpace(match) ||
                string.IsNullOrWhiteSpace(limits) ||
                string.IsNullOrWhiteSpace(submitter))
                return false;
            parsed = new CliArguments(replay, match, limits, submitter);
            return true;
        }

        private sealed class CliArguments
        {
            public string ReplayPath { get; }
            public string MatchPath { get; }
            public string LimitsPath { get; }
            public string Submitter { get; }

            public CliArguments(string replayPath, string matchPath,
                string limitsPath, string submitter)
            {
                ReplayPath = replayPath;
                MatchPath = matchPath;
                LimitsPath = limitsPath;
                Submitter = submitter;
            }
        }
    }
}
