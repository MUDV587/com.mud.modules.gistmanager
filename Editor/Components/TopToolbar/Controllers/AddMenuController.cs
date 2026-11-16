using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>Add 菜单项定义（业务方可自定义，缺省时发往磁盘/git URL/tarball）。</summary>
    public sealed class AddMenuItem
    {
        public string Label = string.Empty;
        public Action? OnSelect;
        public bool Enabled = true;
    }

    /// <summary>工具栏 “+” 菜单：只负责菜单项与事件，业务方订阅事件即可。</summary>
    public sealed class AddMenuController
    {
        public ToolbarDropdownController Dropdown { get; }

        public event Action? AddFromDiskRequested;
        public event Action? AddFromGitUrlRequested;
        public event Action? AddFromTarballRequested;

        /// <param name="root">下拉按钮根元素。</param>
        /// <param name="customItems">自定义菜单项；传 null 则发盘/git URL/tarball 三项，
        /// 事件照常派发；传入后菜单完全由自定义项构成，默认事件不再派发。</param>
        public AddMenuController(VisualElement root, IReadOnlyList<AddMenuItem>? customItems = null)
        {
            Dropdown = new ToolbarDropdownController(root ?? throw new ArgumentNullException(nameof(root)));
            Dropdown.SetLabel(string.Empty);  // 图标由 .gm-toolbar-instance--add Label 的 background-image 提供

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

            Dropdown.AddItem("Add package from disk...", () => AddFromDiskRequested?.Invoke());
            Dropdown.AddItem("Add package from git URL...", () => AddFromGitUrlRequested?.Invoke());
            Dropdown.AddItem("Add package from tarball...", () => AddFromTarballRequested?.Invoke());
        }
    }
}
