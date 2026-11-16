using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager
{
    using MUD.Modules.GistManager.Components;

    /// <summary>
    /// Gist 脚本管理器主窗口。
    /// UI 结构：GistManagerWindow.uxml（TopToolbar + Splitter 左 列表/右 详情 + 底部状态栏），
    /// 左栏 = GroupHeader（作者分组折叠）+ ItemRow（24px 行：标题/短 id/安装态图标），
    /// 右栏 = gistDetails（标题操作区 + Description 单 Tab，内含全 UI Toolkit 文件编辑器），
    /// 工具栏 = “+”（从git链接添加Gist脚本 / Add Gist Script from git URL... 弹窗）/ 同步全部 / 搜索 / More（重新加载清单 / Gist管理器设置 / 关于）。
    /// 业务层（GistManifest / GistCatalog / GistSyncService / GistApiClient / GistSearch）原样复用。
    /// 添加只改 gists.json 并刷新视图，下载由显式“同步”动作触发。
    /// </summary>
    public class GistManagerWindow : EditorWindow
    {
        private const string GistManagerWindowUxmlPath = "Packages/com.mud.modules.gistmanager/Editor/GistManagerWindow.uxml";
        private const string GroupHeaderUxmlPath = "Packages/com.mud.modules.gistmanager/Editor/Components/GroupHeader/GroupHeader.uxml";
        private const string ItemRowUxmlPath = "Packages/com.mud.modules.gistmanager/Editor/Components/ItemRow/ItemRow.uxml";
        private static readonly string[] PlacementLabels = { "Auto", "Editor", "Runtime" };

        /// <summary>搜索防抖时长，与官方 PackageSearchBar.k_SearchFieldDelayTicks 一致（1/3 秒）。</summary>
        private const long SearchDelayTicks = TimeSpan.TicksPerSecond / 3;

        private GistManifest _manifest = new GistManifest();
        private string? _loadError;
        private string _status = "";
        private string _search = string.Empty;
        private bool _busy;
        private readonly List<string> _selectedGistIds = new List<string>();
        private string _anchorGistId = string.Empty;
        private DateTime _manifestWriteTimeUtc = DateTime.MinValue;

        private readonly Dictionary<string, bool> _authorFoldouts = new Dictionary<string, bool>();

        // UI Toolkit 控制器与树（CreateGUI 建立）
        private TopToolbarController? _toolbar;
        private GistListController? _list;
        private EmptyAreaController? _emptyArea;
        private GistStatusBarController? _statusBar;
        private GistDetailsController? _details;
        private readonly Dictionary<string, VisualElement> _rows = new Dictionary<string, VisualElement>();
        private readonly List<GroupHeaderController> _groupControllers = new List<GroupHeaderController>();

        // 详情 Description Tab 内的文件编辑区域（CreateGUI 时构建，之后只刷新内容）
        private Toggle? _toggleAll;
        private VisualElement? _filesContainer;

        // 详情区空态占位（manifest 为空/无选中时显示，引导添加）
        private VisualElement? _detailsEmptyState;

        // 多选详情：信息 Label
        private Label? _multiSelectInfo;

        // 内联添加面板：附着在工具栏下方的输入面板，非独立窗口
        private VisualElement? _addPanel;
        private TextField? _addField;
        private Label? _addHintLabel;
        private Label? _addPlaceholder;

        // 延迟搜索（官方 SearchFieldDelayArgs 同款：记录待应用文本与时间戳）
        private string? _pendingSearch;
        private long _pendingSearchTicks;

        [MenuItem("Tools/MUD/Gist Manager")]
        public static void ShowWindow()
        {
            GetWindow<GistManagerWindow>(GistLoc.IsChinese ? "Gist管理器" : "GistManager");
        }



        private void OnEnable()
        {
            LoadFoldouts();
            ReloadManifest();
        }

        private void OnFocus()
        {
            // 检测手动编辑的 gists.json，窗口获得焦点时读回最新清单
            if (!_busy && GistManifestStore.ManifestWriteTimeUtc != _manifestWriteTimeUtc)
                ReloadManifest();

            // 刚打开窗口时面板内还没有焦点元素，按键事件会被面板直接丢弃：
            // 把焦点交给选中行，方向键/Home/End 才能开箱即用（搜索框获焦时不受影响）。
            FocusSelectionIfNone();
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollSearchDelay;
        }

        // ------------------------------------------------------------------ UI Toolkit 装配

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();

            // 语言切换后重建时刷新 tab 标题（GistManager / Gist管理器）
            titleContent = new GUIContent(GistLoc.T("Gist管理器", "GistManager"));

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GistManagerWindowUxmlPath);
            if (uxml == null)
            {
                Debug.LogError("Gist Manager: UI template missing at " + GistManagerWindowUxmlPath);
                root.Add(new Label("UI template missing: " + GistManagerWindowUxmlPath));
                return;
            }

            uxml.CloneTree(root);

            _toolbar = new TopToolbarController(root, addMenuItems: AddMenuItems(), moreMenuItems: MoreMenuItems());
            // AttachPopupsTo 已为空实现（弹出层改用 GenericMenu，外部点击关闭由系统菜单负责）
            // Sync All 按钮文案跟随语言设置（组件默认 Sync All；demo 保持不变）
            if (_toolbar.SyncAllButton != null)
            {
                _toolbar.SyncAllButton.text = GistLoc.T("全部同步", "Sync All");
                _toolbar.SyncAllButton.tooltip = GistLoc.T("同步清单里所有启用的 gist（Ctrl/Cmd+Shift+G）",
                                                           "Sync all enabled gists in the manifest (Ctrl/Cmd+Shift+G)");
            }

            _list = new GistListController(root.Q<VisualElement>("gistList"));
            _emptyArea = new EmptyAreaController(root.Q<VisualElement>("emptyArea"));
            _statusBar = new GistStatusBarController(root.Q<VisualElement>("statusBar"));
            _details = new GistDetailsController(root.Q<VisualElement>("gistDetails"));
            _ = new SplitterController(root.Q<VisualElement>("mainContainer"));

            ApplySingleSelectDefaults();
            BindFocusClass();
            WireControllers();
            BuildFilesSection();
            BuildAddPanel();

            _toolbar.Search.SetValueWithoutNotify(_search);

            // Ctrl/Cmd+F 聚焦搜索框（官方 PackageSearchBar 监听 Find 命令）
            root.RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            root.RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);

            // 键盘导航（官方 ItemListScrollView.RegisterCallback<KeyDownEvent> 同款：事件冒泡到根统一处理）
            root.RegisterCallback<KeyDownEvent>(OnKeyDown);

            RebuildList();
            UpdateDetails();
            UpdateStatusBar();
            FocusSelectionIfNone();
        }

        /// <summary>详情面板静态配置（UXML 已移除 Install/Unlock/GitUpdate 等操作按钮）。</summary>
        private void ApplySingleSelectDefaults()
        {
            if (_details == null)
                return;

            _details.SetMultiSelectMode(false);
            var update = _details.UpdateButton;
            if (update != null)
                update.text = GistLoc.T("同步", "Sync");
            var remove = _details.RemoveButton;
            if (remove != null)
                remove.text = GistLoc.T("移除", "Remove");

            // quickStart 按钮复用为 “Open Gist URL / 打开Gist链接”
            var open = _details.GetButton("quickStart");
            if (open != null)
            {
                open.text = GistLoc.T("打开Gist链接", "Open Gist URL");
                open.tooltip = GistLoc.T("在浏览器打开该 Gist 的链接", "Open this gist link in browser");
            }

            // 并排新增 “Open Folder / 打开文件夹”：在文件管理器里打开本地 gist 包目录
            if (open != null && open.parent != null && _details.GetButton("openFolderButton") == null)
            {
                var folder = new Button
                {
                    name = "openFolderButton",
                    text = GistLoc.T("打开文件夹", "Open Folder"),
                    tooltip = GistLoc.T("在文件管理器中打开本地 gist 包目录", "Open the local gist package folder in the file manager")
                };
                folder.AddToClassList("rightAlign");
                folder.AddToClassList("gist-open-folder-button");
                folder.clicked += OpenSelectedFolder;
                open.parent.Insert(open.parent.IndexOf(open), folder);
            }

            // 详情里的 “Installed From” 标题改为 gist 语义
            var sourceContainer = _details.GetElement("detailSourcePathContainer");
            var title = sourceContainer?.Q<Label>(className: "containerTitle");
            if (title != null)
                title.text = GistLoc.T("目标文件夹", "Target Folder");

            // Description 单 Tab 的按钮文案
            var descTab = _details.GetButton("descriptionButton");
            if (descTab != null)
                descTab.text = GistLoc.T("描述", "Description");

            // versionLabel 用灰显样式展示短 id
            if (_details.VersionLabel != null)
                _details.VersionLabel.AddToClassList("gist-detail-version");

            if (_details.RegistryAndAuthorLabel != null)
                _details.RegistryAndAuthorLabel.enableRichText = true;

        }

        private void Show(VisualElement? element)
        {
            element?.RemoveFromClassList("gm-hidden");
        }

        private void Hide(VisualElement? element)
        {
            element?.AddToClassList("gm-hidden");
        }

        /// <summary>维护 .focus 类（focusin/out 切换），焦点样式由组件 USS 读取</summary>
        private void BindFocusClass()
        {
            var container = rootVisualElement.Q(null, "gist-manager-window-root");
            if (container == null)
                return;

            container.AddToClassList("focus");
            container.RegisterCallback<FocusInEvent>(_ => container.AddToClassList("focus"));
            container.RegisterCallback<FocusOutEvent>(_ => container.RemoveFromClassList("focus"));
        }

        private IReadOnlyList<AddMenuItem> AddMenuItems()
        {
            return new List<AddMenuItem>
            {
                new AddMenuItem { Label = GistLoc.T("从git链接添加Gist脚本...", "Add Gist Script from git URL..."), OnSelect = ToggleAddPanel }
            };
        }

        private IReadOnlyList<AddMenuItem> MoreMenuItems()
        {
            return new List<AddMenuItem>
            {
                new AddMenuItem { Label = GistLoc.T("重新加载清单", "Reload Manifest"), OnSelect = ReloadManifest },
                new AddMenuItem { Label = GistLoc.T("Gist管理器设置", "GistManager Settings"), OnSelect = ShowSettingsPopup },
                new AddMenuItem { Label = GistLoc.T("关于", "About"), OnSelect = ShowAbout }
            };
        }

        private void WireControllers()
        {
            if (_toolbar == null || _emptyArea == null || _details == null || _list == null || _statusBar == null)
                return;

            var toolbar = _toolbar;
            var details = _details;

            // ---- 工具栏
            toolbar.SyncAllClicked += () => _ = SyncAllAsync();
            toolbar.Search.SearchChanged += OnSearchFieldChanged;

            // ---- 空态：清空搜索并重新聚焦搜索框
            if (_emptyArea.ClearFiltersButton != null)
                _emptyArea.ClearFiltersButton.text = GistLoc.T("清除搜索", "Clear Search");
            _emptyArea.ClearFiltersRequested += ClearSearchAndRebuild;

            // ---- 详情操作按钮
            details.UpdateRequested += SyncSelected;                      // Update → Sync
            details.RemoveRequested += RemoveSelected;                    // Remove → Remove
            var open = details.GetButton("quickStart");
            if (open != null)
                open.clicked += OpenSelectedInBrowser;
        }

        /// <summary>内联添加面板：附着在工具栏下方的输入面板，非独立窗口。</summary>
        private void BuildAddPanel()
        {
            var root = rootVisualElement;
            _addPanel = new VisualElement { name = "gistAddPanel" };
            var panel = _addPanel;
            panel.AddToClassList("gist-add-panel");
            panel.AddToClassList("gm-hidden");   // 默认隐藏，ToggleAddPanel 切换
            panel.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopPropagation();
                    SubmitAddPanel();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    HideAddPanel();
                }
            }, TrickleDown.TrickleDown);

            var title = new Label(GistLoc.T("从git链接添加Gist脚本", "Add Gist Script from git URL"));
            title.AddToClassList("gist-add-panel__title");
            panel.Add(title);

            var hint = new Label(GistLoc.T(
                "粘贴 GitHub Gist 链接，点 Add 添加。",
                "Paste a GitHub Gist URL and click Add to add it."));
            hint.AddToClassList("gist-add-panel__hint");
            panel.Add(hint);

            _addField = new TextField { name = "gistAddUrlField" };
            _addField.AddToClassList("gist-add-panel__field");
            _addField.RegisterValueChangedCallback(_ =>
            {
                UpdateAddPanelState(null);
            });
            panel.Add(_addField);
            // 空时叠加占位（pickingMode=Ignore，不拦截点击）
            _addPlaceholder = new Label("https://gist.github.com/{author}/{id}")
            {
                name = "gistAddPlaceholder",
                pickingMode = PickingMode.Ignore
            };
            _addPlaceholder.AddToClassList("gist-add-panel__placeholder");
            _addField.Add(_addPlaceholder);

            _addHintLabel = new Label(string.Empty);
            _addHintLabel.AddToClassList("gist-add-panel__hintlabel");
            panel.Add(_addHintLabel);

            var buttons = new VisualElement();
            buttons.AddToClassList("gist-add-panel__buttons");
            var cancel = new Button(HideAddPanel) { text = GistLoc.T("取消", "Cancel") };
            cancel.AddToClassList("gist-add-panel__cancel");
            buttons.Add(cancel);
            var add = new Button(SubmitAddPanel) { name = "gistAddConfirm", text = GistLoc.T("添加", "Add") };
            add.AddToClassList("gist-add-panel__confirm");
            buttons.Add(add);
            panel.Add(buttons);

            root.Add(panel);

            // 点击面板外的任何元素（不含添加面板/工具栏）也会关闭面板
            root.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (_addPanel == null || _addPanel.ClassListContains("gm-hidden"))
                    return;
                var target = evt.target as VisualElement;
                if (target == null)
                    return;
                if (_addPanel.Contains(target) || root.Q("toolbarAddMenu")?.Contains(target) == true)
                    return;
                HideAddPanel();
            }, TrickleDown.TrickleDown);
        }

        private void ToggleAddPanel()
        {
            if (_addPanel != null && !_addPanel.ClassListContains("gm-hidden"))
                HideAddPanel();
            else
                ShowAddPanel();
        }

        private void ShowAddPanel()
        {
            if (_addPanel == null)
                return;

            _addPanel.RemoveFromClassList("gm-hidden");

            if (_addField != null)
            {
                _addField.SetValueWithoutNotify(string.Empty);
                _addField.Focus();
            }

            if (_addHintLabel != null)
            {
                _addHintLabel.text = GistLoc.T(
                    "例如 https://gist.github.com/yasirkula/dfc43134",
                    "e.g. https://gist.github.com/yasirkula/dfc43134");
                _addHintLabel.RemoveFromClassList("gist-add-hint--error");
            }

            UpdateAddPanelState(null);
        }

        private void HideAddPanel()
        {
            _addPanel?.AddToClassList("gm-hidden");
        }

        /// <summary>根据输入内容刷新占位符可见性与 Add 按钮启用状态。</summary>
        private void UpdateAddPanelState(string? error)
        {
            if (_addField == null || _addPlaceholder == null)
                return;

            var text = _addField.value ?? string.Empty;
            _addPlaceholder.EnableInClassList("gm-hidden", !string.IsNullOrEmpty(text));

            var add = _addPanel?.Q<Button>("gistAddConfirm");
            if (add != null)
                add.SetEnabled(!string.IsNullOrWhiteSpace(text));
        }

        private void SubmitAddPanel()
        {
            if (_addField == null)
                return;

            var url = (_addField.value ?? string.Empty).Trim();
            string author, id, error;
            if (!GistUrlParser.TryParse(url, out author, out id, out error))
            {
                if (_addHintLabel != null)
                {
                    _addHintLabel.text = error;
                    _addHintLabel.AddToClassList("gist-add-hint--error");
                }

                return;
            }

            HideAddPanel();
            _ = AddGistAsync(url);
        }

        private void ShowSettingsPopup()
        {
            SettingsService.OpenProjectSettings(GistManagerSettingsProvider.SettingsPath);
        }


        private void ShowAbout()
        {
            EditorUtility.DisplayDialog(
                GistLoc.T("Gist管理器", "GistManager"),
                "MUD.Modules.GistManager 0.2.0\n\n" +
                GistLoc.T(
                    "以一份清单（gists.json）统一管理 GitHub Gist 脚本：GUI 添加/启用、显式 Sync 下载落盘。",
                    "Manage GitHub Gist snippets through a single source-of-truth manifest (gists.json), with GUI add/enable and an explicit sync step that downloads files into the project."),
                "OK");
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            if (_toolbar?.SyncAllButton != null)
                _toolbar.SyncAllButton.SetEnabled(!busy);
            _statusBar?.SetSpinnerVisible(busy);
        }

        // ------------------------------------------------------------------ 搜索（官方 PackageSearchBar 同款）

        private void OnSearchFieldChanged(string value)
        {
            _pendingSearch = value;
            _pendingSearchTicks = DateTime.Now.Ticks;
            EditorApplication.update -= PollSearchDelay;
            EditorApplication.update += PollSearchDelay;
        }

        private void PollSearchDelay()
        {
            var pending = _pendingSearch;
            if (pending == null)
            {
                EditorApplication.update -= PollSearchDelay;
                return;
            }

            if (DateTime.Now.Ticks - _pendingSearchTicks <= SearchDelayTicks)
                return;

            EditorApplication.update -= PollSearchDelay;
            _pendingSearch = null;
            ApplySearch(pending);
        }

        private void ApplySearch(string value)
        {
            if (string.Equals(value, _search, StringComparison.Ordinal))
                return;

            _search = value;
            RebuildList();
            UpdateStatusBar();
        }

        private void ClearSearchAndRebuild()
        {
            // 清单为空时，空态主按钮语义是“添加 gist”，直接打免费面板
            if (_manifest.gists.Count == 0)
            {
                ShowAddPanel();
                return;
            }

            if (_toolbar?.Search != null)
                _toolbar.Search.SetValueWithoutNotify(string.Empty);

            _search = string.Empty;
            _pendingSearch = null;
            EditorApplication.update -= PollSearchDelay;
            RebuildList();
            _toolbar?.Search.Field.Focus();
        }

        private void OnValidateCommand(ValidateCommandEvent evt)
        {
            if (evt.commandName == "Find")
                evt.StopPropagation();
        }

        private void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            if (evt.commandName != "Find")
                return;

            evt.StopPropagation();
            if (_toolbar?.Search != null)
                _toolbar.Search.Field.Focus();
        }

        // ------------------------------------------------------------------ 键盘导航（官方 ItemListScrollView.OnKeyDownShortcut 同款语义）

        private void OnKeyDown(KeyDownEvent evt)
        {
            var list = _list?.ScrollView;
            if (list == null)
                return;

            // Ctrl/Cmd+Shift+G：同步全部 Gist（窗口内作用域，不抢全局快捷键）
            if ((evt.ctrlKey || evt.commandKey) && evt.shiftKey && evt.keyCode == KeyCode.G)
            {
                evt.StopPropagation();
                _ = SyncAllAsync();
                return;
            }

            // 焦点在搜索框里时，方向键留给文本光标（与官方一致：只有列表侧获焦才拦截）
            if (evt.target is VisualElement target && _toolbar?.Search != null && _toolbar.Search.Root.Contains(target))
                return;

            int delta;
            bool toFirst, toLast;
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    delta = 1;
                    toFirst = false;
                    toLast = false;
                    break;
                case KeyCode.UpArrow:
                    delta = -1;
                    toFirst = false;
                    toLast = false;
                    break;
                case KeyCode.PageDown:
                    delta = 5;
                    toFirst = false;
                    toLast = false;
                    break;
                case KeyCode.PageUp:
                    delta = -5;
                    toFirst = false;
                    toLast = false;
                    break;
                case KeyCode.Home:
                    delta = 0;
                    toFirst = true;
                    toLast = false;
                    break;
                case KeyCode.End:
                    delta = 0;
                    toFirst = false;
                    toLast = true;
                    break;
                default:
                    return;
            }

            evt.StopPropagation();
            MoveSelection(delta, toFirst, toLast);
        }

        /// <summary>在可见行间移动选中：首次按方向键时落到首/尾行，越界处夹紧。</summary>
        private void MoveSelection(int delta, bool toFirst, bool toLast)
        {
            var rows = new List<VisualElement>();
            CollectVisibleRows(_list?.ScrollView, rows);
            if (rows.Count == 0)
                return;

            var index = _selectedGistIds.Count > 0
                ? rows.FindIndex(r => string.Equals(RowGistId(r), _selectedGistIds[_selectedGistIds.Count - 1], StringComparison.Ordinal))
                : -1;
            if (toFirst)
                index = 0;
            else if (toLast)
                index = rows.Count - 1;
            else if (index < 0)
                index = delta > 0 ? 0 : rows.Count - 1;
            else
                index = Mathf.Clamp(index + delta, 0, rows.Count - 1);

            var id = RowGistId(rows[index]);
            if (id == null || id.Length == 0)
                return;

            SelectGist(id);
            ScrollToSelection();
        }

        /// <summary>按视觉顺序收集当前可见的行；折叠组内的行不算可见，不参与键盘导航。</summary>
        private static void CollectVisibleRows(VisualElement? node, List<VisualElement> rows)
        {
            if (node == null || !IsShown(node))
                return;

            var id = RowGistId(node);
            if (id != null)
            {
                rows.Add(node);
                return;
            }

            foreach (var child in node.Children())
                CollectVisibleRows(child, rows);
        }

        /// <summary>自身到根链路上没有任何 display:none / visibility:hidden 才算可见。
        /// 必须沿 hierarchy.parent 走：折叠时 ApplyRowDisplay 把 display:none 打在行元素自身/rowContent 上，
        /// 而 .parent 返回的是逻辑父级，会跳过 TemplateContainer 包装层。</summary>
        private static bool IsShown(VisualElement element)
        {
            if (element.panel == null)
                return false;

            for (var e = element; e != null; e = e.hierarchy.parent)
            {
                var style = e.resolvedStyle;
                if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden)
                    return false;
            }

            return true;
        }

        /// <summary>行元素名固定为 "row-" + gist id；非行元素返回 null。</summary>
        private static string? RowGistId(VisualElement element)
        {
            var name = element.name;
            return name != null && name.StartsWith("row-", StringComparison.Ordinal) ? name.Substring(4) : null;
        }

        // ------------------------------------------------------------------ 左栏列表（官方 ItemListScrollView/ListArea 同款语义）

        private void RebuildList()
        {
            var list = _list?.ScrollView;
            if (list == null)
                return;

            _rows.Clear();
            _groupControllers.Clear();
            list.contentContainer.Clear();

            var searching = !string.IsNullOrWhiteSpace(_search);
            var authors = _manifest.gists
                .GroupBy(g => string.IsNullOrEmpty(g.author) ? GistUrlParser.AnonymousAuthor : g.author)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            var total = 0;
            foreach (var group in authors)
            {
                var entries = group.ToList();
                var visible = searching ? entries.Where(e => GistSearch.Matches(e, _search)).ToList() : entries;
                if (visible.Count == 0)
                    continue;

                total += visible.Count;
                list.contentContainer.Add(BuildGroup(group.Key, visible, searching));
            }

            // 空状态（官方 ListArea.UpdateListVisibility 文案语义）
            // 四种组合：清单空/非空 × 搜索无命中/无搜索。清单为空时主按钮改为"添加 gist"，
            // 否则（非空但搜索 0 命中）主按钮为"清除搜索"。
            var manifestEmpty = _manifest.gists.Count == 0;
            string message;
            if (total > 0)
                message = string.Empty;
            else if (manifestEmpty)
                message = GistLoc.T("还没有 gist。\n点击“+ → 从git链接添加Gist脚本...”，或直接编辑 " + GistManifestStore.ManifestPath + "。",
                                    "No gists yet.\nClick “+ → Add Gist Script from git URL...”, or edit " + GistManifestStore.ManifestPath + " directly.");
            else
                message = GistSearch.NoResultsMessage(_search);

            _list?.SetEmptyState(total == 0);
            if (_emptyArea?.Message != null)
                _emptyArea.Message.text = message;
            if (_emptyArea?.ClearFiltersButton != null)
                _emptyArea.ClearFiltersButton.text = manifestEmpty
                    ? GistLoc.T("从git链接添加Gist...", "Add gist from git URL...")
                    : GistLoc.T("清除搜索", "Clear Search");
            EnsureValidSelection();
            RefreshRowSelection();
            ScrollToSelection();
        }

        /// <summary>作者分组：GroupHeader（caret + 作者名）；搜索时强制展开。</summary>
        private VisualElement BuildGroup(string author, List<GistEntry> entries, bool searching)
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GroupHeaderUxmlPath);
            var groupElement = template != null ? template.Instantiate() : new VisualElement { name = "group-" + author };
            groupElement.name = "group-" + author;

            var controller = new GroupHeaderController(groupElement);
            controller.CollapsedChanged += collapsed =>
            {
                _authorFoldouts[author] = !collapsed;
                SaveFoldouts();
            };
            _groupControllers.Add(controller);

            var nameLabel = controller.HeaderTitle;
            if (nameLabel != null)
                nameLabel.text = author;

            var container = controller.GroupContainer;
            if (container != null)
            {
                foreach (var entry in entries)
                    container.Add(BuildRow(entry));
            }

            // 折叠状态在行挂载之后应用，否则 ApplyRowDisplay 派发不到新行
            var expanded = searching || GetFoldout(_authorFoldouts, author, true);
            controller.SetCollapsed(!expanded);

            return groupElement;
        }

        /// <summary>列表行：24px 高，标题 + 短 id（灰显）+ 安装态图标，选中/折叠联动。</summary>
        private VisualElement BuildRow(GistEntry entry)
        {
            var url = GistUrlParser.GistWebUrl(entry.author, entry.id);
            var selected = _selectedGistIds.Contains(entry.id);
            var allEnabled = entry.files.Count > 0 && entry.files.All(f => f.enabled);

            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ItemRowUxmlPath);
            var row = template != null ? template.Instantiate() : new VisualElement { name = "row-" + entry.id };
            row.name = "row-" + entry.id;
            row.AddToClassList("list-item");
            row.AddToClassList("id-version");
            if (!allEnabled)
                row.AddToClassList("is-disabled");
            row.tooltip = string.IsNullOrEmpty(entry.description) ? url : entry.description + "\n" + url;
            row.focusable = true;
            row.tabIndex = selected ? 0 : -1;

            var nameLabel = row.Q<Label>("gistName");
            if (nameLabel != null)
                nameLabel.text = GistEntryLabel(entry);

            // numPackages 槽位在 gist 中不显示文件数；与 selected 联动显隐
            var countLabel = row.Q<Label>("numPackages");
            if (countLabel != null)
                countLabel.AddToClassList("gm-hidden");

            var version = row.Q<Label>("versionLabel");
            if (version != null)
                version.text = entry.id.Length > 8 ? entry.id.Substring(0, 8) : entry.id;

            var rowStateIcon = row.Q<VisualElement>("rowStateIcon");
            if (rowStateIcon != null)
            {
                if (allEnabled)
                {
                    rowStateIcon.AddToClassList("installed");
                    rowStateIcon.RemoveFromClassList("gm-hidden");
                }
                else
                    rowStateIcon.AddToClassList("gm-hidden");
            }

            var main = row.Q<VisualElement>("rowContent");
            if (main != null)
                main.EnableInClassList("selected", selected);
            row.EnableInClassList("selected", selected);

            row.userData = entry;
            _rows[entry.id] = row;

            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0)
                    return;

                var additive = evt.ctrlKey || evt.commandKey;
                var range = evt.shiftKey;
                SelectGist(entry.id, additive, range);
                if (_rows.TryGetValue(entry.id, out var clicked))
                    clicked.Focus();
            });
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                // 右键未选中行：先选择该行（右键菜单作用于行）
                if (!_selectedGistIds.Contains(entry.id))
                    SelectGist(entry.id);
                PopulateContextMenu(evt.menu, entry, url);
            }));

            return row;
        }

        /// <summary>行右键菜单：操作收进上下文菜单；多选时作用于所选集合。</summary>
        private void PopulateContextMenu(DropdownMenu menu, GistEntry entry, string url)
        {
            var entries = FindSelectedEntries();
            if (entries.Count == 0 || !entries.Contains(entry))
                entries = new List<GistEntry> { entry };
            var multi = entries.Count > 1;

            // 多选时 Open in Browser 无明确目标，禁用
            if (multi)
            {
                menu.AppendAction(GistLoc.T("在浏览器打开", "Open in Browser"),
                    action => Application.OpenURL(url), DropdownMenuAction.Status.Disabled);
            }
            else
            {
                menu.AppendAction(GistLoc.T("在浏览器打开", "Open in Browser"),
                    action => Application.OpenURL(url));
            }
            menu.AppendSeparator("");

            menu.AppendAction(multi
                    ? GistLoc.T("同步所选（" + entries.Count + "）", "Sync selected (" + entries.Count + ")")
                    : GistLoc.T("同步", "Sync"),
                action => { _ = SyncEntries(new List<GistEntry>(entries)); });
            menu.AppendSeparator("");

            var allEnabled = entries.All(e => e.files.Count > 0 && e.files.All(f => f.enabled));
            var enableLabel = multi
                ? GistLoc.T(allEnabled ? "禁用所选" : "启用所选", allEnabled ? "Disable selected" : "Enable selected")
                : GistLoc.T(allEnabled ? "禁用整个包" : "启用整个包", allEnabled ? "Disable Package" : "Enable Package");
            menu.AppendAction(enableLabel, action => SetAllEnabledForEntries(entries, !allEnabled));
            menu.AppendSeparator("");

            menu.AppendAction(multi
                    ? GistLoc.T("从清单移除所选（" + entries.Count + "）", "Remove selected (" + entries.Count + ") from Manifest")
                    : GistLoc.T("从清单移除", "Remove from Manifest"),
                action => ConfirmAndRemoveGists(entries));
        }

        /// <summary>批量启用/禁用：每个条目走现有 SetAllEnabled 逻辑。</summary>
        private void SetAllEnabledForEntries(List<GistEntry> entries, bool enabled)
        {
            foreach (var e in entries)
                SetAllEnabled(e, enabled);
        }

        /// <summary>批量从清单移除（不碰磁盘文件）；多选时先确认。</summary>
        private void ConfirmAndRemoveGists(List<GistEntry> entries)
        {
            if (entries.Count == 0)
                return;

            if (entries.Count > 1)
            {
                var ok = EditorUtility.DisplayDialog(
                    GistLoc.T("从清单移除所选？", "Remove selected from manifest?"),
                    GistLoc.T("将从清单移除 " + entries.Count + " 个 gist——磁盘文件不受影响。继续？",
                              "Remove " + entries.Count + " gists from the manifest — files on disk are left untouched. Continue?"),
                    GistLoc.T("移除", "Remove"),
                    GistLoc.T("取消", "Cancel"));
                if (!ok)
                    return;
            }

            RemoveGists(entries);
        }

        private void RemoveGists(List<GistEntry> entries)
        {
            var removedAny = false;
            foreach (var entry in entries)
                removedAny |= _manifest.gists.Remove(entry);
            if (!removedAny)
                return;

            SaveManifest();
            _selectedGistIds.Clear();
            _anchorGistId = string.Empty;
            _status = GistLoc.T("已从清单移除 " + entries.Count + " 个 gist（磁盘文件保留）。",
                                "Removed " + entries.Count + " gists from the manifest (files on disk were left untouched).");
            ScheduleManifestChanged();
        }

        private void SelectGist(string id)
        {
            SelectGist(id, additive: false, range: false);
        }

        /// <summary>选中一行（additive = Ctrl/Cmd 切换；range = Shift 从锚点扩展选区）。</summary>
        private void SelectGist(string id, bool additive, bool range)
        {
            if (range && _anchorGistId.Length > 0)
                RangeSelectTo(id);
            else if (additive)
                ToggleSelect(id);
            else
            {
                if (_selectedGistIds.Count == 1 && string.Equals(_selectedGistIds[0], id, StringComparison.Ordinal))
                    return;
                _selectedGistIds.Clear();
                _selectedGistIds.Add(id);
                _anchorGistId = id;
            }

            RefreshRowSelection();
            FocusSelectedRow();
            UpdateDetails();
            UpdateStatusBar();
        }

        private void ToggleSelect(string id)
        {
            if (!_selectedGistIds.Remove(id))
                _selectedGistIds.Add(id);
            _anchorGistId = id;
        }

        private void RangeSelectTo(string id)
        {
            var rows = new List<VisualElement>();
            CollectVisibleRows(_list?.ScrollView, rows);

            var anchorIndex = rows.FindIndex(r => string.Equals(RowGistId(r), _anchorGistId, StringComparison.Ordinal));
            var targetIndex = rows.FindIndex(r => string.Equals(RowGistId(r), id, StringComparison.Ordinal));
            if (anchorIndex < 0 || targetIndex < 0)
            {
                SingleSelectFallback(id);
                return;
            }

            var from = Math.Min(anchorIndex, targetIndex);
            var to = Math.Max(anchorIndex, targetIndex);
            _selectedGistIds.Clear();
            for (var i = from; i <= to; i++)
            {
                var rowId = RowGistId(rows[i]);
                if (!string.IsNullOrEmpty(rowId))
                    _selectedGistIds.Add(rowId!);
            }
            // 锚点保持不变，便于连续 Shift+Click 扩展选区
        }

        private void SingleSelectFallback(string id)
        {
            _selectedGistIds.Clear();
            _selectedGistIds.Add(id);
            _anchorGistId = id;
        }

        private void RefreshRowSelection()
        {
            var lastSelected = _selectedGistIds.Count > 0 ? _selectedGistIds[_selectedGistIds.Count - 1] : null;
            foreach (var pair in _rows)
            {
                var isSelected = _selectedGistIds.Contains(pair.Key);
                pair.Value.EnableInClassList("selected", isSelected);
                // 官方 BaseListItem 同款：只有最后/唯一选中行进 Tab 序（tabIndex 0，其余 -1）
                pair.Value.tabIndex = isSelected && string.Equals(pair.Key, lastSelected, StringComparison.Ordinal) ? 0 : -1;
                var main = pair.Value.Q<VisualElement>("rowContent");
                if (main != null)
                    main.EnableInClassList("selected", isSelected);
            }

            // 折叠组内也要和选中状态保持一致（选中行保持可见，未选中行保持隐藏）
            foreach (var controller in _groupControllers)
                controller.ApplyRowDisplay();
        }

        /// <summary>焦点跟随选中（官方 gistListView item.Focus() 同款）。
        /// UI Toolkit 面板在没有任何焦点元素时会丢弃按键事件，所以选中行必须持焦，方向键才收得到。</summary>
        private void FocusSelectedRow()
        {
            if (_selectedGistIds.Count == 0)
                return;

            var last = _selectedGistIds[_selectedGistIds.Count - 1];
            if (_rows.TryGetValue(last, out var row) && row != null && row.panel != null)
                row.Focus();
        }

        private void FocusSelectionIfNone()
        {
            var panel = rootVisualElement != null ? rootVisualElement.panel : null;
            var focusController = panel != null ? panel.focusController : null;
            if (focusController != null && focusController.focusedElement == null)
                FocusSelectedRow();
        }

        /// <summary>重建后把选中行滚动到可见（官方 ScrollToSelection 语义：布局就绪后再滚）。</summary>
        private void ScrollToSelection()
        {
            var list = _list?.ScrollView;
            if (list == null)
                return;

            if (_selectedGistIds.Count == 0)
                return;

            var last = _selectedGistIds[_selectedGistIds.Count - 1];
            if (!_rows.TryGetValue(last, out var row) || row == null)
                return;

            row.schedule.Execute(() =>
            {
                if (row.panel != null)
                    list.ScrollTo(row);
            });
        }

        // ------------------------------------------------------------------ 详情面板

        private GistEntry? FindSelected()
        {
            return FindSelectedEntries().FirstOrDefault();
        }

        /// <summary>返回按清单顺序排好的选中条目列表。</summary>
        private List<GistEntry> FindSelectedEntries()
        {
            if (_selectedGistIds.Count == 0)
                return new List<GistEntry>();

            var set = new HashSet<string>(_selectedGistIds, StringComparer.Ordinal);
            return _manifest.gists.Where(g => set.Contains(g.id)).ToList();
        }

        private void EnsureValidSelection()
        {
            // 清理已失效的选择（如 RemoveGists 清空了集合、或手动编辑清单导致条目消失）
            for (var i = _selectedGistIds.Count - 1; i >= 0; i--)
            {
                var id = _selectedGistIds[i];
                if (!_manifest.gists.Exists(g => g.id == id))
                    _selectedGistIds.RemoveAt(i);
            }

            if (_selectedGistIds.Count > 0)
                return;

            if (_manifest.gists.Count > 0)
                _selectedGistIds.Add(_manifest.gists[0].id);
            _anchorGistId = _selectedGistIds.Count > 0 ? _selectedGistIds[0] : string.Empty;
        }

        /// <summary>多选详情区：信息行 + 4 个批量操作按钮；只构建一次，按钮读取实时选择。</summary>
        private void BuildMultiSelectSection()
        {
            var multi = _details?.MultiSelectDetails;
            if (multi == null)
                return;

            var infoHost = multi.Q<VisualElement>("multiSelectInfoBoxContainer");
            if (infoHost != null)
            {
                _multiSelectInfo = new Label();
                _multiSelectInfo.AddToClassList("gist-multiselect__info");
                infoHost.Add(_multiSelectInfo);
            }

            var fold = multi.Q<VisualElement>("multiSelectFoldoutsContainer");
            if (fold == null)
                return;

            fold.AddToClassList("gist-multiselect__folds");

            fold.Add(MakeMultiButton(
                GistLoc.T("同步所选", "Sync selected"),
                () => { var entries = FindSelectedEntries(); if (entries.Count > 0) _ = SyncEntries(entries); }));
            fold.Add(MakeMultiButton(
                GistLoc.T("全部启用", "Enable all"),
                () => SetAllEnabledForEntries(FindSelectedEntries(), true)));
            fold.Add(MakeMultiButton(
                GistLoc.T("全部禁用", "Disable all"),
                () => SetAllEnabledForEntries(FindSelectedEntries(), false)));
            fold.Add(MakeMultiButton(
                GistLoc.T("从清单移除所选", "Remove selected from manifest"),
                () => ConfirmAndRemoveGists(FindSelectedEntries())));
        }

        private Button MakeMultiButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("gist-multiselect__button");
            return b;
        }

        /// <summary>右栏空态：清单为空 / 无选中时居中显示添加引导（复用 Add 对话框分支）。</summary>
        private void BuildDetailsEmptyState()
        {
            var sv = _details?.DetailScrollView;
            if (sv == null)
                return;

            _detailsEmptyState = new VisualElement { name = "detailsEmptyState" };
            _detailsEmptyState.AddToClassList("gist-details-empty");
            _detailsEmptyState.AddToClassList("gm-hidden");

            var title = new Label(GistLoc.T("还没有 gist", "No gists yet"));
            title.AddToClassList("gist-details-empty__title");
            _detailsEmptyState.Add(title);

            var hint = new Label(GistLoc.T(
                "粘贴一个 GitHub Gist 链接，或从 gists.json 里手写一个条目。",
                "Paste a GitHub Gist URL, or add an entry in gists.json."));
            hint.AddToClassList("gist-details-empty__hint");
            _detailsEmptyState.Add(hint);

            var add = new Button(() => ShowAddPanel())
            {
                text = GistLoc.T("从git链接添加Gist脚本...", "Add Gist Script from git URL..."),
                tooltip = GistLoc.T("粘贴 GitHub Gist 链接后添加到清单", "Add a gist by pasting its URL")
            };
            _detailsEmptyState.Add(add);

            sv.Add(_detailsEmptyState);
        }

        /// <summary>在 descriptionTab 内构建文件编辑区（ToggleAll + 文件行容器 + 说明尾注），只执行一次。</summary>
        private void BuildFilesSection()
        {
            BuildDetailsEmptyState();
            BuildMultiSelectSection();

            var tab = _details?.GetElement("descriptionTab");
            if (tab == null)
                return;

            var filesHeader = new Label(GistLoc.T("文件", "Files"));
            filesHeader.AddToClassList("gist-files__header");
            tab.Add(filesHeader);

            _toggleAll = new Toggle
            {
                label = GistLoc.T("启用整个包", "Enable whole package"),
                tooltip = GistLoc.T("关闭时移除磁盘上已下载的文件并记录其 .meta",
                                    "Disabling removes downloaded files from disk and records their .meta")
            };
            _toggleAll.RegisterValueChangedCallback(evt =>
            {
                var entry = FindSelected();
                if (entry == null)
                    return;
                SetAllEnabled(entry, evt.newValue);
            });
            tab.Add(_toggleAll);

            _filesContainer = new VisualElement { name = "filesContainer" };
            _filesContainer.AddToClassList("gist-files__container");
            tab.Add(_filesContainer);

            var hint = new Label(GistLoc.T(
                "改动会立即写入 gists.json；下载/移除由 Sync 或启用开关触发。",
                "Changes are written to gists.json immediately; download/remove is triggered by Sync or the enable toggles."));
            hint.AddToClassList("gist-files__hint");
            tab.Add(hint);
        }

        private void UpdateDetails()
        {
            var details = _details;
            if (details == null)
                return;

            var entries = FindSelectedEntries();
            if (entries.Count == 0)
            {
                details.SetMultiSelectMode(false);
                Hide(details.Detail);
                Hide(details.DetailsBody);
                if (_detailsEmptyState != null)
                    _detailsEmptyState.RemoveFromClassList("gm-hidden");
                return;
            }

            if (_detailsEmptyState != null)
                _detailsEmptyState.AddToClassList("gm-hidden");

            // 多选：折叠 `folds` 区块里有 4 个批量操作按钮
            if (entries.Count > 1)
            {
                var totalFiles = entries.Sum(e => e.files.Count);
                var enabledFiles = entries.Sum(e => e.files.Count(f => f.enabled));

                details.SetMultiSelectMode(true);
                Hide(details.DetailsBody);
                details.SetMultiSelectTitle(GistLoc.T(
                    "已选择 " + entries.Count + " 个 gist",
                    entries.Count + " gists selected"));
                if (_multiSelectInfo != null)
                {
                    _multiSelectInfo.text = GistLoc.T(
                        "共 " + totalFiles + " 个文件（" + enabledFiles + " 个已启用）",
                        totalFiles + " files (" + enabledFiles + " enabled)");
                }
                return;
            }

            details.SetMultiSelectMode(false);
            Show(details.Detail);
            Show(details.DetailsBody);
            var entry = entries[0];

            var url = GistUrlParser.GistWebUrl(entry.author, entry.id);
            var allEnabled = entry.files.Count > 0 && entry.files.All(f => f.enabled);

            details.SetTitle(GistEntryLabel(entry));
            details.SetVersion(entry.id.Length > 8 ? entry.id.Substring(0, 8) : entry.id);
            details.SetDetailName(entry.author + "/" + entry.id);
            details.SetRegistryAndAuthor(GistLoc.T("来自 <b>GitHub Gist</b>，作者 ", "From <b>GitHub Gist</b> by ") + entry.author);
            details.SetSourcePath(_manifest.NormalizedTargetRoot + "/" + entry.packageDir);
            details.SetDescription(string.IsNullOrEmpty(entry.description)
                ? GistLoc.T("暂无描述。", "No description.")
                : entry.description);

            if (details.SourcePathLabel != null)
                details.SourcePathLabel.tooltip = GistLoc.T("目标文件夹：", "Target folder: ") + _manifest.NormalizedTargetRoot + "/" + entry.packageDir;

            var open = details.GetButton("quickStart");
            if (open != null)
                open.tooltip = GistLoc.T("在浏览器打开该 Gist 的链接：", "Open this gist link in browser: ") + url;

            RefreshFileRows(entry, allEnabled);
        }

        /// <summary>按选中 Gist 重建文件编辑区（整包开关 + 每文件行）。</summary>
        private void RefreshFileRows(GistEntry entry, bool allEnabled)
        {
            if (_toggleAll != null)
            {
                _toggleAll.label = GistLoc.T(
                    "启用整个包（" + entry.files.Count + " 个文件）",
                    "Enable whole package (" + entry.files.Count + " files)");
                _toggleAll.SetValueWithoutNotify(allEnabled);
            }

            var container = _filesContainer;
            if (container == null)
                return;

            container.Clear();

            if (entry.files.Count == 0)
            {
                var empty = new Label(GistLoc.T(
                    "清单里这个 Gist 还没有文件，点 Sync 从上游拉取文件列表。",
                    "This gist has no files in the manifest yet. Press Sync to fetch the file list from upstream."));
                empty.AddToClassList("gist-files__empty");
                container.Add(empty);
                return;
            }

            foreach (var file in entry.files.ToArray())
                container.Add(BuildFileRow(entry, file));
        }

        private VisualElement BuildFileRow(GistEntry entry, GistFileEntry file)
        {
            var box = new VisualElement();
            box.AddToClassList("gist-file-row");

            // ---- 第一行：启用开关 / 归属下拉 / 移除
            var row1 = new VisualElement();
            row1.AddToClassList("gist-file-row__row1");

            var toggle = new Label
            {
                text = GistNiceName.ForFilename(file.filename),
                tooltip = file.filename
            };
            toggle.AddToClassList("gist-file-row__toggle");
            row1.Add(toggle);

            var placementIndex = PlacementIndexOf(file.placement);
            var placement = new DropdownField
            {
                tooltip = GistLoc.T("Auto：同步时按内容探测归属；Editor/Runtime：固定落在对应子目录并挂对应 asmdef",
                                    "Auto: detect placement by content on sync; Editor/Runtime: always download into the matching subfolder with the matching asmdef")
            };
            placement.choices = PlacementLabels.ToList();
            placement.index = placementIndex;
            placement.AddToClassList("gist-file-row__placement");
            placement.RegisterValueChangedCallback(evt =>
            {
                var picked = PlacementLabels.ToList().IndexOf(evt.newValue);
                if (picked >= 0 && picked != placementIndex)
                    ApplyPlacement(entry, file, picked);
            });
            row1.Add(placement);

            var remove = new Button { text = "X", tooltip = GistLoc.T(
                "从清单移除该文件（不碰磁盘文件）",
                "Remove this file from the manifest (does not touch the file on disk)") };
            remove.AddToClassList("gist-file-row__remove");
            remove.clicked += () =>
            {
                entry.files.Remove(file);
                SaveManifest();
                ScheduleManifestChanged();
            };
            row1.Add(remove);

            box.Add(row1);

            // ---- 第二行：目标路径
            var row2 = new VisualElement();
            row2.AddToClassList("gist-file-row__row2");

            var pathLabel = new Label(GistLoc.T("路径", "Path"));
            pathLabel.AddToClassList("gist-file-row__path-label");
            row2.Add(pathLabel);

            var pathField = new TextField
            {
                value = file.targetPath,
                isDelayed = true,
                tooltip = GistLoc.T("项目相对路径；路径里的 /Editor/、/Runtime/ 段会反推归属",
                                    "Project-relative path; /Editor/ or /Runtime/ segments in the path drive placement")
            };
            pathField.AddToClassList("gist-file-row__path-field");
            pathField.RegisterValueChangedCallback(evt => ApplyTargetPath(entry, file, evt.newValue));
            row2.Add(pathField);

            box.Add(row2);

            // ---- 第三行（可选）：本地有改动警示 + 强制覆盖
            if (GistSyncService.IsLocallyModified(file))
            {
                var modifiedRow = new VisualElement();
                modifiedRow.AddToClassList("gist-file-row__modified");

                var warn = new Label(GistLoc.T(
                    "本地有改动，同步时会跳过",
                    "Locally modified — skipped on sync"));
                warn.AddToClassList("gist-file-row__warn");
                modifiedRow.Add(warn);

                var overwrite = new Button
                {
                    text = GistLoc.T("覆盖下载", "Overwrite"),
                    tooltip = GistLoc.T("用上游内容强制覆盖本地文件——本地改动会丢失",
                                        "Overwrite with upstream content — local changes will be lost")
                };
                overwrite.AddToClassList("gist-file-row__overwrite");
                overwrite.clicked += () => OverwriteFile(entry, file);
                modifiedRow.Add(overwrite);

                box.Add(modifiedRow);
            }

            // ---- 第三行（可选）：已记录 .meta 提示
            if (!string.IsNullOrEmpty(file.meta))
            {
                var meta = new Label(GistLoc.T(
                    "已记录 .meta，重新启用同步时会写回",
                    ".meta recorded; it will be restored on the next enable + sync."));
                meta.AddToClassList("gist-file-row__meta");
                box.Add(meta);
            }

            return box;
        }

        private void ScheduleManifestChanged()
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                    return;
                ManifestChanged();
            };
        }

        // ------------------------------------------------------------------ 状态栏

        private void UpdateStatusBar()
        {
            var status = _statusBar;
            if (status == null)
                return;

            var lastSync = GistManagerSettings.instance.lastSyncUtc;
            var text = _status.Length > 0
                ? _status
                : lastSync.Length > 0
                    ? GistLoc.T("最近同步：", "Last sync: ") + lastSync
                    : GistLoc.T("从未同步", "Never synced");

            status.SetStatusText(text);
            status.SetErrorVisible(!string.IsNullOrEmpty(_loadError));
            status.SetSpinnerVisible(_busy);
            if (status.ErrorIcon != null && !string.IsNullOrEmpty(_loadError))
                status.ErrorIcon.tooltip = _loadError + "\n" +
                    GistLoc.T("修复或删除 " + GistManifestStore.ManifestPath + "，然后点 More → Reload Manifest（重新加载清单）。",
                              "Fix or delete " + GistManifestStore.ManifestPath + ", then press More → Reload Manifest.");
        }

        // ------------------------------------------------------------------ 动作

        private async void SyncSelected()
        {
            var entry = FindSelected();
            if (entry == null)
                return;

            await SyncEntries(new List<GistEntry> { entry });
        }

        private void OpenSelectedInBrowser()
        {
            var entry = FindSelected();
            if (entry == null)
                return;

            Application.OpenURL(GistUrlParser.GistWebUrl(entry.author, entry.id));
        }

        /// <summary>强制用上游内容覆盖单个文件（会丢失本地改动，先确认）。</summary>
        private async void OverwriteFile(GistEntry entry, GistFileEntry file)
        {
            if (_busy)
                return;

            var ok = EditorUtility.DisplayDialog(
                GistLoc.T("强制覆盖？", "Overwrite local changes?"),
                GistLoc.T(
                    "文件 “" + file.filename + "” 有本地改动，覆盖会用上游内容替换它，本地改动将丢失。继续？",
                    "\"" + file.filename + "\" has local modifications. Overwriting replaces it with upstream content and your local changes will be lost. Continue?"),
                GistLoc.T("覆盖", "Overwrite"),
                GistLoc.T("取消", "Cancel"));
            if (!ok)
                return;

            // 覆盖前：被覆盖的本地文件先复制到 Backup~/Overwritten（不覆盖、不删除，只是保险）
            GistSyncService.BackupOverwritten(_manifest, file);

            await RunBusy(GistLoc.T("正在覆盖 " + file.filename + " …", "Overwriting " + file.filename + " ..."), async delegate
            {
                // raw_url 可能缺失：先拉一次上游元数据补齐
                if (string.IsNullOrEmpty(file.rawUrl))
                {
                    var metadata = await GistApiClient.FetchGistAsync(entry.id);
                    GistCatalog.BackfillRawUrls(entry, metadata);
                }

                var result = await GistSyncService.SyncOneFileAsync(_manifest, entry, file, status => _status = status, force: true);
                SaveManifest();
                _status = result == GistSyncService.SyncFileResult.Written
                    ? GistLoc.T("已覆盖 " + file.filename, "Overwritten " + file.filename)
                    : GistLoc.T("覆盖失败：" + file.filename, "Overwrite failed: " + file.filename);
            });
        }

        /// <summary>在文件管理器里打开本地 gist 包目录（targetRoot/{packageDir}）；目录不存在时退到目标根（root 也不存在则退到工程根）。</summary>
        private void OpenSelectedFolder()
        {
            var entry = FindSelected();
            if (entry == null)
                return;

            string dir;
            try
            {
                var packageDir = entry.packageDir ?? string.Empty;
                var relative = packageDir.Length > 0
                    ? _manifest.NormalizedTargetRoot + "/" + packageDir
                    : _manifest.NormalizedTargetRoot;

                var packageFull = GistManifestStore.ToFullPath(relative);
                if (Directory.Exists(packageFull))
                    dir = packageFull;
                else
                {
                    var rootFull = GistManifestStore.ToFullPath(_manifest.NormalizedTargetRoot);
                    dir = Directory.Exists(rootFull) ? rootFull : GistManifestStore.ProjectRoot;
                }
            }
            catch (Exception)
            {
                dir = GistManifestStore.ProjectRoot;
            }

            EditorUtility.RevealInFinder(dir);
        }

        private void RemoveSelected()
        {
            ConfirmAndRemoveGists(FindSelectedEntries());
        }

        private async Task AddGistAsync(string url)
        {
            if (_busy)
                return;

            string author, id, error;
            if (!GistUrlParser.TryParse(url, out author, out id, out error))
            {
                _status = error;
                UpdateStatusBar();
                return;
            }

            await RunBusy(GistLoc.T("正在获取 " + author + "/" + id + " …", "Fetching gist " + author + "/" + id + " ..."), async delegate
            {
                var metadata = await GistApiClient.FetchGistAsync(id);
                var entry = GistCatalog.AddOrUpdate(_manifest, metadata);
                SaveManifest();
                _selectedGistIds.Clear();
                _selectedGistIds.Add(entry.id);
                _anchorGistId = entry.id;
                SetFoldout(_authorFoldouts, entry.author, true);
                _status = GistLoc.T(
                    "已添加 " + entry.author + "/" + entry.id + "（" + entry.files.Count + " 个文件）",
                    "Added " + entry.author + "/" + entry.id + " (" + entry.files.Count + " file(s))");
            });
        }

        private async Task SyncAllAsync()
        {
            if (_busy)
                return;

            await SyncEntries(_manifest.gists.ToList());
        }

        private async Task SyncEntries(List<GistEntry> entries)
        {
            if (_busy)
                return;

            if (entries == null || entries.Count == 0)
            {
                _status = GistLoc.T("没有要同步的内容。", "Nothing to sync.");
                UpdateStatusBar();
                return;
            }

            await RunBusy(GistLoc.T("正在同步 " + entries.Count + " 个 gist …", "Syncing " + entries.Count + " gist(s) ..."), async delegate
            {
                var written = await GistSyncService.SyncAsync(_manifest, entries, status => _status = status);
                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var settings = GistManagerSettings.instance;
                settings.lastSyncUtc = stamp;
                settings.Save();
                SaveManifest();
                _status = GistLoc.T(
                    "同步完成：已写入 " + written + " 个文件，时间 " + stamp,
                    "Sync finished: " + written + " file(s) written at " + stamp);
            });
        }

        private void RemoveGist(GistEntry entry)
        {
            RemoveGists(new List<GistEntry> { entry });
        }

        /// <summary>整包启用/禁用：逐文件应用，禁用时连磁盘文件一起移除并记录 meta。</summary>
        private void SetAllEnabled(GistEntry entry, bool enabled)
        {
            // 整包级别：启用/禁用整棵目录（含 asmdef、.meta）一起搬家到 Backup~/Disabled 或恢复
            if (!enabled)
                GistSyncService.BackupGistPackage(_manifest, entry);
            else
                GistSyncService.TryRestoreGistPackage(_manifest, entry);

            foreach (var file in entry.files)
                file.enabled = enabled;

            SaveManifest();
            EditorApplication.delayCall += RefreshAssets;

            ScheduleManifestChanged();
        }

        private void ReloadManifest()
        {
            string? error;
            _manifest = GistManifestStore.LoadOrCreate(out error);
            _loadError = error;
            _manifestWriteTimeUtc = GistManifestStore.ManifestWriteTimeUtc;

            if (string.IsNullOrEmpty(error) && GistCatalog.Normalize(_manifest))
                SaveManifest();

            EnsureValidSelection();
            _status = string.IsNullOrEmpty(error)
                ? GistLoc.T("已加载 " + _manifest.gists.Count + " 个 gist。", "Loaded " + _manifest.gists.Count + " gist(s).")
                : (error ?? string.Empty);
            ManifestChanged();
        }

        private void SaveManifest()
        {
            // 清单解析失败时绝不写盘：LoadOrCreate 已把空清单放在 _manifest 里，直接保存会把用户的原文件覆盖掉
            if (!string.IsNullOrEmpty(_loadError))
            {
                _status = GistLoc.T(
                    "gists.json 解析失败，已跳过保存——请先修复该文件（见状态栏错误信息；原文件已备份），修复后改动会自动读回。",
                    "gists.json failed to parse; save skipped — fix the file first (see the status bar for details; the corrupt file was backed up). Changes are picked up automatically once it parses again.");
                Debug.LogWarning("[GistManager] Refusing to overwrite gists.json while it fails to parse: " + _loadError);
                UpdateStatusBar();
                return;
            }

            try
            {
                GistManifestStore.Save(_manifest);
                _manifestWriteTimeUtc = GistManifestStore.ManifestWriteTimeUtc;
                _loadError = null;
            }
            catch (Exception ex)
            {
                _status = GistLoc.T("写入 gists.json 失败：", "Failed to write gists.json: ") + ex.Message;
                Debug.LogException(ex);
            }
        }

        private async Task RunBusy(string startStatus, Func<Task> action)
        {
            SetBusy(true);
            _status = startStatus;
            UpdateStatusBar();

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _status = GistLoc.T("错误：", "Error: ") + ex.Message;
                Debug.LogException(ex);
            }
            finally
            {
                SetBusy(false);
                ManifestChanged();
            }
        }

        // ------------------------------------------------------------------ 详情页行为

        private void ApplyPlacement(GistEntry entry, GistFileEntry file, int index)
        {
            file.placement = index == 1
                ? GistCatalog.EditorPlacement
                : index == 2 ? GistCatalog.RuntimePlacement : string.Empty;

            var expected = GistCatalog.BuildTargetPath(_manifest, entry, file.filename, file.placement);
            if (!string.Equals(expected, file.targetPath, StringComparison.Ordinal))
            {
                GistSyncService.MoveFile(file.targetPath, expected);
                file.targetPath = expected;
                EditorApplication.delayCall += RefreshAssets;
            }

            SaveManifest();
            ScheduleManifestChanged();
        }

        /// <summary>直接编辑路径：先做安全校验，再按路径里的 Editor/Runtime 段反推归属，必要时搬文件。</summary>
        private void ApplyTargetPath(GistEntry entry, GistFileEntry file, string input)
        {
            var normalized = GistManifestStore.NormalizeRelative(input);
            if (!GistManifestStore.IsSafeProjectRelative(normalized))
            {
                _status = GistLoc.T("非法目标路径（必须是工程相对路径且不含 ..）：", "Illegal target path (must be project-relative without '..'): ") + input;
                UpdateStatusBar();
                return;
            }

            var placement = normalized.IndexOf("/" + GistCatalog.EditorPlacement + "/", StringComparison.OrdinalIgnoreCase) >= 0
                ? GistCatalog.EditorPlacement
                : normalized.IndexOf("/" + GistCatalog.RuntimePlacement + "/", StringComparison.OrdinalIgnoreCase) >= 0
                    ? GistCatalog.RuntimePlacement
                    : string.Empty;

            file.placement = placement;
            if (!string.Equals(normalized, file.targetPath, StringComparison.Ordinal))
            {
                GistSyncService.MoveFile(file.targetPath, normalized);
                file.targetPath = normalized;
                EditorApplication.delayCall += RefreshAssets;
            }

            SaveManifest();
            ScheduleManifestChanged();
        }

        private static void RefreshAssets()
        {
            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>Gist 主名称 = 首个文件的友好名（与 packageDir 的 slug 生成规则一致），没有文件名时退化为描述，再退化为 id。</summary>
        private static string GistEntryLabel(GistEntry entry)
        {
            foreach (var file in entry.files)
            {
                if (!string.IsNullOrEmpty(file.filename))
                    return GistNiceName.ForFilename(file.filename);
            }

            if (!string.IsNullOrEmpty(entry.description))
                return entry.description;

            return entry.id;
        }

        private static int PlacementIndexOf(string placement)
        {
            if (placement == GistCatalog.EditorPlacement)
                return 1;
            return placement == GistCatalog.RuntimePlacement ? 2 : 0;
        }

        /// <summary>清单变化后刷新左栏列表 + 详情 + 状态栏。</summary>
        private void ManifestChanged()
        {
            RebuildList();
            UpdateDetails();
            UpdateStatusBar();
        }

        private static bool GetFoldout(Dictionary<string, bool> map, string key, bool fallback)
        {
            bool value;
            return map.TryGetValue(key, out value) ? value : fallback;
        }

        private void SetFoldout(Dictionary<string, bool> map, string key, bool value)
        {
            map[key] = value;
            SaveFoldouts();
        }

        /// <summary>折叠状态记入 ProjectSettings/GistManager.asset（与工程一起提交；域重载/重开编辑器后都保持）。</summary>
        private void SaveFoldouts()
        {
            var settings = GistManagerSettings.instance;
            settings.collapsedAuthors.Clear();
            foreach (var pair in _authorFoldouts)
            {
                if (!pair.Value)
                    settings.collapsedAuthors.Add(pair.Key);
            }

            settings.Save();
        }

        private void LoadFoldouts()
        {
            var settings = GistManagerSettings.instance;
            _authorFoldouts.Clear();
            foreach (var author in settings.collapsedAuthors)
                _authorFoldouts[author] = false;
        }
    }
}
