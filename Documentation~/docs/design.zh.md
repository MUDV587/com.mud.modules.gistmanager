# 设计与内部实现

![Design](images/design-zh.png)

## 概览

Gist Manager 是 Unity 2022.3 LTS 编辑器工具；目的让 GitHub Gist 上散落的脚本片段进工程并**可审计、可溯源**。

```
MUD.Modules.GistManager (编辑器程序集)
├── GistManagerWindow          ← UI 主窗口（UI Toolkit）
├── GistManifestStore          ← gists.json 读/写 + 原子替换 + .bak 轮换
├── GistCatalog                ← 合并 / packageDir 冲突 / 迁移（纯逻辑）
├── GistSyncService            ← 下载/移除/asmdef 生成/.meta 记账/sha256
├── GistApiClient              ← 异步 GitHub API（UnityWebRequest）
├── GistSearch                 ← 左栏搜索/空态文案（纯逻辑）
├── GistRelocateService        ← Target Root 搬迁
└── GistManagerSettings        ← ScriptableSingleton（ProjectSettings/GistManager.asset）
```

## 数据流

```
Add（点击 "+"）  → GistUrlParser → FetchGistAsync → 合并写回 → gists.json
Sync（按钮）     → GistSyncService.SyncAsync    → gists.json × 文件 + asmdef
切换 Target Root → GistRelocateService.RelocateAt → 原子搬迁 + 通知 UPM 重解析
```

## 关键决策

1. **`gists.json` 是唯一事实源**：所有写路径都经过它。GUI 读写、CI 读取、手动编辑全部通过同一个文件。

2. **添加 ≠ 下载**：Add 只拉取元数据（owner/描述/文件列表），不下载文件内容；下载只由 Sync 触发。

3. **`Auto` 归属**：编辑器用到 `UnityEditor` 的代码→归 Editor；只引用 Engine → Runtime。可以按文件行下拉手改。

4. **文件级 sha256**：工具写出文件时算出指纹存进清单；Sync 比对磁盘与清单，不一致 → 跳过。首次从旧清单/手写清单加载时，`Normalize` 自动把当前磁盘内容记为基线。强制覆盖是独立按钮（带二次确认）。

5. **包级 Enable/Disable**：禁用时整棵目录移入 `Backup~/Disabled/`（Unity 忽略）；启用时原样移回。本地编辑和 GUID 都保留。

6. **Backup~/ 是工具专属**：`.bak`（原子轮换）、`.bad-*`（损坏备份）、`Disabled/<pkg>`（禁用的包）、`Overwritten/<time>`（覆盖保险）、`.stray-*`（旧版残留自动迁入）——全部 Unity 忽略，可被 `gitignore`。

7. **Target Root 切换**是包里最危险的一段路径：先**整体预检**（目标已占用/非法 → 零改动），整个目录树一次性移动，然后 `PackageManager.Client.Resolve()` 通知 UPM 重解析。

## 程序集

单程序集 `MUD.Modules.GistManager.Editor`；纯逻辑（GistCatalog/GistSearch/GistManifestStore）的类独立成单测，UI 主窗口只做装配。
