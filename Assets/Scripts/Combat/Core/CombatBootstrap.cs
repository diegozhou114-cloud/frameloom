using Cysharp.Threading.Tasks;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗引导器 — 组装 ECS 系统和 SystemGroup。
    /// 负责系统创建和 InitAsync，与运行时 Orchestrator 分离。
    /// </summary>
    public static class CombatBootstrap
    {
        /// <summary>创建并初始化 ECS 管线</summary>
        public static async UniTask<SystemGroup> BuildAsync(World world)
        {
            var systems = new SystemGroup();
            var deathSys = new DeathSystem();

            systems.Add(
                new MovementSystem(),
                new AttackSystem(),
                new DamageSystem(),
                deathSys
            );

            await systems.InitAllAsync(world);
            return systems;
        }
    }
}
