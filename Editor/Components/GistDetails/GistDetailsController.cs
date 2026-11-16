using System;
using System.Linq;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager.Components
{
    /// <summary>
    /// gistDetails 控制器：
    /// 多选/单选切换、Update/Remove 两个操作按钮事件（Description 单 Tab，内容全 UI Toolkit）。
    /// 业务数据由调用方注入（文案 / 显隐），这里只负责结构交互。
    /// </summary>
    public sealed class GistDetailsController
    {
        public VisualElement Root { get; }
        public ScrollView? DetailScrollView { get; }

        public VisualElement MultiSelectDetails { get; }
        public VisualElement Detail { get; }
        public VisualElement DetailsBody { get; }

        // 文本内容（只读暴露，调用方按需 set text）
        public Label? TitleLabel { get; }
        public Label? VersionLabel { get; }
        public Label? DescriptionLabel { get; }
        public Label? SourcePathLabel { get; }
        public Label? RegistryAndAuthorLabel { get; }
        public Label? DetailNameLabel { get; }
        public Label? MultiSelectTitleLabel { get; }

        // 关键操作按钮
        public Button? UpdateButton { get; }
        public Button? RemoveButton { get; }

        public event Action? UpdateRequested;
        public event Action? RemoveRequested;

        public GistDetailsController(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            DetailScrollView = Root.Q<ScrollView>("detailScrollView");
            MultiSelectDetails = Root.Q<VisualElement>("multiSelectDetails");
            Detail = Root.Q<VisualElement>("detail");
            DetailsBody = Root.Q<VisualElement>("detailsBody");

            TitleLabel = Root.Q<Label>("detailTitle");
            VersionLabel = Root.Q<Label>("detailVersion");
            DescriptionLabel = Root.Q<Label>("detailDescription");
            SourcePathLabel = Root.Q<Label>("detailSourcePath");
            RegistryAndAuthorLabel = Root.Q<Label>("registryAndAuthorLabel");
            DetailNameLabel = Root.Q<Label>("detailName");
            MultiSelectTitleLabel = Root.Q<Label>("multiSelectTitle");

            UpdateButton = Root.Q<Button>("gistSyncButton");
            RemoveButton = Root.Q<Button>("gistRemoveButton");

            if (UpdateButton != null)
                UpdateButton.RegisterCallback<ClickEvent>(_ => UpdateRequested?.Invoke());
            if (RemoveButton != null)
                RemoveButton.RegisterCallback<ClickEvent>(_ => RemoveRequested?.Invoke());

            SetMultiSelectMode(false);
        }

        public Button? GetButton(string buttonName) => Root.Q<Button>(buttonName);
        public Label? GetLabel(string labelName) => Root.Q<Label>(labelName);
        public VisualElement? GetElement(string name) => Root.Q<VisualElement>(name);

        /// <summary>true = 多选模式（显示 MultiSelectDetails，隐藏 Detail）；false = 单选（反之）。</summary>
        public void SetMultiSelectMode(bool multiSelect)
        {
            MultiSelectDetails?.EnableInClassList("gm-hidden", !multiSelect);
            Detail?.EnableInClassList("gm-hidden", multiSelect);
        }

        public void SetTitle(string text)
        {
            if (TitleLabel != null) TitleLabel.text = text;
        }

        public void SetVersion(string text)
        {
            if (VersionLabel != null) VersionLabel.text = text;
        }

        public void SetDescription(string text)
        {
            if (DescriptionLabel != null) DescriptionLabel.text = text;
        }

        public void SetSourcePath(string text)
        {
            if (SourcePathLabel != null) SourcePathLabel.text = text;
        }

        public void SetMultiSelectTitle(string text)
        {
            if (MultiSelectTitleLabel != null) MultiSelectTitleLabel.text = text;
        }

        public void SetRegistryAndAuthor(string text)
        {
            if (RegistryAndAuthorLabel != null) RegistryAndAuthorLabel.text = text;
        }

        public void SetDetailName(string text)
        {
            if (DetailNameLabel != null) DetailNameLabel.text = text;
        }
    }
}
