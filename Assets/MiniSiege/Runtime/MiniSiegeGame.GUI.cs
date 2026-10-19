using UnityEngine;
using ZWebGLFrameWork.SimulationCore.MiniSiege;

namespace ZWebGLFrameWork.MiniSiege
{
    public sealed partial class MiniSiegeGame
    {
        private void OnGUI()
        {
            EnsureStyles();
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1100f, Screen.height / 760f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1100 * scale) / 2,
                (Screen.height - 760 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
            Block(0, 0, 1100, 760, Ink);
            Block(32, 28, 5, 48, Gold);
            Text(50, 25, 400, 46, "迷你攻城", _title);
            Text(52, 72, 450, 20, "两条路线，一座城堡，三分钟决胜", _small);
            Text(620, 29, 448, 20, "本地对战  /  " + (_player < 0 ? "未连接" :
                (_player == 0 ? "你是蓝方" : "你是红方")), _small,
                _player < 0 ? Muted : _player == 0 ? Blue : Red, TextAnchor.MiddleRight);
            _url = GUI.TextField(new Rect(620, 58, 302, 30), _url, _field);
            if (Button(new Rect(932, 58, 136, 30), _connection == null ? "连接" : "重新连接", Gold)) Connect();

            DrawScore();
            DrawBoard();
            DrawControls();
            DrawFooter();
            GUI.matrix = previous;
        }

        private void DrawScore()
        {
            int blueHP = _simulation?.BaseHealth(0) ?? SiegeSimulation.BaseHP;
            int redHP = _simulation?.BaseHealth(1) ?? SiegeSimulation.BaseHP;
            int seconds = _simulation == null ? 180 : Mathf.Max(0,
                (SiegeSimulation.DurationTicks - _simulation.Tick + 19) / SiegeSimulation.TickRate);
            BaseCard(32, "蓝方城堡", blueHP, Blue, _player == 0);
            BaseCard(710, "红方城堡", redHP, Red, _player == 1);
            Text(410, 110, 280, 49, (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"),
                _title, Paper, TextAnchor.MiddleCenter);
            Text(410, 158, 280, 24, _simulation != null && _simulation.Tick >= 1800 ?
                "能量恢复加倍" : "摧毁敌方城堡", _small, Gold, TextAnchor.MiddleCenter);
            Text(410, 188, 280, 22, "时间到时，城堡血量较高的一方获胜", _small, Muted, TextAnchor.MiddleCenter);
        }

        private void BaseCard(float x, string label, int hp, Color color, bool own)
        {
            Block(x, 112, 358, 103, Panel);
            Text(x + 18, 123, 240, 24, label + (own ? "  / 你" : ""), _body, color);
            Text(x + 245, 124, 95, 24, hp + " / 300", _body, Paper, TextAnchor.MiddleRight);
            Block(x + 18, 159, 322, 10, Ink);
            Block(x + 18, 159, 322 * hp / SiegeSimulation.BaseHP, 10, color);
            int energy = _simulation == null ? 8 : _simulation.Energy(x < 400 ? 0 : 1);
            Text(x + 18, 180, 120, 20, "能量  " + energy + "/12", _small, Gold);
            for (int i = 0; i < 12; i++)
                Block(x + 145 + i * 16, 184, 11, 12, i < energy ? Gold : Ink);
        }

        private void DrawControls()
        {
            Text(32, 545, 350, 24, "出兵路线", _small, Muted);
            if (Button(new Rect(150, 542, 155, 32), "[Q] 上路", _lane == 0 ? Gold : Muted)) _lane = 0;
            if (Button(new Rect(313, 542, 155, 32), "[E] 下路", _lane == 1 ? Gold : Muted)) _lane = 1;
            if (Button(new Rect(865, 542, 203, 32), _auto ? "自动出兵：开" : "自动出兵：关",
                _auto ? Gold : Muted)) _auto = !_auto;
            ActionCard(32, 1, "[1] 护卫", "90 生命 / 近战", "带头推进，为弓手抵挡攻击");
            ActionCard(382, 2, "[2] 弓手", "45 生命 / 远程", "跟在护卫身后，提高存活率");
            ActionCard(732, 3, "[3] 冲击", "35 伤害 / 12 秒冷却", "打击中场区域的敌方部队");
        }

        private void ActionCard(float x, int kind, string title, string subtitle, string tip)
        {
            Block(x, 585, 336, 100, Panel);
            int cost = SiegeSimulation.Cost(kind);
            Text(x + 16, 592, 220, 28, title, _heading, kind == 3 ? Gold : Paper);
            Text(x + 16, 623, 235, 22, subtitle, _small);
            Text(x + 16, 652, 310, 22, tip, _small, Paper);
            int cooldown = _player < 0 || _simulation == null ? 0 : _simulation.SkillRemaining(_player);
            bool ready = Playing && _simulation.Energy(_player) >= cost && (kind != 3 || cooldown == 0);
            string label = kind == 3 && cooldown > 0 ? ((cooldown + 19) / 20) + "s" : cost + " E";
            if (Button(new Rect(x + 250, 598, 70, 45), label, ready ? Gold : Muted, ready)) Deploy(kind);
        }

        private void DrawFooter()
        {
            Block(32, 704, 1036, 1, Panel);
            bool stale = Playing && Time.unscaledTime - _lastFrameTime > 2;
            string state = _desync ? "不同步 / 已停止" : _sessionStopped ? "对局已中断" : stale ? "等待帧" :
                _verifiedFrames > 0 ? "同步已验证" : "等待对局";
            Dot(41, 731, 8, _desync ? Red : stale ? Gold : Blue);
            Text(54, 714, 245, 29, state, _small, _desync ? Red : Blue);
            string checksum = _simulation == null ? "----------------" : _simulation.Checksum.ToString("X16");
            Text(293, 714, 460, 29, "帧 " + (_simulation?.Tick ?? 0) + "  /  " +
                _verifiedFrames + " 次校验  /  " + checksum, _small);
            Text(770, 714, 298, 29, "逻辑 20 帧/秒  |  画面 60 帧/秒", _small, Muted, TextAnchor.MiddleRight);
        }
    }
}
