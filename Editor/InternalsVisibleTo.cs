using System.Runtime.CompilerServices;

// 测试程序集需要访问显式路径的内部重载（SaveAtPath / LoadOrCreateAtPath），避免测试触碰真实的 Packages/gist-scripts/gists.json。
[assembly: InternalsVisibleTo("MUD.Modules.GistManager.Tests.Editor")]
