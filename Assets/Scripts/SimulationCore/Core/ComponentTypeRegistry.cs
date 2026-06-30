using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 组件类型注册表 — Type → int Id，按类型全名字符串排序确保确定性。
    /// 跨平台相同组件集合产生相同的 Id 分配。
    /// </summary>
    public class ComponentTypeRegistry
    {
        private readonly Dictionary<Type, int> _typeToId = new Dictionary<Type, int>();
        private readonly Type[] _idToType;

        public int Count => _idToType.Length;

        /// <param name="componentTypes">所有组件类型（顺序即ID分配顺序，调用者负责保持与SimChunkHelper一致）</param>
        public ComponentTypeRegistry(IEnumerable<Type> componentTypes)
        {
            var unique = new List<Type>();
            foreach (var t in componentTypes)
                if (!unique.Contains(t))
                    unique.Add(t);

            _idToType = unique.ToArray();
            for (int i = 0; i < _idToType.Length; i++)
                _typeToId[_idToType[i]] = i;
        }

        /// <summary>Type → int Id（O(1)）</summary>
        public int GetId(Type type)
        {
            _typeToId.TryGetValue(type, out int id);
            return id; // 0 if not found (valid: first registered type)
        }

        /// <summary>int Id → Type（O(1)）</summary>
        public Type GetType(int id)
        {
            if (id >= 0 && id < _idToType.Length)
                return _idToType[id];
            return null;
        }

        /// <summary>检查类型是否已注册</summary>
        public bool Has(Type type) => _typeToId.ContainsKey(type);

        /// <summary>创建默认注册表（战斗用的 6 种组件，顺序与 SimChunkHelper 硬编码一致）</summary>
        public static ComponentTypeRegistry CreateCombatRegistry()
        {
            return new ComponentTypeRegistry(new Type[]
            {
                typeof(SimTransformComponent),
                typeof(SimHealthComponent),
                typeof(SimTeamComponent),
                typeof(SimMovementComponent),
                typeof(SimAttackComponent),
                typeof(SimTargetComponent),
            });
        }
    }
}
