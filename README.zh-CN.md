# Gist Script Manager

Unity 编辑器工具：把散落在 GitHub Gist 里的脚本片段收进一个项目，用一份清单 `gists.json`（随 `targetRoot` 移动）统一管理，编辑器 GUI 与 CI 共享同一份事实源。

核心决策：

- **`gists.json` 是唯一事实源**：GUI 添加、手动编辑、CI 同步都只读写这一份清单（位置 = `{targetRoot}/gists.json`，与已同步脚本同目录；切换 Target Root 时清单本身一并搬运）；文件下载是独立的“同步”动作。
- **工具备份集中在 `{targetRoot}/Backup~/`**：Unity 完全忽略的目录——禁用整包备份（整棵 gist 目录连 .meta 一并移入 `Backup~/Disabled/`，启用时原样移回，本地修改不丢）、清单原子写的 `.bak`/解析失败的 `.bad-*` 备份、覆盖下载前的本地保险副本（`Backup~/Overwritten/<时间戳>/`）。Enable/disable/Sync/搬运全链路都走 Backup~，随目标根目录迁移；GitHub Actions 等 CI 可直接 `**/Backup~/` 忽略该目录。
- **添加 ≠ 同步**：粘贴 Gist 链接只解析元数据并写入清单，不下载文件；下载由显式的 Sync 按钮触发。
- **Sync 保护本地改动**：每个文件记录上次写出的 `sha256`；同步时若磁盘文件与记录不一致（= 你手改过）则该文件**跳过、不覆盖**，状态栏会提示；详情里有「覆盖下载」按钮（先确认）可以强制用上游内容覆盖。
- **`enabled` 控制同步与磁盘状态**：启用时下载/覆盖；**取消勾选会把整个包（gist 目录）连 .meta/asmdef 移入 `Backup~/Disabled/`**（Unity 忽略该目录，包级禁用没有“单文件残留”），下次启用时（或 Sync 时）从备份原样移回。旧清单里记录的 `.meta` 文本仍然有效，作为兜底恢复。
- **文件可存放在 `Packages/` 或 `Assets/`**：通过清单里的 `targetRoot` 切换，默认 `Packages/gist-scripts`；位于 `Packages/` 时，会在 `targetRoot` 根上生成**唯一的** `package.json`，整个目录成为一个嵌入式包（Unity 只把 `Packages/` 的直接子目录当作包，不能一个 Gist 一个包）。
- **Editor / Runtime 分开放置并自动生成 asmdef**：每个文件按归属落在 `{targetRoot}/{packageDir}/Editor|Runtime/` 下，每个 Gist 目录按实际用到的归属生成对应 asmdef（包内脚本不挂 asmdef 不会被编译）。归属可在文件行手动指定（Auto/Editor/Runtime），Auto 时按内容探测：用到 `UnityEditor` 就归 Editor。

## 安装

通过 Git URL 安装（包清单在仓库根目录）：

```
在编辑器内打开包管理器（`Window` 菜单）→ `+` → `Install package from git URL...`
https://github.com/MUDV587/com.mud.modules.gistmanager.git
```

依赖：`com.unity.nuget.newtonsoft-json`（Unity 官方 registry，用于解析 GitHub API 响应里以文件名为键的 `files` 字典）。Unity 版本：2022.3（UI Toolkit）。

## 快速开始

1. 打开 `Tools → MUD → Gist Manager`。
2. 点击 **+ → 从git链接添加Gist脚本（English: Add Gist Script from git URL...）**，在工具栏下方弹出的输入面板里粘贴 Gist 链接（`https://gist.github.com/{author}/{id}`，也接受匿名链接、省略协议的地址、裸的 `author/id`），Enter 或 **Add**。
3. 工具调用 GitHub API 解析作者与文件列表，写入清单（位置 = `{targetRoot}/gists.json`，默认 `Packages/gist-scripts/gists.json`），窗口里按 `作者 → Gist → 文件` 层级呈现。
4. 勾选需要的文件；点击详情或行右键菜单里的 **Sync**（同步这一个 Gist），或工具栏 **Sync All** 下载到 `targetRoot` 全部 `enabled` 文件；取消勾选会把整个 gist 目录连 .meta/asmdef 移入 `Backup~/Disabled/`，下次启用时原样恢复（含本地修改）。

