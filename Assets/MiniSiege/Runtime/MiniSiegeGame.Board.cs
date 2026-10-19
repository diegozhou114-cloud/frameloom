using UnityEngine;
using ZWebGLFrameWork.SimulationCore.MiniSiege;

namespace ZWebGLFrameWork.MiniSiege
{
    public sealed partial class MiniSiegeGame
    {
        private void DrawBoard()
        {
            Color grass = new Color32(42, 66, 62, 255);
            Color road = new Color32(99, 103, 84, 255);
            Block(32, 232, 1036, 294, grass);
            for (int row = 0; row < 9; row++)
                for (int col = 0; col < 30; col++)
                    if ((row + col) % 3 == 0)
                        Block(42 + col * 34, 246 + row * 30, 8, 2, new Color32(51, 79, 71, 255));
            Block(533, 232, 34, 294, new Color32(44, 85, 95, 255));
            for (int lane = 0; lane < 2; lane++)
            {
                float y = LaneY(lane);
                Block(107, y - 32, 886, 64, road);
                Block(514, y - 35, 72, 70, new Color32(146, 139, 107, 255));
                Block(514, y - 35, 72, 4, Paper);
                Block(514, y + 31, 72, 4, Paper);
                for (int i = 0; i < 25; i++) Block(126 + i * 34, y, 12, 2, new Color32(139, 140, 110, 255));
                Text(147, y - 63, 180, 23, lane == 0 ? "上路" : "下路", _small,
                    _lane == lane ? Gold : Muted);
                if (_lane == lane && _player >= 0)
                {
                    float x = _player == 0 ? 107 : 938;
                    Block(x, y - 32, 55, 64, new Color(1, 1, 1, 0.07f));
                    Text(x, y - 18, 55, 34, _player == 0 ? ">>" : "<<", _heading,
                        _player == 0 ? Blue : Red, TextAnchor.MiddleCenter);
                }
                if (GUI.Button(new Rect(115, y - 33, 870, 67), GUIContent.none, GUIStyle.none)) _lane = lane;
            }
            Text(148, 236, 796, 22, _simulation?.LastAction ?? "选择路线，部署护卫，再用弓手支援",
                _small, Paper, TextAnchor.MiddleCenter);
            Castle(80, 375, Blue, _simulation?.BaseHealth(0) ?? 300);
            Castle(1020, 375, Red, _simulation?.BaseHealth(1) ?? 300);
            foreach (SiegeUnitView unit in _units) if (unit.Kind != 0) DrawUnit(unit);
            if (Time.unscaledTime < _pulseUntil)
            {
                float y = LaneY(_pulseLane);
                Color tint = _pulsePlayer == 0 ? Blue : Red;
                tint.a = (_pulseUntil - Time.unscaledTime) * 0.5f;
                Block(MapX(10), y - 36, MapX(20) - MapX(10), 72, tint);
                Text(440, y - 15, 220, 30, "冲击", _heading, Gold, TextAnchor.MiddleCenter);
            }
            Text(152, 488, 796, 24, "中场冲击区域  /  x10-20", _small, Muted, TextAnchor.MiddleCenter);
            if (_simulation == null || _simulation.Winner >= 0 || _desync || _sessionStopped)
                DrawOverlay();
        }

        private void DrawUnit(SiegeUnitView unit)
        {
            float x = MapX(_visualX.TryGetValue(unit.Id, out float rendered) ? rendered : unit.X.ToFloat());
            float y = LaneY(unit.Lane) + (unit.Id % 3 - 1) * 12;
            Color color = unit.Player == 0 ? Blue : Red;
            Dot(x + 2, y + 5, unit.Kind == 1 ? 27 : 22, new Color(0, 0, 0, 0.25f));
            if (unit.Kind == 1)
            {
                Block(x - 10, y - 12, 20, 23, color);
                Block(x - 6, y - 7, 12, 10, Ink);
                Block(x + (unit.Player == 0 ? 10 : -14), y - 10, 4, 25, Paper);
            }
            else
            {
                Dot(x, y, 21, color);
                Text(x - 12, y - 16, 24, 29, unit.Player == 0 ? ">" : "<", _body, Ink, TextAnchor.MiddleCenter);
            }
            Block(x - 12, y - 20, 24, 3, Ink);
            Block(x - 12, y - 20, 24f * unit.HP / unit.MaxHP, 3, color);
            if (_simulation.Tick - unit.LastAttackTick < 3)
            {
                float direction = unit.Player == 0 ? 1 : -1;
                Block(x + direction * 16, y - 2, unit.Kind == 1 ? 8 : 32, 2, Gold);
            }
        }

        private void Castle(float x, float y, Color color, int hp)
        {
            if (hp <= 0) color = Muted;
            Block(x - 30, y - 41, 60, 83, Ink);
            Block(x - 27, y - 38, 54, 76, color);
            Block(x - 30, y - 52, 12, 21, color);
            Block(x - 6, y - 52, 12, 21, color);
            Block(x + 18, y - 52, 12, 21, color);
            Block(x - 9, y + 10, 18, 28, Ink);
            Block(x - 20, y - 19, 10, 14, Ink);
            Block(x + 10, y - 19, 10, 14, Ink);
            Block(x, y - 80, 3, 30, Paper);
            Block(x + 3, y - 80, 22, 14, color);
            Text(x - 43, y + 48, 86, 24, hp.ToString(), _heading, color, TextAnchor.MiddleCenter);
        }

        private void DrawOverlay()
        {
            Block(284, 310, 532, 142, new Color32(17, 28, 41, 242));
            string title = _desync ? "同步已停止" : _sessionStopped ? "对局中断" : "准备好对战了吗？";
            string detail = _status;
            if (!_sessionStopped && !_desync && _simulation != null && _simulation.Winner >= 0)
            {
                title = _simulation.Winner == 2 ? "平局" : _simulation.Winner == _player ? "胜利" : "失败";
                detail = (_simulation.Winner == 2 ? "双方城堡都守住了。" :
                    (_simulation.Winner == 0 ? "蓝方" : "红方") + " 赢得了攻城战。") + " 请在两个窗口重新连接再来一局。";
            }
            Text(308, 326, 484, 42, title, _heading, Gold, TextAnchor.MiddleCenter);
            Text(305, 375, 490, 48, detail, new GUIStyle(_body)
                { wordWrap = true, alignment = TextAnchor.MiddleCenter }, Paper, TextAnchor.MiddleCenter);
        }

        private static float MapX(float x) => 80 + x / 30f * 940;
        private static float LaneY(int lane) => lane == 0 ? 320 : 432;

        private void UpdateVisuals()
        {
            float blend = 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime);
            foreach (SiegeUnitView unit in _units)
            {
                float target = unit.X.ToFloat();
                _visualX[unit.Id] = _visualX.TryGetValue(unit.Id, out float previous)
                    ? Mathf.Lerp(previous, target, blend) : target;
            }
        }
    }
}
