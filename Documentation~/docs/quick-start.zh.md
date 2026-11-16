# 快速开始

## 1. 安装

```
Window → Package Manager → + → Install package from git URL...
https://github.com/MUDV587/com.mud.modules.gistmanager.git
```

## 2. 打开窗口

菜单：**Tools → MUD → Gist Manager**

## 3. 添加第一个 gist

在 **+** 栏（或者先 `Ctrl/Cmd+F` 聚焦搜索，再点 **+**）的输入框里粘贴 Gist URL：

```
https://gist.github.com/yasirkula/dfc43134
```

窗口会解析 URL、取文件清单（1 次 API 调用）、写入 `Packages/gist-scripts/gists.json`。**此时不会下载任何文件。**

## 4. Sync

点 **Sync All**（或者 **Ctrl/Cmd+Shift+G**）。工具按 `gists.json` 里启用状态下载文件。磁盘文件内容与清单里记录的 `sha256` 不一致 → **跳过、不覆盖**；若要强制覆盖，在详情页 → 文件行点 **Overwrite**（会再弹一次确认）。

## 5. 启用 / 禁用

勾选**启用整个包**可以开关整个 gist 包：关闭时，整棵 gist 目录（`.cs`/`.meta`/asmdef）整体移动到 `Packages/gist-scripts/Backup~/Disabled/<packageDir>`（Unity 完全忽略），重新勾选后原样移回，**本地改动不会丢失**。

## 6. CI 落地

```bash
# 例：按 gists.json 下载所有启用的文件
MANIFEST=$(grep -oP '^manifestRelativePath:\s*\K.+' ProjectSettings/GistManager.asset | head -1)
MANIFEST=${MANIFEST:-Packages/gist-scripts/gists.json}
# …遍历 enabled=true 的文件，curl 拉 rawUrl，写到 targetPath
```

`Packages/gist-scripts/gists.json` 是唯一事实源——提交进 git，按需提交 `.cs`，或者干脆 `.gitignore` 掉（`**/Backup~/` 建议一起忽略），CI 或一行 Sync 都能恢复。