也可以完全绕过 GUI：直接编辑清单（默认 `Packages/gist-scripts/gists.json`，或 `ProjectSettings/GistManager.asset` 里 `manifestRelativePath` 记录的位置），窗口获得焦点时会自动读回（或 ⋮ More → **Reload Manifest**）。

## 窗口结构（左：列表 / 右：详情）

窗口根为 **UI Toolkit**，用包内的组件（`Editor/Components/`）拼装而成的 UI Toolkit 窗口骨架，不再混用 IMGUI：
左栏 = `GistList`（`GroupHeader` 作者分组 + `ItemRow` 24px 行）+ `GistStatusBar`；
右栏 = `GistDetails`（标题操作区 + Description 单 Tab，Tab 内容为全 UI Toolkit 的文件编辑器）；
工具栏 = `TopToolbar`（Add 下拉 / Sync All / 搜索 / More）。

```
┌ [+ Add] [Sync All] [⋮]  [🔍              ]              工具栏（UI Toolkit）
├──────────────────────────┬──────────────────────────────────────┤
│ [🔍              ]  │  Extract a .unitypackage…  [Sync][Remove]
│ yasirkula                 │  dfc43134  From GitHub Gist by yasirkula
│ ▸ Extract a .unitypack…   │  yasirkula/dfc43134…  [打开文件夹][打开Gist链接]
│ ▸ Find Textures…          │  Description
│ tomkail                    │  Extract a .unitypackage…          │
│ ▸ Displays the fields…     │  Target Folder  Packages/gist-scripts/…│
│  左栏（UI Toolkit）：         │  Files  ☑ 启用整个包（2 个文件）        │
│  24px 行/悬停/选中高亮        │  ☑ UnitypackageExtractor [Editor] X │
│  单击选中、右键出菜单         │  Path Packages/…/Editor/…cs        │
├──────────────────────────┴──────────────────────────────────────┤
│ 状态栏：Never synced / Last sync / 错误信息 / busy spinner          │
└─────────────────────────────────────────────────────────────┘
```

左栏：

| 交互 | 说明 |
| --- | --- |
| 搜索框（工具栏右侧） | `ToolbarSearch`：按作者、Gist ID、描述、文件名或目标路径过滤（忽略大小写）；**1/3 秒防抖**，`Ctrl/Cmd+F` 聚焦，空输入时无占位提示；搜索时强制展开分组；空态：（1）清单空 → `No gists yet.` + **`Add gist from git URL...` 主按钮**（点击直开添加对话框）；（2）清单非空但搜索 0 命中 → `No results for "…"` + `Clear Search` 按钮；（3）有结果 → 正常列表。 |
| 作者分组标题 | `GroupHeader`（caret + 作者名），点击折叠/展开，折叠状态记入 `ProjectSettings/GistManager.asset`（由 `GistManagerSettings` 管理，可提交进版本库；域重载/关闭窗口/重开编辑器均保留）。 |
| Gist 行 | `ItemRow`，**24px 行高**：单行标题（首个文件的友好名；省略号裁剪；没有文件名时退化到描述、再退化到 ID）、灰显短 id（`id` 前 8 位，占标题行右下角次要信息位）；全部启用时显示 Installed 图标，否则隐藏图标且行文字灰显（`is-disabled`）。行 tooltip：`描述\n真实链接`。 |
| 悬停/选中 | USS `:hover` 悬停高亮，单击选中（蓝底白字，`rowContent.selected`），右栏同步刷新详情；重建（搜索/增删）后保留选中并自动滚动到可见。 |
| 键盘导航 / 快捷键 | `↑`/`↓` 逐行、`PageUp`/`PageDown` 翻 5 行、`Home`/`End` 到首/尾行，只在**可见行**间移动（折叠组内不参与），越界处夹紧；选中行同时获得焦点（focus-follows-selection），焦点在搜索框时方向键仍用于移动文本光标；`Ctrl/Cmd+Shift+G` 同步全部 gist（窗口内作用域，不抢全局命令）。 |
| 右键菜单 | UI Toolkit `ContextualMenu`：Open in Browser / Sync / Enable·Disable Package / Remove from Manifest（行上右键也会先选中该条目）。 |
| 左右分栏 | `Splitter`（左栏 300px 起，可拖拽至分割线贴近右沿——右栏保留 ≥300px；3px 实心分隔线，hover 时为 `split-resize-left-right` 光标），面板底色按深色皮肤编写。 |

