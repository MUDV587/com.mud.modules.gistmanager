using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// 工具栏下拉按钮：button + label + caret（纯按钮皮肤）。
    /// 弹出层用 Unity 内置的 GenericMenu —— 和右键菜单同款的原生 IMGUI 风格原生菜单，
    /// 外部点击关闭、键盘导航、禁用态全部由系统菜单负责。
    /// 注意：GenericMenu 不支持逐项自定义颜色；如需自定义弹出层，
    /// 需要提供自己的 popup 实现（并承担布局/关闭/键盘导航/裁剪等整套工作）。
    /// </summary>
    public sealed class ToolbarDropdownController
    {
        private sealed class Item
        {
            public string Label = string.Empty;
            public bool Enabled = true;
            public Action? OnSelect;
        }

        public VisualElement Root { get; }
        public VisualElement Button { get; }
        public Label? Label { get; }

        /// <summary>点选某一项时携带显示文案派发（仅用于 item.id == Label，不用于 i18n）。</summary>
        public event Action<string>? ItemSelected;

        private readonly List<Item> _items = new List<Item>();
        private bool _caretVisible = true;

        public ToolbarDropdownController(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Button = Root.Q<VisualElement>("ToolbarDropdownButton")
                ?? throw new InvalidOperationException("缺少元素 'ToolbarDropdownButton'。");
            Label = Button.Q<Label>("ToolbarDropdownLabel")
                ?? throw new InvalidOperationException("缺少元素 'ToolbarDropdownLabel'。");

            Button.RegisterCallback<ClickEvent>(OnButtonClick);
        }

        /// <summary>是否显示尾部下拉箭头。默认 true；More 按钮等设置 false 只保留图标。</summary>
        public bool ShowCaret
        {
            get => _caretVisible;
            set
            {
                _caretVisible = value;
                ApplyCaretVisibility();
            }
        }

        private void ApplyCaretVisibility()
        {
            Root.Q<VisualElement>("ToolbarDropdownCaret")
                ?.EnableInClassList("gm-hidden", !_caretVisible);
        }

        public void SetLabel(string text)
        {
            if (Label != null)
                Label.text = text;
        }

        public void Clear() => _items.Clear();
        public int ItemCount => _items.Count;

        public void AddItem(string label, Action? onSelect = null, bool enabled = true)
        {
            _items.Add(new Item { Label = label, Enabled = enabled, OnSelect = onSelect });
        }

        /// <summary>按 0-based index 更新 Item 的显示文案（可用于 toggle check 标记）。</summary>
        public void SetItemLabel(int index, string label)
        {
            if (index >= 0 && index < _items.Count)
                _items[index].Label = label;
        }

        /// <summary>添加一条分隔符。marker 仅用作去重语义，目前不消费。</summary>
        public void AddSeparator(string? marker = null)
        {
            _items.Add(new Item { Label = "__sep__" + (marker ?? string.Empty) });
        }

        /// <summary>用 Unity 内置 GenericMenu 在按钮下方弹出菜单。</summary>
        public void Show()
        {
            if (_items.Count == 0)
                return;

            var menu = new GenericMenu();
            foreach (var item in _items)
            {
                if (item.Label.StartsWith("__sep__"))
                {
                    menu.AddSeparator(string.Empty);
                    continue;
                }

                var captured = item;
                if (item.Enabled)
                    menu.AddItem(new GUIContent(item.Label), false, () =>
                    {
                        captured.OnSelect?.Invoke();
                        ItemSelected?.Invoke(captured.Label);
                    });
                else
                    menu.AddDisabledItem(new GUIContent(item.Label));
            }

            var b = Button.worldBound;
            menu.DropDown(new Rect(b.x, b.yMax, Mathf.Max(1f, b.width), 1f));
        }

        /// <summary>保留语义所需的空实现 —— GenericMenu 无需显式关闭。</summary>
        public void Close() { }

        private void OnButtonClick(ClickEvent evt)
        {
            evt.StopPropagation();
            Show();
        }
    }
}
