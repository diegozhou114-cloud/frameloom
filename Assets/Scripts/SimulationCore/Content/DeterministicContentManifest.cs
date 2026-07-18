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
            ArchetypeId = archetypeId;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MaxHealth = maxHealth;
            SpeedRaw = speedRaw;
            Damage = damage;
            RangeRaw = rangeRaw;
            CooldownTicks = cooldownTicks;
        }
    }

    public sealed class DeterministicContentManifest
    {
        private const int MagicLength = 4;
        private static readonly byte[] Magic = { 0x5A, 0x53, 0x49, 0x4D };
        private readonly SimArchetypeDefinition[] _archetypes;

        public int SchemaVersion { get; }
        public IReadOnlyList<SimArchetypeDefinition> Archetypes => Array.AsReadOnly((SimArchetypeDefinition[])_archetypes.Clone());
        public byte[] ContentHash
        {
            get
            {
                using (SHA256 sha256 = SHA256.Create())
                    return sha256.ComputeHash(ToCanonicalBytes());
            }
        }

        public DeterministicContentManifest(int schemaVersion, SimArchetypeDefinition[] archetypes)
        {
            if (schemaVersion != SimComponentSchema.Version)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (archetypes == null) throw new ArgumentNullException(nameof(archetypes));
            var rows = archetypes.ToArray();
            if (rows.Any(row => row == null)) throw new ArgumentException("Archetype rows cannot be null.", nameof(archetypes));
            if (rows.GroupBy(row => row.ArchetypeId).Any(group => group.Count() > 1))
                throw new ArgumentException("Duplicate archetype ID.", nameof(archetypes));
            Array.Sort(rows, (left, right) => left.ArchetypeId.CompareTo(right.ArchetypeId));
            SchemaVersion = schemaVersion;
            _archetypes = rows;
        }

        public SimArchetypeDefinition GetArchetype(int archetypeId)
        {
            foreach (var archetype in _archetypes)
                if (archetype.ArchetypeId == archetypeId) return archetype;
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

            if (!reader.AtEnd) throw new FormatException("Trailing bytes in content manifest.");
            return new DeterministicContentManifest(schemaVersion, rows);
        }
    }
}