右栏（选中条目的详细设置）：

| 设置 | 说明 |
| --- | --- |
| Open Gist URL（打开Gist链接）| 用浏览器打开该 Gist 的真实链接（`gist.github.com/author/id`，匿名无作者段）；复用 quickStart 按钮并去掉其 link 化样式，保留默认按钮底色。 |
| Open Folder（打开文件夹）| 在文件管理器中打开本地 gist 包目录（`targetRoot/{packageDir}`）；目录还不存在（未 Sync 过）时退到 targetRoot，再不存在则退到工程根目录。 |
| Sync / Remove | 只同步或从清单移除这一个 Gist（移除不碰磁盘文件）。 |
| 启用整个包 | 一键勾选/取消全部文件；取消时把整棵目录连 .meta/asmdef 移入 `Backup~/Disabled/<包目录名>`，启用时整体恢复。 |
| 归属下拉 `Auto/Editor/Runtime` | `DropdownField`：决定文件落在 `Editor/` 还是 `Runtime/`、挂哪个 asmdef；切换时文件连 `.meta` 一起搬家。 |
| Path | `TextField(isDelayed)`：目标路径可直接编辑（必须是工程相对路径且无 `..`，focus-out 时提交）；路径里的 `/Editor/`、`/Runtime/` 段会反过来决定归属。 |
| X | 从清单移除该文件（不碰磁盘文件）。 |
| 本地有改动警示行 | 当磁盘文件与记录的 `sha256` 不一致时出现：琥珀色提示“本地有改动，同步时会跳过”；右侧「覆盖下载」按钮（先确认）用上游内容强制覆盖（会丢失本地改动）。 |

两个操作按钮改用本包专属按钮：**Sync**（`gistSyncButton`）/ **Remove**（`gistRemoveButton`）；其余早期遗留的 Install/Unlock/GitUpdate/Download 等 11 个按钮早期已从 `GistDetails.uxml` 删除。标题行 **Open Gist URL（打开Gist链接）** 直接使用 `quickStart` 按钮（文案改为“打开Gist链接”），打开该 Gist 的真实链接；其左侧并排 **Open Folder（打开文件夹）** 打开本地 gist 包目录。

以下设置不在工具栏/窗口弹窗中：走 **Edit → Project Settings → MUD → Gist Manager**（或者工具栏 **⋮ More → Gist管理器设置**，直接打开该面板）：

| 设置 | 说明 |
| --- | --- |
| Target Root | `Packages/gist-scripts` ↔ `Assets/GistScripts`（或清单里自定义的值）。切换时弹出三按钮：**切换并移动**（清单 `gists.json`、嵌入式包标记 `package.json`、全部 gist 脚本及所有 `.meta` 一并搬到新根；旧根清空后整体删除；目标位置已有同名文件 / 非空 gist 目录时整体中止，零改动；搬运中途失败会回滚）/ **仅切换（不移动）**（只改清单 `targetRoot`，旧文件原地不动，见下方 ⚠️）/ **取消**（零改动）。搬运后清单位置写入 `ProjectSettings/GistManager.asset` 的 `manifestRelativePath`。切换到 Packages 根后，Unity 按包状态机自动重解析，嵌入式包在 UPM 里立即出现；切换到 Assets 根后嵌入式包会从 UPM 消失（脚本编译继续正常）。 |
| Manifest | 清单路径（随 `targetRoot`，如 `Packages/gist-scripts/gists.json` 或 `Assets/GistScripts/gists.json`，与已同步脚本同目录）；位置记录于 `ProjectSettings/GistManager.asset` 的 `manifestRelativePath`（可提交进版本库），未记录时回落 `Packages/gist-scripts/gists.json`（老项目零感知）；切换 Target Root 时清单一并搬运；工程根目录的 `gists.json` 旧格式首次加载自动迁移。 |
| GitHub Token | 存在 `EditorPrefs`（本机，**绝不过版本库**），避免随项目提交。未认证 60 次/小时，认证后 5000 次/小时；其余非敏感项目配置（语言/折叠/上游检查/最近 Sync）存于 `ProjectSettings/GistManager.asset`，可提交进版本库。 |
| 同步时检查上游更新 | 默认开启；每条目同步时多调 1 次 GitHub API，对比 `rawUrl`（含 commit sha）与上游文件清单，有差异时只在状态栏提示“上游有更新”——**不会改动清单**（新文件/新 sha 不会自动拉入），重新 Add 可刷新。关闭可省 API 调用。检查失败不影响同步本身。 |
| 界面语言 | 跟随系统（自动，默认：系统语言以 `Chinese` 开头视为中文，否则 English）/ 中文 / English。切换立即重建主窗口与弹窗全部文案；存 `ProjectSettings/GistManager.asset` 的 `language` 字段（auto/zh/en）。 |

