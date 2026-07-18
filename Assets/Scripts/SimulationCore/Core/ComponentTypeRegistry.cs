using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>显式组件 schema 注册表 — Type 与 ID 均由共享 schema 固定。</summary>
    public class ComponentTypeRegistry
    {
        private readonly Dictionary<Type, int> _typeToId = new Dictionary<Type, int>();
        private readonly Type[] _idToType;

        public int Count { get; }

        public ComponentTypeRegistry(IEnumerable<ComponentRegistration> registrations)
        {
            if (registrations == null) throw new ArgumentNullException(nameof(registrations));
            var rows = new List<ComponentRegistration>();
            int maxId = 0;
            foreach (var registration in registrations)
            {
                if (registration.Type == null)
                    throw new ArgumentException("Component type cannot be null.", nameof(registrations));
                if (registration.Id < 1)
                    throw new ArgumentException("Component IDs must be positive.", nameof(registrations));
                if (!TryGetSchemaId(registration.Type, out int schemaId))
                    throw new ArgumentException($"Unknown component type: {registration.Type.FullName}", nameof(registrations));
                if (registration.Id != schemaId)
                    throw new ArgumentException(
                        $"Component ID {registration.Id} does not match schema ID {schemaId} for {registration.Type.FullName}.",
                        nameof(registrations));
                if (_typeToId.ContainsKey(registration.Type))
                    throw new ArgumentException($"Duplicate component type: {registration.Type.FullName}", nameof(registrations));
                if (rows.Exists(row => row.Id == registration.Id))
                    throw new ArgumentException($"Duplicate component ID: {registration.Id}", nameof(registrations));
                _typeToId.Add(registration.Type, registration.Id);
                rows.Add(registration);
                if (registration.Id > maxId) maxId = registration.Id;
            }

            _idToType = new Type[maxId + 1];
            foreach (var row in rows)
                _idToType[row.Id] = row.Type;
            Count = rows.Count;
        }

        public ComponentTypeRegistry(IEnumerable<Type> componentTypes)
            : this(ToRegistrations(componentTypes))
        {
        }

        private static IEnumerable<ComponentRegistration> ToRegistrations(IEnumerable<Type> componentTypes)
        {
            if (componentTypes == null) throw new ArgumentNullException(nameof(componentTypes));
            foreach (var type in componentTypes)
            {
                bool found = false;
                foreach (var registration in SimComponentSchema.Registrations)
                {
                    if (registration.Type == type)
                    {
                        found = true;
                        yield return registration;
                        break;
                    }
                }
                if (!found)
                    throw new ArgumentException($"Unknown component type: {type?.FullName ?? "null"}", nameof(componentTypes));
            }
        }

        private static bool TryGetSchemaId(Type type, out int id)
        {
            foreach (var registration in SimComponentSchema.Registrations)
            {
                if (registration.Type == type)
                {
                    id = registration.Id;
                    return true;
                }
            }
            id = 0;
            return false;
        }

        public int GetId(Type type)
        {
            if (!_typeToId.TryGetValue(type, out int id))
                throw new KeyNotFoundException($"[ComponentTypeRegistry] Type '{type?.FullName ?? "null"}' is not registered.");
            return id;
        }

        public bool TryGetId(Type type, out int id) => _typeToId.TryGetValue(type, out id);

        public Type GetType(int id)
        {
            return id > 0 && id < _idToType.Length ? _idToType[id] : null;
        }

        public bool Has(Type type) => _typeToId.ContainsKey(type);

        public static ComponentTypeRegistry CreateCombatRegistry() => SimComponentSchema.CreateRegistry();
    }
}
