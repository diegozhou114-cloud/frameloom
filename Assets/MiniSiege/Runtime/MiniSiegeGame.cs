using System;
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.SimulationCore.MiniSiege;

namespace ZWebGLFrameWork.MiniSiege
{
    /// <summary>Standalone demo entry: two clients replay the host's ordered player inputs.</summary>
    public sealed partial class MiniSiegeGame : MonoBehaviour
    {
        private SiegeConnection _connection;
        private SiegeSimulation _simulation;
        private readonly List<SiegeUnitView> _units = new List<SiegeUnitView>();
        private readonly Dictionary<int, float> _visualX = new Dictionary<int, float>();
        private string _url = "ws://127.0.0.1:8787/siege";
        private string _status = "连接两个窗口开始对战";
        private string _match;
        private int _player = -1, _lane, _sequence, _verifiedFrames;
        private bool _desync, _auto, _sessionStopped;
        private float _nextBot, _pulseUntil, _lastFrameTime;
        private int _pulseLane, _pulsePlayer;

        private void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            gameObject.name = "MiniSiege";
            _url = Argument("-siegeUrl", _url);
            _auto = HasArgument("-siegeAuto");
            PrepareStyles();
            if (HasArgument("-siegeConnect") || _auto) Connect();
        }

        private void Connect()
        {
            _connection?.Dispose();
            _simulation = null; _player = -1; _sequence = 0;
            _verifiedFrames = 0; _desync = false; _sessionStopped = false;
            _units.Clear(); _visualX.Clear();
            _status = "正在连接……";
            _connection = new SiegeConnection();
            _connection.Message += OnPacket;
            _connection.Error += error => { _status = error; _sessionStopped = true; };
            _connection.Connect(_url, gameObject.name);
        }

        private void Update()
        {
            _connection?.Pump();
            UpdateVisuals();
            if (!Playing) return;
            if (Input.GetKeyDown(KeyCode.Q)) _lane = 0;
            if (Input.GetKeyDown(KeyCode.E)) _lane = 1;
            if (Input.GetKeyDown(KeyCode.Alpha1)) Deploy(1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Deploy(2);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Deploy(3);
            if (_auto && Time.unscaledTime >= _nextBot)
            {
                _nextBot = Time.unscaledTime + 0.65f;
                _lane = (_simulation.Tick / 160 + _player) % 2;
                int kind = (_sequence % 3 == 2) ? 2 : 1;
                if (_simulation.Energy(_player) >= SiegeSimulation.Cost(kind)) Deploy(kind);
            }
            CaptureEvidence();
        }

        private bool Playing => _simulation != null && _simulation.Winner < 0 &&
            !_desync && !_sessionStopped && _connection != null && _connection.Connected;

        private void OnPacket(string json)
        {
            try
            {
                SiegePacket packet = JsonUtility.FromJson<SiegePacket>(json);
                if (packet.Kind == "welcome")
                {
                    _player = packet.Player; _match = packet.MatchId;
                    _status = "等待对手，请在第二个窗口连接";
                }
                else if (packet.Kind == "start")
                {
                    _simulation = new SiegeSimulation(packet.Seed);
                    _verifiedFrames = 0; _status = "战斗开始，摧毁敌方城堡！";
                    _simulation.CopyUnits(_units);
                    BeginEvidence();
                }
                else if (packet.Kind == "frame" && !_desync && _simulation != null)
                {
                    if (packet.MatchId != _match || packet.Tick != _simulation.Tick)
                        throw new InvalidOperationException("帧顺序错误，模拟已停止");
                    _simulation.Step(packet.Inputs);
                    string actual = _simulation.Checksum.ToString("X16");
                    if (!string.Equals(actual, packet.Checksum, StringComparison.Ordinal))
                        throw new InvalidOperationException("第 " + packet.Tick + ": " + actual + " != " + packet.Checksum);
                    _verifiedFrames++;
                    _lastFrameTime = Time.unscaledTime;
                    foreach (SiegeInput input in packet.Inputs)
                        if (input.Unit == 3 && _simulation.SkillRemaining(input.Player) == SiegeSimulation.SkillCooldownTicks - 1)
                        { _pulseLane = input.Lane; _pulsePlayer = input.Player; _pulseUntil = Time.unscaledTime + 0.5f; }
                    _simulation.CopyUnits(_units);
                    if (packet.Tick % 20 == 0)
                        Send(new SiegePacket { Kind = "checksum", Tick = packet.Tick, Checksum = actual });
                    RecordEvidence(packet.Tick, actual);
                    if (_simulation.Winner >= 0) FinishEvidence();
                }
                else if (packet.Kind == "left" || packet.Kind == "error")
                { _status = packet.Message; _sessionStopped = true; }
                else if (packet.Kind == "rejected") _status = packet.Message;
            }
            catch (Exception exception)
            {
                _desync = true; _status = exception.Message;
                Debug.LogError("[MiniSiege] " + exception.Message);
            }
        }

        private void Deploy(int kind)
        {
            if (!Playing || _simulation.Energy(_player) < SiegeSimulation.Cost(kind) ||
                (kind == 3 && _simulation.SkillRemaining(_player) > 0)) return;
            Send(new SiegePacket { Kind = "input", Input = new SiegeInput
                { Player = _player, Sequence = ++_sequence, Unit = kind, Lane = _lane } });
        }

        private void Send(SiegePacket packet) => _connection?.Send(JsonUtility.ToJson(packet));
        public void OnSiegeMessage(string message) => _connection?.ReceiveWebMessage(message);
        public void OnSiegeError(string message) => _connection?.ReceiveWebError(message);
        public void OnSiegeOpen(string unused) => _connection?.ReceiveWebOpen();
        private void OnDestroy() { _connection?.Dispose(); _evidence?.Dispose(); }
        private static bool HasArgument(string key) => Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;
        private static string Argument(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
    }
}