More 菜单项：**Reload Manifest**（重读 gists.json，检测手工编辑；窗口获得焦点时也会自动读回）、**Gist管理器设置**（English: GistManager Settings；打开 Project Settings 面板）、**About**（关于对话框）。弹出菜单用 Unity 内置 `GenericMenu`（编辑器右键菜单同款）。

⚠️ **「仅切换（不移动）」的风险**：只改清单里的 `targetRoot`，磁盘一个文件不动。之后 Sync 会按新根**重新下载**全部启用文件，旧根的旧副本还在——新旧两根里同名 gist 会产生**同名 asmdef**，导致 Unity 工程无法编译。除非你明确知道旧文件怎么处理，否则选此项；选错了请手动删掉其中一侧的脚本目录。正常切换请选「**切换并移动**」（有目标冲突会整体中止，中途失败会回滚，不会弄丢任何文件）。

## gists.json

清单位置 = `{targetRoot}/gists.json`，与已同步脚本同目录（三个 gist 目录与 `package.json(com.gistscripts.gists)` 同级）；**切换 Target Root 时清单本身一并搬运**，位置记录于 `ProjectSettings/GistManager.asset` 的 `manifestRelativePath`。旧版本清单位于工程根目录时，首次加载会自动迁移过去并输出一条日志；字段顺序与工具写出的一致，可直接复制 `gists.json.example` 改名使用：

```json
{
  "targetRoot": "Packages/gist-scripts",
  "gists": [
    {
      "id": "dfc43134fbfefb820d0adbc5d7c25fb3",
      "author": "yasirkula",
      "description": "Unitypackage Extractor",
      "packageDir": "yasirkula.unitypackage-extractor",
      "files": [
        {
          "filename": "UnitypackageExtractor.cs",
          "targetPath": "Packages/gist-scripts/yasirkula.unitypackage-extractor/Editor/UnitypackageExtractor.cs",
          "enabled": true,
          "rawUrl": "https://gist.githubusercontent.com/yasirkula/dfc43134/raw/UnitypackageExtractor.cs",
          "meta": "",
          "placement": "Editor"
        }
      ]
    }
  ]
}
```

| 字段 | 含义 |
| --- | --- |
| `targetRoot` | 同步时文件写入的根目录，必须是工程相对路径（`Packages/...` 或 `Assets/...`）。 |
| `gists[].id` | Gist id，去重键。 |
| `gists[].author` | 从 URL/API 解析后持久化，构建层级视图时无需联网。 |
| `gists[].packageDir` | 内部分组目录名 `author.slug`，slug 取首个文件名的友好形式（`UnitypackageExtractor.cs` → `unitypackage-extractor`，没有文件名才回退描述），添加时生成并固定，之后不随上游描述变化。 |
| `files[].targetPath` | 目标路径（正斜杠）。缺失时按 `targetRoot/packageDir/[/Editor\|/Runtime]/filename` 自动补全；只允许非绝对、无 `..` 的工程相对路径。 |
| `files[].placement` | `Editor` / `Runtime` / 空（= Auto，同步时按内容探测）。决定文件落在哪个子目录、挂哪个 asmdef。 |
| `files[].sha256` | 最近一次由本工具写出的文件内容的 SHA-256（hex 小写）。Sync 时磁盘文件存在且指纹一致 ⇒ 视为可覆盖；指纹不一致 ⇒ 本地改动过，**跳过且不覆盖**；记录为空（旧清单/手写条目）视为可覆盖。**旧清单首次加载时会把当前磁盘内容自动记为基线**（`Normalize` 自动回填，无需手工处理）。 |
| `files[].enabled` | 只控制同步时是否下载/覆盖。**手工新增条目时必须显式写 `"enabled": true`**（缺省会被读成 `false`）。 |
| `files[].rawUrl` | 缓存的下载地址；缺失时同步会自动拉一次上游元数据补齐。 |
| `files[].meta` | 已弃用（兼容旧清单）：文件被禁用移除时先抓下来的 `.meta` 内容（含 guid）。**新禁用是包级的：整棵目录连 .meta 迁入 `Backup~/Disabled/<包目录名>`**，启用时原样恢复；旧清单里尚未恢复的 `.meta` 仍作为同步时的兜底写回。 |

