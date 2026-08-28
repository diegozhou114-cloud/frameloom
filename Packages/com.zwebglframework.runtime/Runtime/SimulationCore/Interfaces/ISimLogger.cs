namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真日志接口 — SimulationCore 对宿主环境的唯一日志依赖。
    /// Unity 侧由 DebugLogger 实现（适配 Debug.Log），服务器侧由 ConsoleLogger 实现。
    /// </summary>
    public interface ISimLogger
    {
        void Log(string message);
        void Warn(string message);
        void Error(string message);
    }
}
