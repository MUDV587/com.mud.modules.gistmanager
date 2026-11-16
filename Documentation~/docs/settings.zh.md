# 设置页

**Edit → Project Settings → MUD → Gist Manager**

![Settings](images/settings-zh.png)

## 界面语言

`跟随系统（自动）` / `中文` / `English`，默认：Unity `Application.systemLanguage`（系统语言以 Chinese 开头视为中文）。切换后主窗口立即重建，Project Settings 页下一次重绘时切换。

## Target Root

`Packages/gist-scripts`（默认，嵌入式 UPM 包）或者 `Assets/GistScripts`（普通资产目录）。切换 Target Root 时，**整个目录树**（清单、package.json、所有 gist 文件及其 .meta）整体搬到新根；旧根清空后整体删除。

若新根已有同名目录（同 packageDir 但非空），或已有 gists.json / package.json / 它们的 .meta → **整体中止，零改动**。详见 [切换 Target Root](target-root.md)。

## GitHub Token

存 `EditorPrefs`（本机、不入版本库）。匿名请求 60 次/小时；填 Token 后 5,000 次/小时。

## 上游更新检查

Sync 时顺带每条目调一次 GitHub Gist API，对比 `rawUrl`（内含 commit SHA）的文件清单，有差异时仅在状态栏提示 `Upstream updated:`，**不会改动你的清单**。可关以节省 API 调用。

## 清单路径

从 `ProjectSettings/GistManager.asset` → `manifestRelativePath` 读取，缺省回落到 `Packages/gist-scripts/gists.json`。本面板直接显示当前生效的清单位置，随 Target Root 联动变化。