## 存储策略：Packages/gist-scripts（单个嵌入式包 + 每 Gist 一个编译单元）

同步后的磁盘结构（一个 Gist 可含多个文件，按 Editor/Runtime 分开存放）：

```
Packages/gist-scripts/
  package.json                                   ← 唯一的嵌入式包（com.gistscripts.gists）
  yasirkula.unitypackage-extractor/              ← 一个 Gist 一个目录
    Editor/
      yasirkula.unitypackage_extractor.Editor.asmdef   ← 自动生成（无额外前缀）
      UnitypackageExtractor.cs
    Runtime/
      yasirkula.unitypackage_extractor.Runtime.asmdef  ← 只在真有 runtime 脚本时生成
      SomeHelper.cs
```

要点：

- **package.json 只在 targetRoot 根上生成一份**：Unity 只把 `Packages/` 的直接子目录当包，之前“每 Gist 一个 package.json”放在更深层目录里不会被识别，已废弃；同步时会自动清理旧残留。
- **Editor/Runtime 子目录 + 自动生成的 asmdef**：包内脚本不挂 asmdef 不会参与编译，所以每个 Gist 目录按实际用到的归属生成 asmdef（程序集名 = `{packageDir 的 - 换成 _}.{Editor|Runtime}`，不加额外前缀；Editor 版 `includePlatforms: ["Editor"]`，Runtime 版不限平台）；某层没有脚本时对应 asmdef 会被删掉，旧版 `GistScripts.*` 命名的 asmdef 会在同步时自动清理。
- **默认物理目录名 `gist-scripts`**（不是 `GistScripts`），可在 Settings 里切到 `Assets/GistScripts`。

这借鉴了 NuGetForUnity 的“清单-恢复”模型：`gists.json` 与根 `package.json` 提交进版本控制，`.cs` / `.asmdef` 作为可恢复产物可选择不提交（CI 或一次 Sync 即可重建）。`targetRoot` 必须至少比 `Packages/` 深一层（如 `Packages/gist-scripts`）；`Packages` 本身不会被当作包根。

`.gitignore` 示例（按需取用）：

```gitignore
# 恢复产物：由 gists.json + Sync/CI 重建
Packages/gist-scripts/**/*.cs
Assets/GistScripts/**/*.cs
# 工具备份（禁用前的文件/清单备份/覆盖保险）——备份不入版本库
**/Backup~/
```

## 与 CI 的协同（第二步，未包含在本版本）

清单（`{targetRoot}/gists.json`）同时是编辑器 GUI 与 GitHub Actions 的输入源：CI 读取清单、遍历 `enabled` 条目，用 `curl` 拉 `raw_url` 写入 `targetPath`，再发布为 npm 包。编辑器内 Sync 与 CI 的“恢复”步骤语义一致，区别仅在执行环境。

由于清单位置随 `targetRoot` 移动，CI 不应硬编码路径，而应从 `ProjectSettings/GistManager.asset` 解析（老项目未切换过时回落默认路径）：

```bash
# POSIX shell / CI 脚本
MANIFEST=$(grep -oP '^manifestRelativePath:\s*\K.+' ProjectSettings/GistManager.asset | head -1)
MANIFEST=${MANIFEST:-Packages/gist-scripts/gists.json}
echo "using manifest: $MANIFEST"
```

