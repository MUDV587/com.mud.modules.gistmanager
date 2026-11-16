using System;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// EmptyArea 控制器：对外暴露 “No items found” 文案与 Clear Filters 事件。
    /// 是否可见由宿主（通常是 gistList）决定，这里只负责交互与内容。
    /// </summary>
    public sealed class EmptyAreaController
    {
        public VisualElement Root { get; }
        public Label? Message { get; }
        public Button? ClearFiltersButton { get; }

        public event Action? ClearFiltersRequested;

        public EmptyAreaController(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Message = root.Q<Label>("emptyAreaMessage");
            ClearFiltersButton = root.Q<Button>("emptyAreaButton");
            if (ClearFiltersButton != null)
                ClearFiltersButton.RegisterCallback<ClickEvent>(_ => ClearFiltersRequested?.Invoke());
        }
    }
}
