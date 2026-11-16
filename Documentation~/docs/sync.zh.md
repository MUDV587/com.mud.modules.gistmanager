# 同步与本地改动

![Sync](images/sync-zh.png)

## Sync 都干了什么

对 `gists.json` 里每个 `enabled=true` 的 gist：

1. 拉 `raw_url` 写入目标路径 `targetPath`
2. 按实际文件归属（Editor/Runtime）自动生成/维护 asmdef；Unity 不识别无 asmdef 的包内脚本
3. 若目标根位于 `Packages/` 且没有 `package.json`，在 targetRoot 根上生成唯一的 `com.gistscripts.gists` 嵌入式包标记

## 本地改动受保护

工具写出的每个文件都在 `gists.json` 里记下 `sha256`。Sync 时，磁盘文件与记录指纹不一致 → **跳过**，状态栏出现 `skipped: N (locally modified)`；要强制覆盖请在详情页该文件行点 **Overwrite**（会再弹一次确认）。

## 写不进去时

- `raw_url` 缺失 / 路由丢失 → `skipped > missing raw_url`
- 上游改名 / 删除 → `skipped: file not found`
- 限流 → `HTTP 403` 包裹在 `GistApiException` 里，提示里会建议在设置里填 GitHub Token

## Sync ≠ Add

**Add 只写清单、不下载文件**。只有 Sync 动作才真正发网络请求并落地文件。