```powershell
# PowerShell
$m = Select-String -Path ProjectSettings/GistManager.asset -Pattern '^manifestRelativePath:\s*(\S+)' | Select-Object -First 1
$MANIFEST = if ($m) { $m.Matches[0].Groups[1].Value } else { 'Packages/gist-scripts/gists.json' }
Write-Output "using manifest: $MANIFEST"
```

## 与 anatawa12/unity-gist-pack 的区别

它通过修改嵌入式包内容实现启用/禁用，因此需要私有源、不便与 CI 配合；本工具只做文件级的下载/覆盖，`enabled` 不触碰包内容，因此与标准 npm 发布流程完全兼容。

## 测试

EditMode 测试位于包内 `Tests/Editor/`，程序集 `MUD.Modules.GistManager.Tests.Editor`（67 个用例）：覆盖 `GistUrlParser`、`GistNiceName`、`GistSearch`、`GistCatalog`（AddOrUpdate/Retarget/Normalize/packageDir 冲突）、`GistManifestStore`（路径安全校验/往返序列化/原子写入/损坏清单备份/清单定位的默认回落与非法回退/stray 备份迁移）、`GistRelocateService`（Target Root 搬运的双向迁移、预检中止零改动、IO 失败回滚、空源根、同根 no-op、Backup~ 随根迁移及预检冲突、搬运后旧根空目录/stray .meta 彻底清理）、`GistSyncService`（sha256 本地改动判定）、`GistBackupTests`（禁用→备份纯文件移动/恢复字节一致/备份失败不丢本地改动、覆盖保险）。需要真实网络请求的 Sync 下载链路没有自动化用例（恢复分支为纯文件移动，有覆盖）。

运行：`uloop run-tests --test-mode EditMode --filter-type assembly --filter-value MUD.Modules.GistManager.Tests.Editor`，或 Test Runner 里选该程序集。

## 目录结构

```
package.json                               npm 包清单 com.mud.modules.gistmanager（可 Git URL 安装）
gists.json.example                         清单样例（与 Packages/gist-scripts/gists.json 的格式一致）
Editor/
  MUD.Modules.GistManager.Editor.asmdef    仅编辑器程序集
  GistManifest.cs                          数据模型 + gists.json 读写（原子替换 + .bak 轮换）+ 清单定位（`{targetRoot}/gists.json`，读 `ProjectSettings/GistManager.asset`）+ 路径安全校验
  GistRelocateService.cs                   Target Root 切换时的整体搬运（清单/package.json/gist 目录树及 .meta；预检整体中止；失败回滚）
  GistCatalog.cs                           清单合并 / packageDir / targetPath / targetRoot 迁移
  GistUrl.cs                               Gist 链接解析 + 真实链接拼接
  GistNiceName.cs                          文件名友好化（对齐 Unity NicifyVariableName 的断词规则 + 扩展名剥离 + 分隔符折叠；CJK 直通）
  GistApiClient.cs                         GitHub API 异步客户端 + Token 存储
  GistSyncService.cs                       同步下载/移除 + Editor/Runtime 归位 + asmdef 生成 + .meta 记录恢复 + sha256 指纹（本地改动保护）
  GistSearch.cs                            左栏搜索过滤 + 空状态文案（纯逻辑，可在无 UI 环境下单测）
  GistLoc.cs                               中英双语文案 + Language 设置存取（GistManagerSettings，默认跟随系统语言）
  GistManagerSettings.cs                   项目级设置 ScriptableSingleton（ProjectSettings/GistManager.asset，可入版本库；一次性从 EditorPrefs 迁移）
  GistManagerWindow.cs               主窗口：GistManagerWindow + 各 Controller 装配；左栏/详情/状态栏全部 UI Toolkit；含内联 Add 面板（工具栏下方，非独立窗口）
  GistManagerSettingsProvider.cs          Project Settings 面板（Project/MUD/Gist Manager，参照 MenuRedirect 做法；界面语言/Target Root/Manifest/GitHub Token/上游检查）
  GistManagerWindow.uxml                 主窗口外壳（Editor/，非复用组件）：TopToolbar + Splitter + 左右分栏
  GistManagerWindow.uss                  外壳槽位 + 1px 底线 + 模板容器填充规则 + 28px 状态栏槽位
  Components/                              原子组件（UXML + USS + Controller，可独立复用）
    TopToolbar/                            工具栏组件（gm-toolbar__menu / gm-toolbar__search / gm-toolbar__add / gm-toolbar__sync ...）
      TopToolbar.uxml / .uss
      TopToolbarController.cs              （含 Sync All 按钮 SyncAllButton / SyncAllClicked；AddMenu 支持自定义菜单项）
      Controllers/                         各下拉控制器（AddMenu/More）
    ToolbarSearch/                       重写搜索框（替换 ToolbarSearchField）
    ToolbarDropdown/                     下拉按钮（基于 VisualElement；弹出菜单用 Unity 内置 `GenericMenu`，与编辑器右键菜单同款）
    Splitter/                            左右可拖分栏（替换 VisualSplitter，3px 实心黑条 / first pane 300px 起按窗口宽度钳制）
    GistList/                             左栏（Scroll + 空态）
    GistStatusBar/                      左栏底部状态栏
    GistDetails/                           右栏详情（描述页签 + 操作按钮 + 多选批量操作）
    GroupHeader/                           分组折叠行
    ItemRow/                                  Gist 条目行
    EmptyArea/                             空态组件
  Icons/…                                从编辑器内置资源导出的工具图标（含下拉箭头 d_dropdown.png，供上面的样式表引用）
```

