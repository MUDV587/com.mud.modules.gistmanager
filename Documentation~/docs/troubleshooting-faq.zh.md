# 故障排查与 FAQ

![FAQ](images/troubleshoot-zh.png)

## UPM：`com.gistscripts.gists` 没出现

嵌入式包会在 Target Root 切换、整包启用/禁用时通过 `PackageManager.Client.Resolve()` 自动重新注册。如果刚先于包物理更新打开了 UPM，按 `Ctrl+R` 刷新 Project 窗也行；或者切一次 Target Root 切回去即可。

## Sync：`skipped (locally modified)`

磁盘文件 ≠ 清单里记录的 `sha256`。要么是你手改过，要么是别的工具改过。

- 本地改动是垃圾 → 文件行 → **Overwrite**（会二次确认）。
- 本地改动重要 → 保留下来；如果还没 ready 合并上游，先禁用再恢复。

## asmdef 引用为空

默认生成的 asmdef `references` 一律为 `[]`。如果 gist 引用跨程序集边界的自有代码：

- 手动给生成的 `.asmdef` 补上引用，或
- 把 Target Root 切到 `Assets/GistScripts`——不产生程序集边界。

## `github.com rate limit 403`

匿名调用 GitHub API 每小时限 60 次。打开 **Project Settings → MUD → Gist Manager → GitHub Token**，贴一个 token进去；填完立即生效。

## 清单损坏 / 非法 JSON

状态栏显示解析错误，工具会**拒绝覆盖**该文件；

1. 坏文件被自动复制到 `Backup~/gists.json.bad-<时间戳>`
2. 加载一个空清单让 GUI 继续
3. 修好后窗口获焦时自动读回；也可以 ⋮ More → **Reload Manifest** 主动重读

## Target Root 切换提示 `Cannot switch: destination occupied`

目标位置已有 `gists.json` / `package.json` / 它们的 `.meta`，或者同名包目录（非空）。工具**不会覆盖**。要么先处理掉冲突，要么取消切换。

## 启用后 gist 没出现

Unity 忽略 `Backup~/`。启用后 UPM 里 / Project 里通常都会立刻看到；如果没看到，手动按一次 `Ctrl+R` 让 AssetDatabase 导入一次即可；必要时再点一次 Sync。
