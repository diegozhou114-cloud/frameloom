using System;

namespace ZWebGLFrameWork.SimulationCore.MiniSiege
{
    /// <summary>Only player intent crosses the wire; positions and health stay in the simulation.</summary>
    [Serializable]
    public sealed class SiegeInput
    {
        public int Player;
        public int Sequence;
        public int Unit;
        public int Lane;
    }

    /// <summary>Small JSON demo protocol shared by Unity and the standalone host.</summary>
    [Serializable]
    public sealed class SiegePacket
    {
        public string Kind;
        public string MatchId;
        public string Checksum;
        public string Message;
        public int Player;
        public int Tick;
        public int Seed;
        public SiegeInput[] Inputs;
        public SiegeInput Input;
    }

    /// <summary>Immutable copy consumed by the visual layer.</summary>
    public readonly struct SiegeUnitView
    {
        public readonly int Id, Player, Kind, Lane, HP, MaxHP, LastAttackTick;
        public readonly LogicInt X;

        public SiegeUnitView(int id, int player, int kind, int lane, int hp,
            int maxHP, int lastAttackTick, LogicInt x)
        {
            Id = id; Player = player; Kind = kind; Lane = lane; HP = hp;
            MaxHP = maxHP; LastAttackTick = lastAttackTick; X = x;
        }
    }
}
