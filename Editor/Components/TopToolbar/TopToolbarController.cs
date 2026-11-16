using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// TopToolbar 通用控制器。
    /// 只负责“找到控件 + 把行为交给各个下拉 controller”，不包含业务数据；
    /// 使用方通过事件或各 controller 的 API 注入行为。
    /// </summary>
    public sealed class TopToolbarController
    {
        public VisualElement Root { get; }
        public AddMenuController AddMenu { get; }
        public MoreMenuController More { get; }
        public ToolbarSearchController Search { get; }
        public ToolbarButton? SyncAllButton { get; }

        public event Action? SyncAllClicked;

        /// <param name="root">窗口根元素（Q 查找工具栏控件）。</param>
        /// <param name="addMenuItems">自定义 “+” 菜单项；空表示保留默认项。</param>
        /// <param name="moreMenuItems">自定义 “⋮” 菜单项；空表示保留默认项。</param>
        public TopToolbarController(VisualElement root, IReadOnlyList<AddMenuItem>? addMenuItems = null, IReadOnlyList<AddMenuItem>? moreMenuItems = null)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));

            AddMenu = new AddMenuController(Require<VisualElement>("toolbarAddMenu"), addMenuItems);
            More = new MoreMenuController(Require<VisualElement>("toolbarMoreMenu"), moreMenuItems);
            Search = new ToolbarSearchController(Require<VisualElement>("toolbarSearch"));

            SyncAllButton = Root.Q<ToolbarButton>("toolbarSyncAllButton");
            if (SyncAllButton != null)
                SyncAllButton.RegisterCallback<ClickEvent>(_ => SyncAllClicked?.Invoke());
        }

        private T Require<T>(string name) where T : VisualElement
        {
            var element = Root.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"TopToolbar: 缺少名为 '{name}' 的 {typeof(T).Name} 元素。");
            return element;
        }
    }
}
