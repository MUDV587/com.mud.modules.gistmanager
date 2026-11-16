# Changelog

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [0.3.0] - 2026-11-16

整体品质与可靠性里程碑再往前走一步：启用/禁用升级为整包语义、改动前原子保护全链路落地、Target Root 可搬运、工具备份统一化、切换/禁用后 Unity 不再需要手动刷新。

### 新增

- **Target Root 切换改为「切换并移动」**：Project Settings 下拉切换 `Packages/gist-scripts ↔ Assets/GistScripts` 改为三按钮（**切换并移动** / **仅切换（不移动）** / **取消**）。搬运由新 `Editor/GistRelocateService.cs` 执行：
  - 清单（`gists.json`）、嵌入式包标记（`package.json`）和每个 gist 的目录树（含所有 `.meta`、asmdef）一并搬到新根；旧根清空后整体删除（连同其 `.meta`）
  - **整体预检**：任一目标侧 gist 目录存在且非空、或目标已有同名 `gists.json`/`package.json`/各自 `.meta` → 直接报错中止，零文件被改动（不覆盖任何现有文件）
  - 搬运中途任何 IO 失败 → 逆序回滚已搬动项，清单保持不变
  - 新根位于 `Packages/` 且没有 `package.json` 时，自动生成嵌入式包标记（`com.gistscripts.gists`）
  - 只重写这次真正搬动的文件路径；磁盘上不存在的文件（被禁用、从未落盘）按新根记录，Sync 时自动补回；未随搬运的文件保持原路径并在报告里列出
- **启用/禁用升级为包级语义**：文件行里的“单文件启用开关”删除——勾选 `Enable whole package` 才决定整个 gist 走磁盘状态。禁用 → 整棵 `{packageDir}`（连 asmdef、`.meta`、子目录）移到 `{targetRoot}/Backup~/Disabled/<包目录名>`；启用/Sync → 整体移回（含本地修改，GUID 保留）；目标已存在非空目录时提示并不覆盖。禁用 sweep（Sync 时自动清理遗留分散禁用文件）保留，但同时仍用于纯包级路径；旧 `Enable package` 切换走 `BackupGistPackage(At)`/`TryRestoreGistPackage(At)`，`Backup~/Disabled/<相对 targetRoot 的路径>` 旧的单文件备份与同步仍兼容。清单 `files[].meta` 保留作 legacy 兜底。
- **禁用整包不再残留空目录**：禁用时把包目录整体连 `.meta` 搬进 `Backup~/Disabled/`（包目录的 `.meta` 是根的兄弟文件，旧版不搬它、Unity 建空壳）；启用时一并移回
- **启用/禁用改为自动刷新**：勾选/取消「启用整个包」后工具自动 `AssetDatabase.Refresh()`，新文件/asmdef 立即可被 Unity 识别编译（不再需要手动 Ctrl+R）；禁用时同理——`if (!enabled)` 刷新改为统一刷新。`Sync` 本来就收尾 `Refresh()`，顺序无变化
- **切换 Target Root 后 UPM 立即可见新包**：搬运成功后先 AssetDatabase.Refresh 再 `PackageManager.Client.Resolve()`（公开 API，强制包管理器以磁盘当前状态重解析，无网络），切换到 Packages 根后嵌入式包立即重新注册，不再需手动 Ctrl+R；切换到 Assets 根则同一时刻解注册（原嵌入式包从 UPM 列表消失）
- **搬运旧根清理更彻底**：`DeleteEmptiedRoot` 现在递归清理「兄弟已不存在但自己还在」的 stray `.meta`（Unity 否则会据它重建空目录）；从而旧根在搬运后通常整体被删（用户文件依然保留）
- **工具备份集中在 `{targetRoot}/Backup~/`**（Unity 完全忽略的目录，不导入、不生成 `.meta`）：
  - **禁用文件保险**：禁用时连文件及 `.meta` 一并移入 `Backup~/Disabled/<相对 targetRoot 的路径>`（不再仅记录 meta 文本到清单）；启用时在下载前先从 `Backup~/Disabled/` 移回，字节一致，**包含禁用前的本地修改**；Sync 恢复分支同样先查 Backup~，找不到才走网络/sha256 判定
  - **清单备份**：`gists.json` 原子写的 `.bak`、解析失败时的 `.bad-*` 备份写入 `Backup~/`；加载时自动把清单旁边的旧 `.bak`/`.bad-*`/`.tmp-*`（及它们的 `.meta`）迁移进 `Backup~/`
  - **覆盖保险**：覆盖下载前把被覆盖的本地文件复制到 `Backup~/Overwritten/<时间戳>/<相对路径>`
  - **Relocate 联动**：`Backup~/` 视为根级整体单元随 Target Root 搬运（目标 `Backup~` 非空 → 预检整体中止）
  - 清单 `files[].meta` 字段保留（兼容旧数据），新禁用不再写入
