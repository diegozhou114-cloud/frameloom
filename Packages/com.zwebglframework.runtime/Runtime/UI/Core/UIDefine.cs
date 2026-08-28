using UnityEngine;

namespace ZWebGLFrameWork.UI
{
    /// <summary>UI 层级 — 值越大显示越靠前</summary>
    public enum UILayer
    {
        Background = 0,  // 背景层
        HUD        = 1,  // HUD层（战斗HUD、部署面板等常驻战斗界面）
        Normal     = 2,  // 普通界面（主城、商店、背包等全屏界面）
        Popup      = 3,  // 弹窗层（确认框、奖励弹窗等模态窗口）
        Tip        = 4,  // 提示层（飘字、Tooltip 等短暂提示）
        Top        = 5,  // 顶层（Loading遮罩、网络重连、系统通知）
    }

    /// <summary>UI 常量定义</summary>
    public static class UIDefine
    {
        /// <summary>UI 预制体存放路径（Resources 目录下）</summary>
        public const string UIPrefabPath = "UI/Panels/";

        /// <summary>每层的 sortingOrder 基数（层内每多一个面板 +1）</summary>
        public const int SortingOrderStep = 100;

        /// <summary>获取层级的 sortingOrder 起始值</summary>
        public static int GetLayerSortingOrder(UILayer layer)
        {
            return (int)layer * SortingOrderStep;
        }
    }
}
