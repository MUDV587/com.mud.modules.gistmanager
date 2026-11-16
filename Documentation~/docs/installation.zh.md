# 安装

## 从 Git URL 安装

```
Window → Package Manager → + → Install package from git URL...
https://github.com/MUDV587/com.mud.modules.gistmanager.git
```

需要 Unity **2022.3 LTS**（UI Toolkit）。唯一依赖是 `com.unity.nuget.newtonsoft-json`（3.0.2）——Unity Package Manager 会自动解析。

## 通过 manifest.json 安装

在 `Packages/manifest.json` 里加一行：

```json
{
  "dependencies": {
    "com.mud.modules.gistmanager": "https://github.com/MUDV587/com.mud.modules.gistmanager.git"
  }
}
```

## 钉到固定 tag

```json
"com.mud.modules.gistmanager": "https://github.com/MUDV587/com.mud.modules.gistmanager.git#v0.3.0"
```

## 安装后产生的目录

```
Packages/com.mud.modules.gistmanager/    ← 包本体
Packages/gist-scripts/                   ← 默认目标根
  gists.json                             ← 首次 Sync 自动生成
  package.json                            ← 仅在 Packages 目标根下自动生成的嵌入式包标记
```

`Backup~` 位于目标根根目录，Unity 完全忽略，CI 可直接安全 `**/Backup~/`。

## Git URL 安装失败

若提示 `Package Manager error`，检查：

1. Unity 能访问 `github.com`（代理 / 防火墙）
2. `.git` URL 是否正确
3. 认证：GitHub 匿名调用限流 60 次/小时。在 **Project Settings → MUD → Gist Manager → GitHub Token** 里填 Token 可提升到 5,000 次/小时
