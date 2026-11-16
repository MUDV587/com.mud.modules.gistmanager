# 变更日志

完整历史见 [`CHANGELOG.md`](https://github.com/MUDV587/com.mud.modules.gistmanager/blob/main/CHANGELOG.md)。本文是最新版本概要。

## 0.3.0 新增

- **Target Root 切换** — `Packages/gist-scripts` ↔ `Assets/GistScripts` 可运行时搬运：清单 + 包标记 + 所有 gist 目录树及 `.meta` 一起搬家，失败整体回滚
- **包级启用/禁用** — 禁用把整棵 gist 目录移入 `Backup~/Disabled/`；启用原样移回
- **`.meta` 随包走** — `.meta` 搬过去避免出空壳文件夹
- **切换后自动通知 UPM 重解析（`Client.Resolve()`）** — 新嵌入式包立即出现、不再需要 Ctrl+R
- **gists.json 原子写** — `gists.json.tmp-*` → `File.Replace`；旧版轮换为 `Backup~/gists.json.bak`
- **损坏清单保护** — 非法 JSON 时拒绝覆盖，备份 `.bad-*` 保留，状态栏展示错误
- **网络请求超时 30 秒** — `Sync All` 卡死成为过去式

完整历史详见 [`CHANGELOG.md`](https://github.com/MUDV587/com.mud.modules.gistmanager/blob/main/CHANGELOG.md)。