- **清单位置不再是硬编码的 `Packages/gist-scripts/gists.json`**：`{targetRoot}/gists.json` 随根移动；位置记录于 `ProjectSettings/GistManager.asset` 的 `manifestRelativePath` 字段（老项目未记录时回落旧的固定路径，零迁移感知）。CI 示例更新为先从 `ProjectSettings/GistManager.asset` 解析清单路径（POSIX/PowerShell 两版）
- 编辑器内同步时的上游更新检查保留，搬运不产生网络调用

### 修复

- `gists.json` 写入改为**原子替换**：先写 `gists.json.tmp-*` 临时文件，`File.Replace` 替换目标，旧版本轮换为 `gists.json.bak`（单副本）；非 NTFS / 目标被占用时退化为「复制备份 + 覆盖」，不再直接截断清单
- 清单**解析失败不再静默覆盖**：`LoadOrCreate` 在读失败 / JSON 非法时，先把原文件复制为 `gists.json.bad-yyyyMMdd-HHmmss-fff` 备份再报错（错误信息含备份路径）；主窗口在 `_loadError` 非空时 `SaveManifest` 直接拒绝写盘并提示修复，避免坏清单被后续保存操作静默销毁
- 网络请求**增加 30 秒超时**（`UnityWebRequest.timeout`）：网络黑洞（代理挂死 / 防火墙吞包）时请求快速失败进现有错误路径，不再把 Sync All 永久挂在 busy 状态；超时（`responseCode == 0`）时错误信息不再带 `HTTP 0` 前缀

### 其它

- 新增 `Tests/Editor/GistManifestSaveTests.cs`：显式路径重载（`SaveAtPath` / `LoadOrCreateAtPath`）在临时目录中覆盖「首存无 bak / 覆盖轮换 bak（Backup~ 位置）/ 损坏 JSON 备份（Backup~ 位置）/ 旧 stray 备份迁移 / 合法往返」5 个用例；Editor 程序集用 `InternalsVisibleTo` 向测试程序集开放内部重载
- 新增 `Tests/Editor/GistRelocateTests.cs`（16 个用例）：清单定位默认回落/记录读取/非法回退、搬运双向迁移（含 .meta GUID 保留、嵌入式包标记重建、目标已有空目录合并）、预检整体中止零改动、空源根只改清单、同根 no-op、注入 IO 失败回滚后源树原样恢复、Backup~ 随根迁移及目标 Backup~ 非空中止、搬运后旧根空目录与 stray .meta 彻底清理（旧根整体删除）/有用户文件时保留但其余清理
- 新增 `Tests/Editor/GistBackupTests.cs`（7 个用例）：禁用纯文件移动（含 Fallback 与散置路径回退到记录 meta+删除）、恢复字节一致（空备份 → false；目标已存在 → 不覆盖）、Sync 恢复分支优先于 rawUrl 缺失、覆盖保险复制到 `Backup~/Overwritten/<时间戳>/`

## [0.2.0] - 2026-11-11

Gist 管理器整体 UI 全面 UI Toolkit 化；双语；Sync 增加本地改动保护与上游更新提示；支持多选批量操作。

### 新增

