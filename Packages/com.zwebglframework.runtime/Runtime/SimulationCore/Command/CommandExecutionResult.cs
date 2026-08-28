namespace ZWebGLFrameWork.SimulationCore
{
    public readonly struct CommandExecutionResult
    {
        public ISimCommand Command { get; }
        public CommandDisposition Disposition { get; }
        public int EntityId { get; }
        public int EntityGeneration { get; }

        public CommandExecutionResult(ISimCommand command, CommandDisposition disposition,
            int entityId, int entityGeneration)
        {
            Command = command;
            Disposition = disposition;
            EntityId = entityId;
            EntityGeneration = entityGeneration;
        }
    }
}
