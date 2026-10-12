# AdoriA MUSIC

支持网易云音乐与 QQ 音乐的 Windows 悬浮窗：封面、同步歌词、播放进度，以及白、蓝、灰、黑四种配色和毛玻璃效果。

## 下载

从 [最新版 Releases](https://github.com/114514homo666/AdoriA-Music/releases/latest) 下载 `AdoriA-Music-v1.3.0-win-x64.zip`，完整解压后运行 `AdoriA-Music.exe`。升级前请退出旧版。

无需安装或管理员权限。支持 Windows 10/11 64 位，使用系统自带的 .NET Framework 4.x；QQ 系统媒体接口需要 Windows 10 1809 或更新版本。

## 外观

顶部调色盘或右键“颜色与毛玻璃”可切换四种颜色，并打开或关闭毛玻璃。文字、进度、按钮、颜色菜单、播放器菜单、右键菜单及子菜单都随主题变化；配色切换带过渡动画，偏好会保存。

![配套颜色菜单](preview-menu-blue.jpg)

毛玻璃使用 Windows 11 22H2 及更新版本的桌面 Acrylic。较早系统使用普通背景；显示效果还受 Windows 透明效果设置、远程桌面和图形环境影响。软件不更改系统设置。

## 播放器与歌词

- 点击播放器名称切换网易云音乐／QQ 音乐；切换读取来源不会改变音乐播放。
- 显示封面、歌手、主歌名、同步歌词、翻译、播放进度及总时长。
- 图钉控制置顶；− / + 切换完整／精简模式；拖动空白处移动窗口。
- QQ 音乐需要启用“设置 → 常规设置 → 通知 → 显示系统媒体传输控件（SMTC）”。
- QQ 歌词联网匹配歌名、歌手及有效时长，保留版本区分；网络失败自动重试，右键可以重新获取或复制诊断信息，也可导入 LRC。

完整操作、数据来源与限制见 [使用说明](使用说明.md)。

## 从源码构建与验证

在 Windows PowerShell 中运行 `./build.ps1`，会生成 `AdoriA-Music.exe`。不依赖 NuGet 或额外安装工具，使用 Windows 自带的 C# 编译器和 WPF。

运行 `./verify.ps1` 执行歌词检查及屏幕外界面检查。验证记录见 [验证记录](验证记录.md)。本次发布包含自 v1.1.0 以来的 QQ 音乐支持和 v1.2.1 歌词兼容修复。

缓存及诊断位于 `%LOCALAPPDATA%\AdoriA-Music\`，窗口设置位于程序旁。软件不读取登录凭据、不注入代码、不修改播放器文件。

参考：[Microsoft 桌面背景材料](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)、[Microsoft WPF 玻璃区域](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/extend-glass-frame-into-a-wpf-application)、[QQ 音乐公开接口研究](https://github.com/WXRIW/Lyricify-Lyrics-Helper/blob/master/Lyricify.Lyrics.Helper/Providers/Web/QQMusic/Api.cs)、[网易云本地播放数据研究](https://github.com/YUCLing/netease-watcher)。DeepSeek 参与动画、系统媒体适配及歌词复核，Codex 完成集成、配色与菜单、原生毛玻璃及验证。本项目与两个播放器厂商无关联。
