using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimArchetypeDefinition
    {
        public int ArchetypeId { get; }
        public string Name { get; }
        public int MaxHealth { get; }
        public long SpeedRaw { get; }
        public int Damage { get; }
        public long RangeRaw { get; }
        public int CooldownTicks { get; }

        public SimArchetypeDefinition(int archetypeId, string name, int maxHealth,
            long speedRaw, int damage, long rangeRaw, int cooldownTicks)
        {
            if (archetypeId <= 0)
                throw new ArgumentOutOfRangeException(nameof(archetypeId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Archetype name is required.", nameof(name));
            if (maxHealth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (speedRaw < 0)
                throw new ArgumentOutOfRangeException(nameof(speedRaw));
            if (damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));
            if (rangeRaw < 0)
                throw new ArgumentOutOfRangeException(nameof(rangeRaw));
            if (cooldownTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(cooldownTicks));
            ArchetypeId = archetypeId;
            Name = name;
            MaxHealth = maxHealth;
            SpeedRaw = speedRaw;
            Damage = damage;
            RangeRaw = rangeRaw;
            CooldownTicks = cooldownTicks;
        }
    }

    public sealed class SimModeProfile
    {
        private readonly int[] _waveTicks;

        public SimCombatMode Mode { get; }
        public bool ManualDeployment { get; }
        public int MaxDeployCount { get; }
        public int DeployDurationTicks { get; }
        public int TimeoutTicks { get; }
        public int[] WaveTicks => (int[])_waveTicks.Clone();

        public SimModeProfile(SimCombatMode mode, bool manualDeployment, int maxDeployCount,
            int deployDurationTicks, int timeoutTicks, int[] waveTicks)
        {
            if (!IsSupported(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (maxDeployCount < 0) throw new ArgumentOutOfRangeException(nameof(maxDeployCount));
            if (deployDurationTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(deployDurationTicks));
            if (timeoutTicks < 0) throw new ArgumentOutOfRangeException(nameof(timeoutTicks));
            if (waveTicks == null) throw new ArgumentNullException(nameof(waveTicks));

            var waves = (int[])waveTicks.Clone();
            for (int index = 0; index < waves.Length; index++)
            {
                if (waves[index] < 0)
                    throw new ArgumentException("Wave ticks cannot be negative.", nameof(waveTicks));
                if (index > 0 && waves[index] <= waves[index - 1])
                    throw new ArgumentException("Wave ticks must be strictly sorted.", nameof(waveTicks));
            }

            Mode = mode;
            ManualDeployment = manualDeployment;
            MaxDeployCount = maxDeployCount;
            DeployDurationTicks = deployDurationTicks;
            TimeoutTicks = timeoutTicks;
            _waveTicks = waves;
        }

        internal int CountWavesAfter(int completedTick)
        {
            for (int index = 0; index < _waveTicks.Length; index++)
                if (_waveTicks[index] > completedTick)
                    return _waveTicks.Length - index;
            return 0;
        }

        internal static bool IsSupported(SimCombatMode mode) =>
            mode == SimCombatMode.VillageAttack
            || mode == SimCombatMode.BuilderBase
            || mode == SimCombatMode.ClanWar
            || mode == SimCombatMode.Campaign;
    }

    public sealed class DeterministicContentManifest
    {
        private const int MagicLength = 4;
        private const int MinimumArchetypeBytes = sizeof(int) * 5 + sizeof(long) * 2;
        private static readonly byte[] Magic = { 0x5A, 0x53, 0x49, 0x4D };
        private readonly SimArchetypeDefinition[] _archetypes;
        private readonly SimModeProfile[] _modes;

        public int SchemaVersion { get; }
        public IReadOnlyList<SimArchetypeDefinition> Archetypes => Array.AsReadOnly((SimArchetypeDefinition[])_archetypes.Clone());
        public IReadOnlyList<SimModeProfile> Modes =>
            Array.AsReadOnly((SimModeProfile[])_modes.Clone());
        public byte[] ContentHash
        {
            get
            {
                using (SHA256 sha256 = SHA256.Create())
                    return sha256.ComputeHash(ToCanonicalBytes());
            }
        }

        public DeterministicContentManifest(int schemaVersion, SimArchetypeDefinition[] archetypes)
            : this(schemaVersion, archetypes, Array.Empty<SimModeProfile>())
        {
        }

        public DeterministicContentManifest(int schemaVersion, SimArchetypeDefinition[] archetypes,
            SimModeProfile[] modes)
        {
            if (schemaVersion != SimComponentSchema.Version)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (archetypes == null) throw new ArgumentNullException(nameof(archetypes));
            if (modes == null) throw new ArgumentNullException(nameof(modes));
            var rows = archetypes.ToArray();
            if (rows.Any(row => row == null)) throw new ArgumentException("Archetype rows cannot be null.", nameof(archetypes));
            if (rows.GroupBy(row => row.ArchetypeId).Any(group => group.Count() > 1))
                throw new ArgumentException("Duplicate archetype ID.", nameof(archetypes));
            Array.Sort(rows, (left, right) => left.ArchetypeId.CompareTo(right.ArchetypeId));

            var modeRows = modes.ToArray();
            if (modeRows.Any(row => row == null))
                throw new ArgumentException("Mode rows cannot be null.", nameof(modes));
            if (modeRows.Any(row => !SimModeProfile.IsSupported(row.Mode)))
                throw new ArgumentException("Unsupported combat mode.", nameof(modes));
            if (modeRows.GroupBy(row => row.Mode).Any(group => group.Count() > 1))
                throw new ArgumentException("Duplicate mode ID.", nameof(modes));
            if (modeRows.Length != 0 && (modeRows.Length != 4
                || !modeRows.Any(row => row.Mode == SimCombatMode.VillageAttack)
                || !modeRows.Any(row => row.Mode == SimCombatMode.BuilderBase)
                || !modeRows.Any(row => row.Mode == SimCombatMode.ClanWar)
                || !modeRows.Any(row => row.Mode == SimCombatMode.Campaign)))
                throw new ArgumentException("A non-empty mode table must contain every supported mode.",
                    nameof(modes));
            Array.Sort(modeRows, (left, right) => ((byte)left.Mode).CompareTo((byte)right.Mode));

            SchemaVersion = schemaVersion;
            _archetypes = rows;
            _modes = modeRows;
        }

        public SimArchetypeDefinition GetArchetype(int archetypeId)
        {
            foreach (var archetype in _archetypes)
                if (archetype.ArchetypeId == archetypeId) return archetype;
            return null;
        }

        public SimModeProfile GetMode(SimCombatMode mode)
        {
            if (!SimModeProfile.IsSupported(mode))
                throw new ArgumentOutOfRangeException(nameof(mode));
            foreach (SimModeProfile profile in _modes)
                if (profile.Mode == mode) return profile;
            return null;
        }

        public byte[] ToCanonicalBytes()
        {
            var writer = new DeterministicBinaryWriter();
            writer.WriteBytes(Magic);
            writer.WriteInt32(SchemaVersion);
            writer.WriteInt32(_archetypes.Length);
            foreach (var archetype in _archetypes)
            {
                writer.WriteInt32(archetype.ArchetypeId);
                writer.WriteUtf8(archetype.Name);
                writer.WriteInt32(archetype.MaxHealth);
                writer.WriteInt64(archetype.SpeedRaw);
                writer.WriteInt32(archetype.Damage);
                writer.WriteInt64(archetype.RangeRaw);
                writer.WriteInt32(archetype.CooldownTicks);
            }
            writer.WriteInt32(_modes.Length);
            foreach (SimModeProfile profile in _modes)
            {
                writer.WriteByte((byte)profile.Mode);
                writer.WriteByte(profile.ManualDeployment ? (byte)1 : (byte)0);
                writer.WriteInt32(profile.MaxDeployCount);
                writer.WriteInt32(profile.DeployDurationTicks);
                writer.WriteInt32(profile.TimeoutTicks);
                int[] waves = profile.WaveTicks;
                writer.WriteInt32(waves.Length);
                foreach (int waveTick in waves) writer.WriteInt32(waveTick);
            }
            return writer.ToArray();
        }

        public static DeterministicContentManifest Parse(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            var reader = new DeterministicBinaryReader(bytes);
            for (int index = 0; index < MagicLength; index++)
                if (reader.ReadByte() != Magic[index]) throw new FormatException("Invalid content manifest magic.");

            int schemaVersion = reader.ReadInt32();
            if (schemaVersion != SimComponentSchema.Version)
                throw new FormatException("Unsupported content manifest schema.");
            int count = reader.ReadInt32();
            if (count < 0) throw new FormatException("Negative archetype count.");
            if (count > reader.RemainingBytes / MinimumArchetypeBytes)
                throw new FormatException("Archetype count exceeds the manifest payload.");

            var rows = new SimArchetypeDefinition[count];
            int previousId = 0;
            for (int index = 0; index < count; index++)
            {
                int archetypeId = reader.ReadInt32();
                if (archetypeId <= previousId) throw new FormatException("Archetype rows must be strictly sorted.");
                previousId = archetypeId;
                rows[index] = new SimArchetypeDefinition(
                    archetypeId,
                    reader.ReadUtf8(),
                    reader.ReadInt32(),
                    reader.ReadInt64(),
                    reader.ReadInt32(),
                    reader.ReadInt64(),
                    reader.ReadInt32());
            }

            if (reader.AtEnd)
                return new DeterministicContentManifest(schemaVersion, rows);

            int modeCount = reader.ReadInt32();
            if (modeCount != 0 && modeCount != 4)
                throw new FormatException("Mode table must be empty or contain every supported mode.");
            var modes = new SimModeProfile[modeCount];
            try
            {
                byte previousModeId = 0;
                for (int index = 0; index < modeCount; index++)
                {
                    SimCombatMode mode = (SimCombatMode)reader.ReadByte();
                    byte modeId = (byte)mode;
                    if (modeId <= previousModeId)
                        throw new FormatException("Mode rows must be strictly sorted.");
                    previousModeId = modeId;
                    byte manualValue = reader.ReadByte();
                    if (manualValue > 1)
                        throw new FormatException("Invalid manual deployment flag.");
                    int maxDeployCount = reader.ReadInt32();
                    int deployDurationTicks = reader.ReadInt32();
                    int timeoutTicks = reader.ReadInt32();
                    int waveCount = reader.ReadInt32();
                    if (waveCount < 0)
                        throw new FormatException("Negative wave count.");
                    if (waveCount > reader.RemainingBytes / sizeof(int))
                        throw new FormatException("Wave count exceeds the manifest payload.");
                    var waves = new int[waveCount];
                    for (int waveIndex = 0; waveIndex < waveCount; waveIndex++)
                        waves[waveIndex] = reader.ReadInt32();
                    modes[index] = new SimModeProfile(mode, manualValue == 1, maxDeployCount,
                        deployDurationTicks, timeoutTicks, waves);
                }

                if (!reader.AtEnd) throw new FormatException("Trailing bytes in content manifest.");
                return new DeterministicContentManifest(schemaVersion, rows, modes);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Invalid mode table.", exception);
            }
        }
    }
}
