using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 游戏初始化器 — 离线模式核心。
    /// 首次启动时创建默认玩家数据和村庄建筑，保存到本地存档槽位。
    /// </summary>
    public static class GameInitializer
    {
        /// <summary>是否有任何存档存在</summary>
        public static bool HasAnySave()
        {
            for (int i = 0; i < SaveManager.MaxSaveSlots; i++)
            {
                if (SaveManager.Exists(i)) return true;
            }
            return false;
        }

        /// <summary>
        /// 确保当前槽位存在存档。若不存在则创建默认新手数据并保存。
        /// </summary>
        public static void EnsureSaveExists()
        {
            int slot = SaveManager.CurrentSlot;

            if (SaveManager.Exists(slot))
            {
                Debug.Log($"[GameInitializer] 存档已存在 slot={slot}，跳过初始化");
                return;
            }

            Debug.Log($"[GameInitializer] 首次启动 slot={slot}，创建默认存档...");
            CreateDefaultSave(slot);
        }

        /// <summary>创建默认新手存档（Town Hall Lv1 + 基础建筑）</summary>
        public static void CreateDefaultSave(int slot)
        {
            var saveData = new PlayerSaveData
            {
                PlayerName = "Villager",
                PlayerLevel = 1,
                TownHallLevel = 1,
                Gold = 500,
                Elixir = 300,
                DarkElixir = 0,
                Gems = 50,
                Trophies = 0,
                Exp = 0,
                Buildings = new List<BuildingSaveData>
                {
                    new BuildingSaveData { TypeId = 1, GridX = 20, GridY = 22, Level = 1 }, // Town Hall (中心)
                    new BuildingSaveData { TypeId = 2, GridX = 16, GridY = 26, Level = 1 }, // Gold Mine (左上)
                    new BuildingSaveData { TypeId = 3, GridX = 24, GridY = 26, Level = 1 }, // Elixir Collector (右上)
                    new BuildingSaveData { TypeId = 5, GridX = 16, GridY = 18, Level = 1 }, // Cannon (左下)
                },
                SaveTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };

            SaveManager.Save(saveData, slot);

            Debug.Log($"[GameInitializer] ✅ 默认存档已创建 slot={slot}: " +
                      $"{saveData.PlayerName} Lv.{saveData.PlayerLevel}");
        }

    }
}
