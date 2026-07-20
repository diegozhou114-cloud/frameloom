using System;
using System.Collections.Generic;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public sealed class ReplayStructuralValidator
    {
        private readonly ReplayValidationLimits _limits;
        private readonly KnownCommandSchema _commandSchema;

        public ReplayStructuralValidator(
            ReplayValidationLimits limits,
            KnownCommandSchema commandSchema)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _commandSchema = commandSchema ??
                throw new ArgumentNullException(nameof(commandSchema));
        }

        public ReplayValidationResult Validate(ReplayFile replay, int serializedBytes)
        {
            if (replay == null || replay.Records == null)
                return Reject(ReplayValidationCode.NullReplay);
            if (serializedBytes <= 0 || serializedBytes > _limits.MaxFileBytes)
                return Reject(ReplayValidationCode.FileTooLarge);
            if (replay.Records.Count == 0)
                return Reject(ReplayValidationCode.Empty);
            if (replay.FormatVersion != ReplayFormat.CurrentVersion)
                return Reject(ReplayValidationCode.UnsupportedVersion);
            if (!HashText.IsSha256(replay.SimulationSchemaHash))
                return Reject(ReplayValidationCode.SchemaMismatch);
            if (!HashText.IsSha256(replay.ContentConfigHash))
                return Reject(ReplayValidationCode.ConfigMismatch);
            if (!HashText.IsSha256(replay.InitialStateHash) ||
                string.IsNullOrWhiteSpace(replay.MatchId) ||
                string.IsNullOrWhiteSpace(replay.SubmissionNonce))
                return Reject(ReplayValidationCode.InitialStateMismatch);
            if (replay.TickRate != ReplayFormat.TickRate)
                return Reject(ReplayValidationCode.TickRateMismatch);
            if (replay.Records.Count > _limits.MaxTicks)
                return Reject(ReplayValidationCode.TooManyTicks);
            if (replay.FinalTick < 0 || replay.FinalTick >= _limits.MaxTicks)
                return Reject(ReplayValidationCode.FinalTickMismatch);
            if (replay.Records.Count <= replay.FinalTick)
                return Reject(ReplayValidationCode.Truncated);
            if (replay.Records.Count > replay.FinalTick + 1)
                return Reject(ReplayValidationCode.TrailingRecord);

            ReplayValidationResult orderError = ValidateTickOrder(replay.Records);
            if (orderError != null) return orderError;

            int totalCommands = 0;
            var lastSequenceByPlayer = new Dictionary<int, int>();
            for (int index = 0; index < replay.Records.Count; index++)
            {
                ReplayTickRecord record = replay.Records[index];
                if (record.Tick != index)
                    return Reject(ReplayValidationCode.TickGap, index);
                if (record.Commands == null ||
                    record.Commands.Count > _limits.MaxCommandsPerTick)
                    return Reject(ReplayValidationCode.TooManyCommands, index);

                if (record.Commands.Count > _limits.MaxTotalCommands - totalCommands)
                    return Reject(ReplayValidationCode.TooManyCommands, index);
                totalCommands += record.Commands.Count;

                foreach (SerializableCommand command in record.Commands)
                {
                    ReplayValidationCode commandError =
                        _commandSchema.Validate(command, record.Tick);
                    if (commandError != ReplayValidationCode.Valid)
                        return Reject(commandError, index);
                    if (lastSequenceByPlayer.TryGetValue(
                            command.PlayerId, out int previousSequence) &&
                        command.Sequence <= previousSequence)
                        return Reject(ReplayValidationCode.InvalidCommand, index);
                    lastSequenceByPlayer[command.PlayerId] = command.Sequence;
                }
            }

            return ReplayValidationResult.StructurallyValid();
        }

        private static ReplayValidationResult ValidateTickOrder(
            IReadOnlyList<ReplayTickRecord> records)
        {
            int previousTick = -1;
            for (int index = 0; index < records.Count; index++)
            {
                ReplayTickRecord record = records[index];
                if (record == null)
                    return Reject(ReplayValidationCode.MalformedJson, index);
                if (index > 0 && record.Tick <= previousTick)
                    return Reject(
                        ReplayValidationCode.DuplicateOrUnorderedTick, index);
                previousTick = record.Tick;
            }
            return null;
        }

        private static ReplayValidationResult Reject(
            ReplayValidationCode code, int failedTick = -1) =>
            new ReplayValidationResult(code, failedTick);
    }
}
