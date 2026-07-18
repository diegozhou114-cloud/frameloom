using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// SimChunk 辅助方法 — 硬编码 schema 组件类型的操作。
    /// 零反射，全平台安全（IL2CPP/iOS/Android/WebGL/微信小游戏）。
    /// </summary>
    internal static class SimChunkHelper
    {
        /// <summary>根据类型 ID 创建组件默认值</summary>
        public static object CreateDefault(int typeId)
        {
            switch (typeId)
            {
                case SimComponentTypeId.Transform: return new SimTransformComponent();
                case SimComponentTypeId.Health: return new SimHealthComponent();
                case SimComponentTypeId.Team: return new SimTeamComponent();
                case SimComponentTypeId.Movement: return new SimMovementComponent();
                case SimComponentTypeId.Attack: return new SimAttackComponent();
                case SimComponentTypeId.Target: return new SimTargetComponent();
                case SimComponentTypeId.Identity: return new SimIdentityComponent();
                default: return null;
            }
        }

        /// <summary>根据类型 ID 创建对应类型的数组（Capacity 大小）</summary>
        public static Array CreateColumn(int typeId, int capacity)
        {
            switch (typeId)
            {
                case SimComponentTypeId.Transform: return new SimTransformComponent[capacity];
                case SimComponentTypeId.Health: return new SimHealthComponent[capacity];
                case SimComponentTypeId.Team: return new SimTeamComponent[capacity];
                case SimComponentTypeId.Movement: return new SimMovementComponent[capacity];
                case SimComponentTypeId.Attack: return new SimAttackComponent[capacity];
                case SimComponentTypeId.Target: return new SimTargetComponent[capacity];
                case SimComponentTypeId.Identity: return new SimIdentityComponent[capacity];
                default: return null;
            }
        }

        /// <summary>将 Array 列中 localIndex 处的值取出（零装箱 return）</summary>
        public static object GetValue(Array col, int localIndex, int typeId)
        {
            switch (typeId)
            {
                case SimComponentTypeId.Transform: return ((SimTransformComponent[])col)[localIndex];
                case SimComponentTypeId.Health: return ((SimHealthComponent[])col)[localIndex];
                case SimComponentTypeId.Team: return ((SimTeamComponent[])col)[localIndex];
                case SimComponentTypeId.Movement: return ((SimMovementComponent[])col)[localIndex];
                case SimComponentTypeId.Attack: return ((SimAttackComponent[])col)[localIndex];
                case SimComponentTypeId.Target: return ((SimTargetComponent[])col)[localIndex];
                case SimComponentTypeId.Identity: return ((SimIdentityComponent[])col)[localIndex];
                default: return null;
            }
        }

        /// <summary>设置 Array 列中 localIndex 处的值（零装箱 write）</summary>
        public static void SetValue(Array col, int localIndex, object value, int typeId)
        {
            switch (typeId)
            {
                case SimComponentTypeId.Transform: ((SimTransformComponent[])col)[localIndex] = (SimTransformComponent)value; break;
                case SimComponentTypeId.Health: ((SimHealthComponent[])col)[localIndex] = (SimHealthComponent)value; break;
                case SimComponentTypeId.Team: ((SimTeamComponent[])col)[localIndex] = (SimTeamComponent)value; break;
                case SimComponentTypeId.Movement: ((SimMovementComponent[])col)[localIndex] = (SimMovementComponent)value; break;
                case SimComponentTypeId.Attack: ((SimAttackComponent[])col)[localIndex] = (SimAttackComponent)value; break;
                case SimComponentTypeId.Target: ((SimTargetComponent[])col)[localIndex] = (SimTargetComponent)value; break;
                case SimComponentTypeId.Identity: ((SimIdentityComponent[])col)[localIndex] = (SimIdentityComponent)value; break;
            }
        }

        /// <summary>获取列的元素类型名（用于快照类型匹配）</summary>
        public static string GetTypeName(int typeId)
        {
            switch (typeId)
            {
                case SimComponentTypeId.Transform: return typeof(SimTransformComponent).FullName;
                case SimComponentTypeId.Health: return typeof(SimHealthComponent).FullName;
                case SimComponentTypeId.Team: return typeof(SimTeamComponent).FullName;
                case SimComponentTypeId.Movement: return typeof(SimMovementComponent).FullName;
                case SimComponentTypeId.Attack: return typeof(SimAttackComponent).FullName;
                case SimComponentTypeId.Target: return typeof(SimTargetComponent).FullName;
                case SimComponentTypeId.Identity: return typeof(SimIdentityComponent).FullName;
                default: return null;
            }
        }

        /// <summary>根据类型名获取类型 ID</summary>
        public static int GetTypeId(string typeName)
        {
            switch (typeName)
            {
                case "ZWebGLFrameWork.SimulationCore.SimTransformComponent": return SimComponentTypeId.Transform;
                case "ZWebGLFrameWork.SimulationCore.SimHealthComponent": return SimComponentTypeId.Health;
                case "ZWebGLFrameWork.SimulationCore.SimTeamComponent": return SimComponentTypeId.Team;
                case "ZWebGLFrameWork.SimulationCore.SimMovementComponent": return SimComponentTypeId.Movement;
                case "ZWebGLFrameWork.SimulationCore.SimAttackComponent": return SimComponentTypeId.Attack;
                case "ZWebGLFrameWork.SimulationCore.SimTargetComponent": return SimComponentTypeId.Target;
                case "ZWebGLFrameWork.SimulationCore.SimIdentityComponent": return SimComponentTypeId.Identity;
                default: return -1;
            }
        }
    }
}
