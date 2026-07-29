using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.UI
{
    /// <summary>
    /// UI 层级堆栈 — 每个 UILayer 维护一个独立栈，管理面板的 Push/Pop 和自动暂停/恢复。
    /// </summary>
    public class UIStack
    {
        private const int MaxStackDepth = 50;
        private readonly Dictionary<UILayer, Stack<UIBase>> _stacks = new Dictionary<UILayer, Stack<UIBase>>();

        public UIStack()
        {
            foreach (UILayer layer in System.Enum.GetValues(typeof(UILayer)))
            {
                _stacks[layer] = new Stack<UIBase>();
            }
        }

        // ==================== 查询 ====================

        /// <summary>获取指定层级栈顶面板（不移除）</summary>
        public UIBase Peek(UILayer layer)
        {
            var stack = _stacks[layer];
            return stack.Count > 0 ? stack.Peek() : null;
        }

        /// <summary>获取指定层级的所有面板（栈顶在上）</summary>
        public IEnumerable<UIBase> GetLayerPanels(UILayer layer)
        {
            return _stacks[layer];
        }

        /// <summary>查找指定类型的面板（任意层级）</summary>
        public T Find<T>() where T : UIBase
        {
            foreach (var kv in _stacks)
            {
                foreach (var panel in kv.Value)
                {
                    if (panel is T t)
                        return t;
                }
            }
            return null;
        }

        /// <summary>指定面板是否在栈中</summary>
        public bool Contains(UIBase panel)
        {
            if (panel == null) return false;
            if (!_stacks.TryGetValue(panel.Layer, out var stack)) return false;
            return stack.Contains(panel);
        }

        /// <summary>获取层级中面板数量</summary>
        public int Count(UILayer layer) => _stacks[layer].Count;

        /// <summary>所有层级总面板数</summary>
        public int TotalCount
        {
            get
            {
                int count = 0;
                foreach (var kv in _stacks) count += kv.Value.Count;
                return count;
            }
        }

        // ==================== Push（打开） ====================

        /// <summary>将面板推入其所在层级的栈顶，自动处理暂停/恢复</summary>
        public void Push(UIBase panel)
        {
            if (panel == null) return;

            var layer = panel.Layer;
            var stack = _stacks[layer];

            if (stack.Count >= MaxStackDepth)
            {
                Debug.LogError($"[UIStack] 层级 {layer} 栈深度已达上限 {MaxStackDepth}，Push 被拒绝: {panel.name}");
                return;
            }

            // 暂停同层旧栈顶
            if (stack.Count > 0)
            {
                var previous = stack.Peek();
                previous.DoPause();
            }

            // 暂停更低层级的面板（如果当前面板需要）
            if (panel.PauseLowerPanels)
            {
                PauseLowerLayers(layer);
            }

            stack.Push(panel);
        }

        // ==================== Pop（关闭） ====================

        /// <summary>关闭指定面板并从栈中移除，自动恢复</summary>
        public void Pop(UIBase panel)
        {
            if (panel == null) return;

            var layer = panel.Layer;
            var stack = _stacks[layer];

            if (stack.Count == 0) return;

            // 如果关闭的不是栈顶，需要重建栈（中间面板关闭）
            if (stack.Peek() != panel)
            {
                RemoveFromMiddle(stack, panel);
                panel.DoClose();
                // 恢复被中间面板阻挡的下层
                if (panel.PauseLowerPanels)
                {
                    ResumeLowerLayers(layer);
                }
                return;
            }

            // 正常关闭栈顶
            stack.Pop();
            panel.DoClose();

            // 恢复同层新栈顶
            if (stack.Count > 0 && !IsBlockedByHigherLayer(layer))
            {
                stack.Peek().DoResume();
            }

            // 恢复更低层级（仍有模态面板时 ResumeLowerLayers 会保持暂停）
            if (panel.PauseLowerPanels)
            {
                ResumeLowerLayers(layer);
            }
        }

        /// <summary>关闭指定层级栈顶面板</summary>
        public void Pop(UILayer layer)
        {
            if (_stacks[layer].Count == 0) return;
            Pop(_stacks[layer].Peek());
        }

        /// <summary>关闭所有面板</summary>
        public void Clear()
        {
            // 从高层到底层关闭
            for (int i = (int)UILayer.Top; i >= (int)UILayer.Background; i--)
            {
                var layer = (UILayer)i;
                var stack = _stacks[layer];
                while (stack.Count > 0)
                {
                    var panel = stack.Pop();
                    panel.DoClose();
                }
            }
        }

        /// <summary>关闭指定层级及以上的所有面板</summary>
        public void ClearFrom(UILayer layer)
        {
            for (int i = (int)UILayer.Top; i >= (int)layer; i--)
            {
                var curLayer = (UILayer)i;
                var stack = _stacks[curLayer];
                while (stack.Count > 0)
                {
                    var panel = stack.Pop();
                    panel.DoClose();
                }
            }

            ResumeLowerLayers(layer);
        }

        // ==================== 内部方法 ====================

        private void PauseLowerLayers(UILayer currentLayer)
        {
            for (int i = (int)currentLayer - 1; i >= (int)UILayer.Background; i--)
            {
                var layer = (UILayer)i;
                var stack = _stacks[layer];
                if (stack.Count > 0)
                {
                    stack.Peek().DoPause();
                }
            }
        }

        private bool IsBlockedByHigherLayer(UILayer currentLayer)
        {
            for (int i = (int)currentLayer + 1; i <= (int)UILayer.Top; i++)
            {
                var stack = _stacks[(UILayer)i];
                if (ContainsBlockingPanel(stack))
                    return true;
            }

            return false;
        }

        private static bool ContainsBlockingPanel(Stack<UIBase> stack)
        {
            foreach (var panel in stack)
            {
                if (panel.PauseLowerPanels)
                    return true;
            }

            return false;
        }

        private void ResumeLowerLayers(UILayer currentLayer)
        {
            // 检查当前层及更高层是否还有需要暂停下层的面板
            for (int i = (int)currentLayer; i <= (int)UILayer.Top; i++)
            {
                var checkLayer = (UILayer)i;
                var stack = _stacks[checkLayer];
                if (ContainsBlockingPanel(stack))
                    return; // 仍有上层需要暂停下层，不恢复
            }

            // 没有上层需要暂停了，逐层恢复到下一个模态面板为止
            for (int i = (int)currentLayer - 1; i >= (int)UILayer.Background; i--)
            {
                var layer = (UILayer)i;
                var stack = _stacks[layer];
                if (stack.Count > 0)
                {
                    var panel = stack.Peek();
                    panel.DoResume();
                    if (ContainsBlockingPanel(stack))
                        return;
                }
            }
        }

        private void RemoveFromMiddle(Stack<UIBase> stack, UIBase target)
        {
            var temp = new Stack<UIBase>();
            while (stack.Count > 0)
            {
                var panel = stack.Pop();
                if (panel == target) break;
                temp.Push(panel);
            }

            // 放回
            while (temp.Count > 0)
            {
                stack.Push(temp.Pop());
            }
        }
    }
}
