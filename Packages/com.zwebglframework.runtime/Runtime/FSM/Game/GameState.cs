namespace ZWebGLFrameWork.FSM
{
    /// <summary>游戏全局状态</summary>
    public enum GameState
    {
        /// <summary>启动初始化 — 资源检查、SDK初始化</summary>
        Launch,

        /// <summary>登录 — 账号认证、服务器选择</summary>
        Login,

        /// <summary>主城 — 建造管理、部队训练、社交</summary>
        MainVillage,

        /// <summary>战斗 — 攻击/防御、单位控制</summary>
        Battle,

        /// <summary>加载 — 场景/资源异步加载过渡</summary>
        Loading,

        /// <summary>暂停 — 游戏暂停（手动或切后台）</summary>
        Pause,
    }
}
