using System;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public sealed class KnownCommandSchema
    {
        public static readonly KnownCommandSchema Version1 = new KnownCommandSchema();

        private KnownCommandSchema()
        {
        }

        internal ReplayValidationCode Validate(SerializableCommand command, int tick)
        {
            if (command == null || command.Tick != tick ||
                (command.PlayerId != 0 && command.PlayerId != 1) ||
                command.Sequence < 0 || command.Sequence == int.MaxValue)
                return ReplayValidationCode.InvalidCommand;

            if (!Enum.TryParse(command.Type, false, out SimCommandType type) ||
                !Enum.IsDefined(typeof(SimCommandType), type) ||
                !string.Equals(command.Type, type.ToString(), StringComparison.Ordinal) ||
                type == SimCommandType.None)
                return ReplayValidationCode.UnknownCommand;

            switch (type)
            {
                case SimCommandType.Deploy:
                    return command.UnitTypeId > 0 &&
                        command.GridX >= 0 && command.GridY >= 0 &&
                        command.EntityId == 0 && command.CasterId == 0 &&
                        command.SkillId == 0 && command.TargetX == 0 &&
                        command.TargetY == 0
                            ? ReplayValidationCode.Valid
                            : ReplayValidationCode.InvalidCommand;
                case SimCommandType.Move:
                    return command.EntityId >= 0 &&
                        command.TargetX >= 0 && command.TargetY >= 0 &&
                        command.UnitTypeId == 0 && command.CasterId == 0 &&
                        command.SkillId == 0 && command.GridX == 0 &&
                        command.GridY == 0
                            ? ReplayValidationCode.Valid
                            : ReplayValidationCode.InvalidCommand;
                case SimCommandType.Skill:
                    bool hasNoTarget = command.TargetX == -1 && command.TargetY == -1;
                    bool hasGridTarget = command.TargetX >= 0 && command.TargetY >= 0;
                    return command.CasterId >= 0 && command.SkillId > 0 &&
                        (hasNoTarget || hasGridTarget) &&
                        command.UnitTypeId == 0 && command.EntityId == 0 &&
                        command.GridX == 0 && command.GridY == 0
                            ? ReplayValidationCode.Valid
                            : ReplayValidationCode.InvalidCommand;
                case SimCommandType.EndDeployment:
                    return command.UnitTypeId == 0 && command.EntityId == 0 &&
                        command.CasterId == 0 && command.SkillId == 0 &&
                        command.GridX == 0 && command.GridY == 0 &&
                        command.TargetX == 0 && command.TargetY == 0
                            ? ReplayValidationCode.Valid
                            : ReplayValidationCode.InvalidCommand;
                default:
                    return ReplayValidationCode.UnknownCommand;
            }
        }
    }
}
