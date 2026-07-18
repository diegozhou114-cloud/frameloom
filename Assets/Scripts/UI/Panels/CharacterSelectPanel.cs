using UnityEngine;
using UnityEngine.UI;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.UI
{
    /// <summary>角色选择面板 — 5个存档槽位，空槽位点击创建新角色</summary>
    public class CharacterSelectPanel : UIBase
    {
        private Text[] _slotTexts = new Text[5];

        protected override void OnOpen(object data)
        {
            BuildUI();
            RefreshSlots();
        }

        private void BuildUI()
        {
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.12f, 0.2f, 1f);

            CreateTitle("选择存档", new Vector2(0.5f, 0.88f));

            for (int i = 0; i < 5; i++)
            {
                float y = 0.73f - i * 0.12f;
                int slotIndex = i;

                var go = new GameObject($"Slot_{i}",
                    typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(transform, false);
                go.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.35f);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.1f, y);
                rt.anchorMax = new Vector2(0.9f, y + 0.10f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                go.GetComponent<Button>().onClick.AddListener(() => OnSlotClick(slotIndex));

                var txtGo = CreateText($"SlotText_{i}", "", 18,
                    new Vector2(0.5f, 0.5f), TextAnchor.MiddleCenter, Color.white);
                txtGo.transform.SetParent(go.transform, false);
                txtGo.rectTransform.anchorMin = Vector2.zero;
                txtGo.rectTransform.anchorMax = Vector2.one;
                _slotTexts[i] = txtGo;
            }
        }

        private void RefreshSlots()
        {
            for (int i = 0; i < 5; i++)
            {
                if (SaveManager.Exists(i))
                {
                    var data = SaveManager.Load<PlayerSaveData>(i);
                    if (data != null)
                    {
                        _slotTexts[i].text =
                            $"[存档 {i + 1}]  {data.PlayerName}  " +
                            $"Lv.{data.PlayerLevel}  TH{data.TownHallLevel}";
                        _slotTexts[i].color = Color.white;
                    }
                }
                else
                {
                    _slotTexts[i].text = $"[空槽位 {i + 1}]  点击创建新角色";
                    _slotTexts[i].color = Color.gray;
                }
            }
        }

        private void OnSlotClick(int slot)
        {
            SaveManager.CurrentSlot = slot;

            if (!SaveManager.Exists(slot))
            {
                if (!GameInitializer.CreateDefaultSave(slot))
                {
                    EventBus.Publish(LoginEvents.LoginError, "创建本地存档失败，请重试");
                    return;
                }
            }

            // 通过 EventBus 通知 LoginState：角色已选择，槽位 N
            EventBus.Publish(LoginEvents.CharacterSelected, slot);
        }

        private Text CreateText(string name, string text, int fontSize,
            Vector2 anchor, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = align;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            return txt;
        }

        private void CreateTitle(string text, Vector2 anchor)
        {
            var go = new GameObject("Title", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = 28;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.rectTransform.anchorMin = txt.rectTransform.anchorMax = anchor;
            txt.rectTransform.sizeDelta = new Vector2(400, 50);
        }
    }
}
