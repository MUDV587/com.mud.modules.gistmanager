using System;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// statusBar 控制器：状态文案 / spinner / 错误图标显隐。
    /// </summary>
    public sealed class GistStatusBarController
    {
        public VisualElement Root { get; }
        public Label? StatusLabel { get; }
        public VisualElement? ErrorIcon { get; }
        public VisualElement? LoadingSpinner { get; }

        public GistStatusBarController(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            StatusLabel = root.Q<Label>("statusLabel");
            ErrorIcon = root.Q<VisualElement>("errorIcon");
            LoadingSpinner = root.Q<VisualElement>("loadingSpinner");

            // 官方行为：无错误时隐藏错误图标（有错误时由调用方 SetErrorVisible(true)）
            SetErrorVisible(false);
        }

        public void SetStatusText(string text)
        {
            if (StatusLabel != null)
                StatusLabel.text = text;
        }

        public void SetErrorVisible(bool visible)
        {
            ErrorIcon?.EnableInClassList("gm-hidden", !visible);
        }

        public void SetSpinnerVisible(bool visible)
        {
            LoadingSpinner?.EnableInClassList("gm-hidden", !visible);
        }
    }
}
