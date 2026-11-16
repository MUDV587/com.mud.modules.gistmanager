# 清单格式

![Manifest](images/manifest-zh.png)

`gists.json` 是**唯一事实源**。编辑器和 CI 都读它。可手动编辑，保存后窗口会自动读回；手动编辑正则不准也会被工具拒绝/覆盖。

```json
{
  "targetRoot": "Packages/gist-scripts",
  "gists": [
    {
      "id": "dfc43134fbfefb820d0adbc5d7c25fb8",
      "author": "yasirkula",
      "description": "unitypackage extractor",
      "packageDir": "yasirkula.unitypackage-extractor",
      "files": [
        {
          "filename": "UnitypackageExtractor.cs",
          "targetPath": "Packages/gist-scripts/yasirkula.unitypackage-extractor/Editor/UnitypackageExtractor.cs",
          "enabled": true,
          "rawUrl": "https://gist.githubusercontent.com/yasirkula/dfc43134/raw/UnitypackageExtractor.cs",
          "meta": "",
          "placement": "Editor",
          "sha256": "…"
        }
      ]
    }
  ]
}
```

## 顶层

- `targetRoot` — 所有 gist 的根目录（相对工程）。
- `gists` — gist 条目数组。

## 每条 gist

- `id` — gist ID（必填且唯一）。
- `author` — GitHub 用户名（缺省 `anonymous`）。
- `description` — 描述文本，窗口列表用。
- `packageDir` — `targetRoot` 下的子目录名（缺省 `{author}.{short-id}`）。
- `files` — 文件条目数组。

## 每个文件

- `filename` — 显示名。
- `targetPath` — 磁盘上的位置（正斜杠）。
- `enabled` — 包级开关（整个 gist、整个目录）。
- `rawUrl` — 文件 URL，从 GitHub 返回的 `raw_url` 取到。
- `sha256` — 上次写出时的指纹，用于检测本地改动。
- `placement` — `Auto`（按内容探测）/ `Editor` / `Runtime`。
- `meta` — **兼容旧清单**。新禁用不会再写入；旧清单里该字段会被作为同步时的兜底。
