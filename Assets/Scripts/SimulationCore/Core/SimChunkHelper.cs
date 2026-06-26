using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// SimChunk 辅助方法 — 硬编码 6 种组件类型的操作。
    /// 零反射，全平台安全（IL2CPP/iOS/Android/WebGL/微信小游戏）。
    /// </summary>
    internal static class SimChunkHelper
    {
        /// <summary>根据类型 ID 创建组件默认值</summary>
        public static object CreateDefault(int typeId)
        {
            switch (typeId)
            {
                case 0: return new SimTransformComponent();
                case 1: return new SimHealthComponent();
                case 2: return new SimTeamComponent();
                case 3: return new SimMovementComponent();
                case 4: return new SimAttackComponent();
                case 5: return new SimTargetComponent();
                default: return null;
            }
        }

        /// <summary>根据类型 ID 创建对应类型的数组（Capacity 大小）</summary>
        public static Array CreateColumn(int typeId, int capacity)
        {
            switch (typeId)
            {
                case 0: return new SimTransformComponent[capacity];
                case 1: return new SimHealthComponent[capacity];
                case 2: return new SimTeamComponent[capacity];
                case 3: return new SimMovementComponent[capacity];
                case 4: return new SimAttackComponent[capacity];
                case 5: return new SimTargetComponent[capacity];
                default: return null;
            }
        }

        /// <summary>将 Array 列中 localIndex 处的值取出（零装箱 return）</summary>
        public static object GetValue(Array col, int localIndex, int typeId)
        {
            switch (typeId)
            {
                case 0: return ((SimTransformComponent[])col)[localIndex];
                case 1: return ((SimHealthComponent[])col)[localIndex];
                case 2: return ((SimTeamComponent[])col)[localIndex];
                case 3: return ((SimMovementComponent[])col)[localIndex];
                case 4: return ((SimAttackComponent[])col)[localIndex];
                case 5: return ((SimTargetComponent[])col)[localIndex];
                default: return null;
            }
        }

        /// <summary>设置 Array 列中 localIndex 处的值（零装箱 write）</summary>
        public static void SetValue(Array col, int localIndex, object value, int typeId)
        {
            switch (typeId)
            {
                case 0: ((SimTransformComponent[])col)[localIndex] = (SimTransformComponent)value; break;
                case 1: ((SimHealthComponent[])col)[localIndex] = (SimHealthComponent)value; break;
                case 2: ((SimTeamComponent[])col)[localIndex] = (SimTeamComponent)value; break;
                case 3: ((SimMovementComponent[])col)[localIndex] = (SimMovementComponent)value; break;
                case 4: ((SimAttackComponent[])col)[localIndex] = (SimAttackComponent)value; break;
                case 5: ((SimTargetComponent[])col)[localIndex] = (SimTargetComponent)value; break;
            }
        }

        /// <summary>获取列的元素类型名（用于快照类型匹配）</summary>
        public static string GetTypeName(int typeId)
        {
            switch (typeId)
            {
                case 0: return typeof(SimTransformComponent).FullName;
                case 1: return typeof(SimHealthComponent).FullName;
                case 2: return typeof(SimTeamComponent).FullName;
                case 3: return typeof(SimMovementComponent).FullName;
                case 4: return typeof(SimAttackComponent).FullName;
                case 5: return typeof(SimTargetComponent).FullName;
                default: return null;
            }
        }

        /// <summary>根据类型名获取类型 ID</summary>
        public static int GetTypeId(string typeName)
        {
            switch (typeName)
            {
                case "ZWebGLFrameWork.SimulationCore.SimTransformComponent": return 0;
                case "ZWebGLFrameWork.SimulationCore.SimHealthComponent": return 1;
                case "ZWebGLFrameWork.SimulationCore.SimTeamComponent": return 2;
                case "ZWebGLFrameWork.SimulationCore.SimMovementComponent": return 3;
                case "ZWebGLFrameWork.SimulationCore.SimAttackComponent": return 4;
                case "ZWebGLFrameWork.SimulationCore.SimTargetComponent": return 5;
                default: return -1;
            }
        }
    }
}