## 架构要点

- **组件组合**：窗口根为 `Editor/GistManagerWindow.uxml`（Template/Instance 挂载 `TopToolbar` `Splitter` 左右分栏），右栏 `GistDetails`，左栏 `GistList` + `GistStatusBar`。各子组件独立带 USS + Controller；窗口不加载任何第三方 stylesheet，样式完全由组件 USS 提供。
- **框架级小扩展**：`TopToolbar` 新增 `toolbarSyncAllButton`（Sync All）及 `TopToolbarController.SyncAllButton / SyncAllClicked`；`AddMenuController` 与 `MoreMenuController` 均支持可选的自定义菜单项（gistmanager 的 “+” 只保留 从git链接添加Gist脚本 / Add Gist Script from git URL...，“⋮” 只保留 Reload Manifest / Gist管理器设置 / About）；`ItemRow.uss` 增加 `id-version` / `is-disabled` 两套灰显规则，用 `installed` 勾选列表行的 Installed 图标。所有扩展均为增量可选项，互不影响。
- **控件实现**：`Components/ToolbarDropdown/`（VisualElement 按钮皮肤 + Unity 内置 `GenericMenu` 弹出层）；`Components/ToolbarSearch/` 自重写搜索框；`Components/Splitter/`（3px 实心黑条，鼠标 `split-resize-left-right` 光标作为唯一的拖动反馈）。
- **图标**：用到的 9 个 PNG（含下拉箭头 `d_dropdown.png`）放在 `Editor/Icons/`（平铺）。样式表引用均用 `project://database/…?guid=…` 指向包内资产；`.meta` GUID 与引用一一对应。gist 的状态行只挂 `installed` 图标。
- **交互**：搜索框、分组折叠、Shift/Ctrl 多选、`↑`/`↓`/`PageUp`/`PageDown`/`Home`/`End` 键盘导航等。gist 主窗口支持 Shift/Ctrl 多选；右键菜单与详情面板均作用于所选集合。
- **折叠实现**：UI Toolkit 对 CloneTree/Instantiate 后行的 `display` 不会因父级 `.collapsed` class 重新解析，所以折叠的显隐直接由 `GroupHeaderController.ApplyRowDisplay()` 派发 inline `style.display`（折叠 + 非 selected → `None`；selected 行保持 `Flex`）。controller 同时给外层 GroupHeader 根与内层 `.gm-group-header` 打 `.collapsed`（caret 切换图标与 USS 兜底规则），caret 高亮/`.gm-group-header.collapsed .gm-item-row` 的 USS 规则保留作为双保险。选中状态变化（如折叠后 Ctrl+Click 取消唯一可见的选中行）也会触发重算并把该行隐藏。
- **Target Root 切换**：设置页下拉触发（三按钮：切换并移动 / 仅切换（不移动）/ 取消）。搬运由 `GistRelocateService` 执行——清单位置来自 `ProjectSettings/GistManager.asset` 的 `manifestRelativePath`（未记录回落 `Packages/gist-scripts/gists.json`），搬运前整体预检（任一目标 gist 目录非空、或目标存在同名 `gists.json`/`package.json` → 中止零改动），搬运失败逆序回滚，成功后 `manifestRelativePath` 写入资产，然后 `PackageManager.Client.Resolve()` 让 UPM 自动重解析嵌入式包（从 Assets 切回 Packages 后嵌入式包立即重新注册；从 Packages 切到 Assets 则解注册）。清单本身随根移动；CI 用根里的 `ProjectSettings/GistManager.asset` 解析清单位置（见「与 CI 的协同」）。
- **启用/禁用是以包（gist）为单位的**：没有“单文件启用开关”——文件行里的开关统一整包控制；禁用整个包 → `GistSyncService.BackupGistPackage(At)` 把整棵 gist 目录（连 .cs / .meta / 自动生成的 asmdef / 所有子目录）移到 `{targetRoot}/Backup~/Disabled/<包目录名>`（Unity 忽略该目录）；启用整个包或 Sync → `TryRestoreGistPackage(At)` 整体移回（字节一致、含本地修改、GUIDs 保留；目标已存在且非空则保留现场并提示不覆盖）。旧清单 `files[].meta`（逐文件记录 meta）仍兼容——禁用恢复时，同步单文件与整包恢复两条路径都行。覆盖下载前 `BackupOverwritten(At)` 仍复制本地版本到 `{targetRoot}/Backup~/Overwritten/<时间戳>/<相对路径>`。清单原子写把旧文件轮换到 `Backup~/gists.json.bak`、解析失败备份写 `Backup~/gists.json.bad-*`；加载时旧 stray（`gists.json.bak`/`gists.json.bad-*`/`gists.json.tmp-*` 及 `.meta`）自动迁入 `Backup~/`；Relocate 搬运时 Backup~ 作为根级单元整体迁移。

