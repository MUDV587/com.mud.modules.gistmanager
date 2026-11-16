# Gist 库界面

![Library](images/library-zh.png)

## 分组

- **yasirkula**、**tomkail** 等——按作者自动分组，可折叠，可记忆在 `ProjectSettings/GistManager.asset` 的 `collapsedAuthors` 字段里（跨域重载/关闭窗口/重开编辑器都保留）。

## 交互

| 操作 | 行为 |
| --- | --- |
| 点击行 | 选中 |
| Ctrl/Cmd + Click | 切换选中 |
| Shift + Click | 扩展选区 |
| ↑ / ↓ / PageUp / PageDown / Home / End | 移动/跳转 |
| Ctrl/Cmd + F | 聚焦搜索 |
| `Esc`（添加面板打开时） | 关闭添加面板 |

## 搜索

搜索（1/3 秒防抖）按作者、Gist ID、描述、文件名过滤；空查询恢复全部。

## 多选

多选后右栏切换为批量操作面板：`N 个 gist 已选` + 批量同步/批量启用/批量禁用/批量移除。按钮只在多选模式显示。
