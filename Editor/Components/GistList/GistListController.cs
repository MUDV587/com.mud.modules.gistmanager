using System;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// gistList 控制器：管理列表区域的空态切换。
    /// </summary>
    public sealed class GistListController
    {
        public VisualElement Root { get; }
        public VisualElement ListContainer { get; }
        public VisualElement EmptyArea { get; }
        public ScrollView? ScrollView { get; }

        public GistListController(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            ListContainer = root.Q<VisualElement>("listContainer");
            EmptyArea = root.Q<VisualElement>("emptyArea");
            ScrollView = root.Q<ScrollView>("scrollView");
        }

        /// <summary>空态：showEmpty=true 时显示 emptyArea，隐藏 listContainer。</summary>
        public void SetEmptyState(bool showEmpty)
        {
            if (ListContainer != null)
                ListContainer.EnableInClassList("gm-hidden", showEmpty);
            if (EmptyArea != null)
                EmptyArea.EnableInClassList("gm-hidden", !showEmpty);
        }
    }
}