## 已知限制

- 生成的 asmdef `references` 为空：Gist 脚本之间、以及 Gist 脚本引用工程自有代码（Assembly-CSharp）时，需要手动在生成的 asmdef 里补引用；反之工程代码可以自由引用这些脚本（asmdef 默认 autoReferenced）。
- Auto 探测只看是否用到 `UnityEditor`（`using UnityEditor` 或 `UnityEditor.`），带 `#if UNITY_EDITOR` 守卫的 runtime 脚本会被归到 Runtime（正确行为）；拿不准的用文件行的归属下拉手改。
- 禁用时整棵包目录（含 `.meta`/asmdef）移到 `{targetRoot}/Backup~/Disabled/`（Unity 忽略该目录、不编译、Project 视图不可见），`Backup~/` 目录本身保留；启用时整体移回。
- 同步不会拉取上游**新增**的文件：`rawUrl` 只回填缺失项，文件清单的增删只由 Add 或手动编辑触发（避免同步悄悄改写你的选择）。
- 上游改名或删除的文件会在同步时报 `Skipped` 并跳过，清单条目保留，可手动删除或重新 Add。
- 左栏分组的折叠状态记入 `ProjectSettings/GistManager.asset` 的 `collapsedAuthors`（ScriptableSingleton，随工程一起提交）：跨域重载、关闭窗口、重开编辑器都保留。0.2.0 之前的 EditorPrefs 旧值（`GistScriptManager.CollapsedAuthors`）会在首次加载时自动迁移进资产。
- 框架 USS（含所有原子组件）以深色皮肤为基准，未做浅皮肤两套适配；行悬停依赖 USS `:hover`。
- gist 主窗口支持 Shift/Ctrl 多选：左键切换、Ctrl/Cmd+Click 增删一个、Shift+Click 扩展选区；多选时右键菜单与 MultiSelectDetails 面板显示批量操作（多选时对 `Open in Browser` 无意义、自动禁用）；箭头键的键盘导航仍按单选移动（改为替换选区为单行）。
