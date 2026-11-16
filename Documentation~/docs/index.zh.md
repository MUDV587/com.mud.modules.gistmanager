# MUD Gist Manager

把分散在 GitHub Gist 上的代码片段收进项目并统一管理。Gist 文件作为源代码落地，下载仅在显式点 Sync 时发生，同时
保留清单作为编辑器GUI与 CI 的共同事实源。

![MUD Gist Manager](images/menu.png)

## 它能做什么

- **Add**：粘贴 Gist URL，GistManager 解析元数据并写入 `gists.json`，**不会下载文件**，下载在你点 Sync 之后发生。
- **Sync**：下载清单里所有启用的 gist。磁盘文件内容与清单中记录的 `sha256` 不一致（即你手改过）→ **跳过、不覆盖**；详情页提供 **Overwrite** 按钮用于强制覆盖。
- **Enable / Disable（包级）**：禁用整个 gist（不是单个文件）时，把整棵 gist 目录（`.cs`/`.meta`/asmdef）移动到 `Backup~/Disabled/`（Unity 完全忽略）；启用时字节一致地移回，GUID 与本地改动原样保留。
- **两种存放根目录（Target Root）**：`Packages/gist-scripts`（嵌入式 UPM 包，遵循 `Packages/manifest.json` 的 `file:` 链接语义）或 `Assets/GistScripts`（普通资产目录）。切换 Target Root 时，**整个包及其目录树和 `.meta` 一并搬运**，并会自动让 UPM 重新解析 `packages-lock.json`。
- **多选、分组折叠、搜索、键盘导航、双语界面**（中文/英文，跟随系统语言）。

## 设计原则

- `gists.json` 是唯一事实源，编辑器 GUI 与 GitHub Actions 都读这一份清单。
- **添加 ≠ 同步**：Add 只写清单，下载走显式 Sync 动作。
- **Sync 保护本地改动**：每个文件记录上次写出时的 `sha256`；同步时若磁盘不一致 → 跳过，不覆盖。
- **包级启用/禁用**：整棵 gist 目录连 `.meta` 在 `Backup~/Disabled/` 里收养回家，GUID 和本地修改绝对不死。

## 快速安装

```
Window → Package Manager → + → Install package from git URL...
https://github.com/MUDV587/com.mud.modules.gistmanager.git
```

需要 Unity 2022.3 LTS（UI Toolkit）。唯一的依赖是 `com.unity.nuget.newtonsoft-json`，由 Unity Package Manager 自动解析。
