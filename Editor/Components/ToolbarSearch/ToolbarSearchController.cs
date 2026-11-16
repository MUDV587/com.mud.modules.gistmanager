using System;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// ToolbarSearch 控制器：完全独立于 ToolbarSearchField 的工具栏搜索框。
    /// 提供输入事件、清除能力、has-text 视觉 class 切换。
    /// </summary>
    public sealed class ToolbarSearchController
    {
        public VisualElement Root { get; }
        public TextField Field { get; }

        public event Action<string>? SearchChanged;

        private string _value = string.Empty;

        private readonly VisualElement _innerContainer;

        public ToolbarSearchController(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _innerContainer = Root.Q<VisualElement>(className: "gm-toolbar-search") ?? Root;
            Field = Root.Q<TextField>("ToolbarSearchText")
                ?? throw new InvalidOperationException("缺少元素 'ToolbarSearchText'。");

            Field.RegisterValueChangedCallback(evt =>
            {
                _value = evt.newValue ?? string.Empty;
                Root.EnableInClassList("has-text", !string.IsNullOrEmpty(_value));
                SearchChanged?.Invoke(_value);
            });

            Root.EnableInClassList("has-text", !string.IsNullOrEmpty(Field.value));
            _value = Field.value ?? string.Empty;

            // :focus-within 不是 2022.3 的合法伪类；用 class 切换复刻 "输入时蓝框"。
            // 直接挂到 TextField 上——容器/TextField 上的 FocusInEvent 冒泡路径不稳定。
            Field.RegisterCallback<FocusInEvent>(_ => _innerContainer.EnableInClassList("focused", true));
            Field.RegisterCallback<FocusOutEvent>(_ => _innerContainer.EnableInClassList("focused", false));

            if (Root.Q<Button>("ToolbarSearchClear") is Button clearButton)
            {
                clearButton.clicked += () =>
                {
                    _value = string.Empty;
                    Field.value = string.Empty;
                    Root.EnableInClassList("has-text", false);
                    SearchChanged?.Invoke(string.Empty);
                    Field.Focus();
                };
            }
        }

        public void SetValueWithoutNotify(string value)
        {
            var v = value ?? string.Empty;
            _value = v;
            Field.value = v;
            Root.EnableInClassList("has-text", !string.IsNullOrEmpty(v));
        }
    }
}
