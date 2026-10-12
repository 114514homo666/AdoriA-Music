# AdoriA MUSIC

支持网易云音乐与 QQ 音乐的 Windows 悬浮窗，显示封面、同步歌词、进度与总时长，提供四种配色、毛玻璃和独立背景透明度。

## v1.5.1

从 [v1.5.1 发布页](https://github.com/114514homo666/AdoriA-Music/releases/tag/v1.5.1) 下载 AdoriA-Music-v1.5.1-win-x64.zip，完整解压，退出旧版悬浮窗后运行 AdoriA-Music.exe。无需安装或管理员权限。发布程序与已通过本地审查的 v1.5.1 程序一致。

本版修复四角外的灰色直角残边，并重新利用竖向长窗的空白。毛玻璃改为独立背景层，由 Windows 11 合成器绘制圆角；前景使用一致的 8 DIP 圆角，去掉窗口描边。Windows 10 使用普通透明背景，其他功能仍可使用。

## 窗口与外观

- 拖动任意四角或四边调整大小，宽高独立变化，可做横向歌词条、竖向长窗或小窗。最小尺寸 240×64，位置和尺寸自动保存。
- 竖向空间充足时，恢复封面、歌手、上下歌词、翻译、进度和时长。封面随可用空间变大，封面、歌手和歌名纵向居中排列，歌词另占中部。
- 小窗逐步收起次要内容；最小窗保留当前歌词、播放／暂停、调色板、设置和关闭。长歌词按空间换行或省略。
- 调色板提供白、蓝、灰、黑、毛玻璃开关及 0%–100% 背景不透明度。文字和图标不随背景变淡，默认背景 35%。
- 齿轮设置集中提供播放器切换、恢复大小、置顶、精简显示、歌词偏移、重试、诊断、LRC 导入、重连和退出。菜单随主题配色。
- 没有缩放滑条、Ctrl＋滚轮缩放、右键菜单、悬停提示或文字描边。过渡动画目标为 120 帧，本机采样的动画值变化中位间隔约 8.33 毫秒。

![完整窗口](preview-default-v151.jpg)

![竖向重排](preview-portrait-v151.jpg)

![横向歌词条](preview-wide-v151.jpg)

![最小窗口](preview-mini-v151.jpg)

## 播放器与歌词

顶部播放器名称或设置中选择网易云音乐／QQ 音乐；小窗使用设置。来源切换不改变歌曲播放。

QQ 音乐需启用“设置 → 常规设置 → 通知 → 显示系统媒体传输控件（SMTC）”。歌词按歌名、歌手、版本和有效时长匹配，网络失败自动重试。设置中可重试、复制诊断或导入 LRC。详见 [使用说明](使用说明.md)。

## 构建与验证

在 Windows PowerShell 中运行 ./build.ps1，使用系统自带的 C# 编译器与 WPF，无 NuGet 依赖。./verify.ps1 执行 30 项歌词及 87 项界面检查。本机实测见 [验证记录](验证记录.md)。

Windows 10/11 64 位、.NET Framework 4.x；QQ 系统媒体接口需要 Windows 10 1809 或更新版本。圆角毛玻璃需要 Windows 11 支持的系统合成接口，否则退回普通透明背景。实际效果与帧率取决于图形环境和显示器。

缓存和日志位于 %LOCALAPPDATA%\AdoriA-Music\，设置位于程序旁，不随压缩包分发。本版没有修改歌曲读取或歌词网络逻辑。

DeepSeek 通过 ACP 协助复核圆角残边；Codex 复现问题、实现独立背景层、重排竖向界面并完成集成验证。此前的拉伸计算、动画、滑条和系统媒体适配亦包含 DeepSeek 的贡献。

参考：[Microsoft 系统圆角说明](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners)、[Microsoft 动画目标帧率](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.animation.timeline.desiredframerate?view=windowsdesktop-10.0)。本项目与播放器厂商无关联。
