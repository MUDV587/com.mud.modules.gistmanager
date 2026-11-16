using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// 工具栏 "⋮"（More actions）菜单：只有图标、没有文字的下拉。
    /// 缺省（customItems 为 null）时发 Reload Manifest / Clear collapsed groups / Advanced Project Settings... / About...；
    /// 传入 customItems 时菜单完全由自定义项构成（不画分隔符，默认事件不派发）。
    /// </summary>
    public sealed class MoreMenuController
    {
        public ToolbarDropdownController Dropdown { get; }

        public event Action? ReloadRequested;
        public event Action? ClearCollapsedRequested;
        public event Action? AdvancedProjectSettingsRequested;
        public event Action? AboutRequested;

        /// <param name="root">下拉按钮根元素。</param>
        /// <param name="customItems">自定义菜单项；传 null 则发 Reload Manifest / Clear collapsed groups / Advanced Project Settings... / About this window...</param>
        public MoreMenuController(VisualElement root, IReadOnlyList<AddMenuItem>? customItems = null)
        {
            Dropdown = new ToolbarDropdownController(root ?? throw new ArgumentNullException(nameof(root)));
            Dropdown.SetLabel(string.Empty);  // 图标由 .gm-toolbar-instance--more Label 的 background-image 提供
            Dropdown.ShowCaret = false;       // More 按钮：只有图标，不带下拉箭头

            if (customItems != null)
            {
                foreach (var item in customItems)
                {
                    if (item == null)
                        continue;
                    Dropdown.AddItem(item.Label, item.OnSelect, item.Enabled);
                }

                return;
            }

            Dropdown.AddItem("Reload Manifest", () => ReloadRequested?.Invoke());
            Dropdown.AddSeparator("more");
            Dropdown.AddItem("Clear collapsed groups", () => ClearCollapsedRequested?.Invoke());
            Dropdown.AddSeparator("settings");
            Dropdown.AddItem("Advanced Project Settings...", () => AdvancedProjectSettingsRequested?.Invoke());
            Dropdown.AddSeparator("about");
            Dropdown.AddItem("About this window...", () => AboutRequested?.Invoke());
        }
    }
}