- 迁移到包内 `Components/` 框架（`Editor/Components/`，`MUD.Modules.GistManager.Components` 命名空间）：TopToolbar（Add/Sync All/搜索/More）、GroupHeader、ItemRow、GistList、GistDetails、GistStatusBar、EmptyArea、Splitter、ToolbarSearch/Dropdown
- `sync-all` 工具栏按钮 + `Ctrl/Cmd+Shift+G` 快捷键
- Add 嵌入面板 `Add Gist Script from git URL...`（点工具栏 `+` 弹窗不关，直接贴在工具栏下方；粗体大标题/说明/占位输入框/Cancel/Add；Enter=Add/Escape=Cancel）；只写清单，不下载
- Gist Manager 窗口：菜单统一为 **Tools → MUD → Gist Manager**（与 MenuRedirect 同组），移除旧的 **Tools → Gist Script Manager**；设置走 **Edit → Project Settings → MUD → Gist Manager**（Gist 主窗口的 **⋮ More → Gist管理器设置** 也会直接打开该面板），含界面语言/Target Root/清单/GitHub Token/上游检查
- Sync 本地改动保护：每个文件记录 `sha256`，同步时磁盘内容与记录不一致即**跳过并提示**；详情面板对此文件出现琥珀色警示 + 「覆盖下载」按钮（强制覆盖前确认）；旧清单首次加载自动回填指纹基线
- 同步时可选顺带检查上游是否有更新（GitHub API，每条目 1 次调用；只报告不改清单，可关闭）
- 多选：Ctrl/Cmd+Click 增删、Shift+Click 扩选；多选时右栏显示 "N gists selected" + 批量按钮（Sync selected / Enable all / Disable all / Remove selected from manifest）；右键菜单按选中集合作用
- 详情面板空态占位（"No gists yet" + Add 按钮）与列表空态分四种语义（有结果/清单空/搜索 0 命中）
- Open Gist URL（打开Gist链接）与 Open Folder（打开文件夹）按钮
- 中英双语（默认跟随系统语言）
- EditMode 测试套件（`Tests/Editor/`，39 个用例，程序集 `MUD.Modules.GistManager.Tests.Editor`）

### 变更

- 内联样式全面收拢进 USS：新增 `.gm-hidden` 工具类与 code 创建元素的全部常量样式（Add 面板/文件行/多选区/空态/Files 区块）；各控制器 `style.display` 显隐改为 `gm-hidden` class 切换；`UXML` 中 16 处 `style="..."` 全部移入对应组件 USS；`isProSkin` 三元改为深色常量；C# 里仅保留真正动态的像素值（splitter 宽度）
- 工具栏下拉（Add / More）弹出层由自绘 VisualElement Popup 改为 **Unity 内置 `GenericMenu`**（编辑器右键菜单同款；外部点击关闭、禁用态、分隔线全由系统菜单负责）：自绘方案运行时创建的元素匹配不到组件完整的 `.gm-dropdown-panel*` 规则（`Popup` 样式/悬停/行高全部失效，文字在窗口顶端被挤压重叠），干脆换回原生菜单
- `GistNiceName` 断词算法对齐 Unity `ObjectNames.NicifyVariableName`：新增缩写块末尾断词（`JSONUtility`→`JSON Utility`）、digit→Upper 后跟 lower 断词（`UTF8Utility`→`UTF8 Utility`）、lower→digit 断词（Unity 行为，`Utils2x`→`Utils 2x`、`some_utils-v2`→`Some Utils V 2`）；扩展名剥离/分隔符折叠/CJK 直通保留；`upper→digit`（`V2`、`AB2CD`）不断词
- 组件命名及 USS 命名空间统一 `gm-*` 前缀；element `name` 同步更新（如 `SplitterHandle`/`gm-toolbar-search`）；GUID/样式值未变
- 移除无人执行的死规则：`.gm-load-bar__dropdown`、`.gm-group-header.{reserved,empty,hidden}`、`.gm-toolbar-dropdown.toggled`、`.gm-item-row.invisible`、`.gm-gist-list__list-view`、`.gm-gist-status-bar__refresh*`、`.gm-gist-details .quickStartButton*`、`.gm-gist-details .unity-button.link`、`#detailCustomContainer/#detailExtensionContainer`；ItemRow `.status.{installedasdependency,indevelopment,custom,updateavailable,importavailable,downloadavailable,inprogress,error,warning}`；`.focus.selectedInInspector`。`ItemRow.uxml` 模板随之去掉 `downloadavailable` class；孤儿图标共 3 个（`Update-Available.png`/`Warning.png`/`Dependency.png`）已删除，剩余 9 个 PNG 全部有引用。
- `GistDetails.uxml` 精简为 Gist 实际使用的元素：仅保留 `UpdateButton`/`RemoveButton` 2 个操作按钮、Description 单 Tab、title/version/source/name；移除 11 个早期遗留的操作按钮、7 个占位 Tab 按钮和 7 个空 Tab Body、`tagRelease`/`tagPreRelease`/`dependencyContainer`/`detailAuthorLink`/`toolbarProgressContainer`/`leftItems`/`extensionItems`；`GistDetailsController` 相应去掉 `k_TabButtonNames`/`k_TabBodyClassNames`/`SelectTab`/`TabChanged`/`InstallRequested`/`InstallButton`/`SetActionVisible`
- `GistListController` 去掉 LoadMore 占位（`LoadedLabel`/`LoadMoreButton`/`LoadBar` 等）；`GistStatusBarController` 去掉 refresh 区（`RefreshButton`/`RefreshRequested`）；`GroupHeaderController` 去掉未使用的公有 API（`IsItemRow`/`IsSelectedRowVisible`/`ClearSelection`/`GetItemRow`/`GetSelectedRow`、`SelectionChanged`）；`ToolbarSearchController` 去掉 `Value` 属性和 `ClearButton` 公有爆露；`SplitterController` 收窄为私有 `_dragging`/`FirstPane`/`Handle`；`ToolbarDropdownController.PanelAnchor` 删除；`TopToolbarController.AttachPopupsTo` 删除
- 窗口类名与文件改名：`GistScriptManagerWindow` → `GistManagerWindow`（class + `.cs` + `.meta`），asmdef 不需改（按文件夹 glob）；对外仅 `GistManagerSettingsProvider` 和内部 `ShowWindow` 引用同步更新；`GistManagerWindow.uss` 已于此前合并进 `GistManagerWindow.uss`，主窗口现在只有 `.cs/.uxml/.uss` 三个同名文件；`MUD.EditorWindowTemplates` 命名空间改为 `MUD.Modules.GistManager.Components`（与包其它代码一致）；`UpdateButton/RemoveButton` → `gistSyncButton/gistRemoveButton`
- 顶层外壳文件改名：`BasicWindow01.uxml`→`GistManagerWindow.uxml`、`BasicWindow.uss`→`GistManagerWindow.uss`（自定义 gist 专用槽位骨架而非通用"基础窗口"）；根类改名为 `.gist-manager-window-root`；`BindFocusClass` 改为通过 `rootVisualElement` 子元素查询根控件
- 图标统一归集到 **`Editor/Icons/`**（平铺）：下拉箭头 `d_dropdown.png` 已归集到 `Editor/Icons/`；全部 `project://database` 引用同步更新（GUID 不变）
- 项目级设置（界面语言/折叠状态/上游检查/最近 Sync）从 EditorPrefs 迁移到 **`ProjectSettings/GistManager.asset`**（`ScriptableSingleton`，可提交进版本库；旧值自动迁移）；GitHub Token 仍保留在 EditorPrefs（不入版本库）
- 清单固定位置从工程根目录 `gists.json` 迁移到 **`Packages/gist-scripts/gists.json`**（与已同步脚本同目录）；旧位置存在时自动迁移并输出一条日志
- UI 从「UI Toolkit 左栏 + IMGUI 工具栏/右栏/状态栏」迁移为全 UI Toolkit；Scope/Ordering/Filters 下拉与 FilterBar 子页因无 gist 语义早期已移除
- 窗口标题：英文 GistManager，中文 Gist管理器；More 菜单：Reload Manifest / Gist管理器设置 / About；Add 下拉：Add Gist Script from git URL...
- Sync 收尾统计：已写 N 个文件 / M 个跳过（本地有改动）/ K 个 gist 没有文件可同步
- `Sync All` 按钮忙碌时禁用，状态栏显示加载中 spinner

