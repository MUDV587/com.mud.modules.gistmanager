using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// GroupHeader 控制器（对应 作者分组）：
    /// 折叠/展开切换；选中行在折叠后仍可见：行级 Show/Hide 派发。
    /// 折叠的显隐必须走 C# inline display——USS 对 CloneTree 后的行不重算 display，
    /// 已证实改 class 走 USS 路径不生效；USS 侧保留 .gm-group-header.collapsed .gm-item-row 规则作为兜底文档。
    /// Gist 行不区分 Package/Feature 类型。
    /// </summary>
    public sealed class GroupHeaderController
    {
        public VisualElement Root { get; }
        /// <summary>真正的 .gm-group-header 根：UXML 被 Instance 后外面会包一层 TemplateContainer，
        /// 所有样式/状态都要施加在内层（.gm-group-header）才会被 USS 规则命中。</summary>
        public VisualElement Header { get; }
        public Label? HeaderTitle { get; }
        public VisualElement? GroupContainer { get; }

        public event Action<bool>? CollapsedChanged;

        public bool IsCollapsed { get; private set; }

        public GroupHeaderController(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));

            // UXML CloneTree/Instance 会包 TemplateContainer，下钻找到 .gm-group-header 内层元素
            Header = Root.Q<VisualElement>(className: "gm-group-header") ?? Root;
            GroupContainer = Header.Q<VisualElement>("groupContainer");
            HeaderTitle = Header.Q<Label>("headerTitle");

            var headerRow = Header.Q<VisualElement>("headerRow");
            if (headerRow != null)
                headerRow.RegisterCallback<ClickEvent>(_ => Toggle());

            IsCollapsed = Header.GetClasses().Contains("collapsed");
        }

        public void SetCollapsed(bool collapsed)
        {
            if (IsCollapsed == collapsed)
                return;

            IsCollapsed = collapsed;
            Header.EnableInClassList("collapsed", collapsed);
            Root.EnableInClassList("collapsed", collapsed);
            ApplyRowDisplay();
            Header.MarkDirtyRepaint();
            Root.MarkDirtyRepaint();
            CollapsedChanged?.Invoke(collapsed);
        }

        public void Toggle() => SetCollapsed(!IsCollapsed);

        /// <summary>子行直接挂在 groupContainer 下；兼容 Instantiate() 返回的 TemplateContainer 包装器。</summary>
        private IReadOnlyList<VisualElement> GetItemRows()
        {
            if (GroupContainer == null) return Array.Empty<VisualElement>();
            return GroupContainer.Children()
                .Where(static e => e.GetClasses().Contains("gm-item-row")
                                   || (e.childCount > 0 && e[0].GetClasses().Contains("gm-item-row")))
                .ToList();
        }

        /// <summary>根据折叠状态与 selected class，派发每个子行显隐（折叠时 selected 行保持可见）。</summary>
        public void ApplyRowDisplay()
        {
            foreach (var row in GetItemRows())
            {
                var main = row.Q<VisualElement>("rowContent");
                var selected = main != null && main.GetClasses().Contains("selected");
                var disp = IsCollapsed && !selected ? DisplayStyle.None : DisplayStyle.Flex;
                row.style.display = disp;
                if (main != null)
                    main.style.display = disp;
            }
        }
    }
}
