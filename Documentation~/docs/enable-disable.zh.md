# 启用 / 禁用

![Enable](images/enable-zh.png)

## 语义

每个 gist 只有一个开关：**启用整个包**。它作用于整棵 gist 目录（子目录、所有文件、`.meta`、asmdef 全部一起走）。

- **启用** — 文件落盘在 `targetPath`；Sync 会下载它们。
- **禁用** — 整棵目录移动到 `Backup~/Disabled/<packageDir>`。Unity 忽略以 `~` 结尾的目录，禁用的 gist 不会编译、不会出现在工程里。`.meta` 一起带走，GUID 保留。

## 安全保证

禁用/启用走的是**目录搬运**（不是复制），因此：

1. **本地修改**字节一致地留在原地，下次启用照样能拿回来
2. 重启后 AssetDatabase 看到的是同一棵树——asmdef 正常、引用不中断
3. `Backup~/` 不会被任何常规清理动作删掉（它是工具专属的暂存区）

## Sync 里的语义

Sync 只下载 `enabled=true` 的文件。禁用的 gist 是"暂不存在"的——不下载、但也不算失败。

## 备份位置

`{targetRoot}/Backup~/Disabled/<packageDir>`。整棵 Backup~ 目录 Unity 完全忽略，CI 可直接跳过。
