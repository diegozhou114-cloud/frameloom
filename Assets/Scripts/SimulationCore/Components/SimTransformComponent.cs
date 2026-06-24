namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>位置组件 — 逻辑坐标（无 Unity Vector3）</summary>
    public struct SimTransformComponent : ISimComponent
    {
        public LogicInt PosX, PosY;
        public int GridX, GridY;

        public static SimTransformComponent Create(int gridX, int gridY, LogicInt cellSize)
        {
            return new SimTransformComponent
            {
                GridX = gridX,
                GridY = gridY,
                PosX = LogicInt.FromInt(gridX) * cellSize + cellSize / LogicInt.FromInt(2),
                PosY = LogicInt.FromInt(gridY) * cellSize + cellSize / LogicInt.FromInt(2),
            };
        }
    }
}
