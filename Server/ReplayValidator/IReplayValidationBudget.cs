namespace ZWebGLFrameWork.Server
{
    public interface IReplayValidationBudget
    {
        bool TryConsumeTick(int commandCount);
    }

    public sealed class UnlimitedReplayValidationBudget : IReplayValidationBudget
    {
        public bool TryConsumeTick(int commandCount) => true;
    }
}