### 移除

- 旧窗口 `GistScriptManagerWindow.uss/.uxml` 与全部 IMGUIContainer 段落
- 阶段一“演示窗口 + 占位组件”等历史代码：演示窗口（含 `Window/Gist Manager Replica` 菜单）、`Preview.uxml`、`Feature*ItemRow` 组件与 Feature 图标、`FilterBar` 组件
- `TopToolbar` 中残留的 Scope/Ordering/Filters 下拉与 Clear Filters 按钮早期已移除（无 gist 语义）；对应控制器一并删除，`TopToolbar` 收敛为 `Add / Sync All / ⋮ / Search`
- Clear collapsed groups、Install/Git/Unlock/Download 等按钮

### 固定/修复

- GistDetails `.detailsHeader` 补右 padding，操作按钮不再贴边
- Splitter 手柄从「6px 热点 + 透明底（透出窗口灰底成缝）」改为 3px 实心黑条（`rgb(35,35,35)`），黑条紧贴右栏
- 右栏内容（标题栏/版本/依赖/描述/文件行）全部左边缘贴到黑线，移除残留的左侧内缩
- ToolbarSearch `:focus-within` 伪类替换为 class 切换（Unity 2022.3 USS 不支持该伪类）

## [0.1.0] - 2026-10-29

首个可用版本：GistScriptManager 基础窗口（UI Toolkit 左栏 + IMGUI 工具栏/右栏），`gists.json` 作为唯一事实源，Add/启用/Sync 下载到工程，Editor/Runtime 归位 + 自动生成 asmdef，`.meta` 禁用时保留并在重新启用时恢复。
