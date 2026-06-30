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
            if (world == null)
            {
                Debug.LogError("[CombatBootstrap] BuildAsync: world 参数为 null，无法构建 ECS 管线");
                return null;
            }

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
