# Apple Music Desktop Lyrics

Apple Music 桌面歌词伴侣。当前提供 Windows 可运行版本和 macOS 原生预览实现。

完整安装、操作与故障排查请参阅 [中文详细使用说明](USER_GUIDE.zh-CN.md)。

## 仓库结构

- [`windows/`](windows/)：可运行的 Windows 桌面歌词应用（WPF / .NET）
- [`macos/`](macos/)：SwiftUI/AppKit 原生版本，可在 Mac 上用 Xcode 编译和实机调试
- [`ARCHITECTURE.md`](ARCHITECTURE.md)：跨平台边界、数据流和隐私说明

## Windows 功能

- 透明桌面歌词、置顶、锁定、鼠标穿透、缩放与托盘管理
- 优先使用 LRCLIB 同步歌词，找不到时回退到 Apple Music 自带歌词
- 逐行卡拉 OK 填色与精选字体切换
- 每首歌曲独立保存时间偏移
- 按歌手、合作歌手或组合应用稳定单色/渐变配色
- 独立管理界面和本地窗口状态保存

## 快速运行

需要 Windows 10 1809 或更高版本以及 .NET 10 SDK：

```powershell
dotnet run --project .\windows\AppleMusicDesktopLyrics.csproj
```

发布独立的 Windows x64 单文件程序：

```powershell
dotnet publish .\windows\AppleMusicDesktopLyrics.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o .\windows\publish
```

## 数据与隐私

应用通过系统媒体会话读取当前曲目的歌名、歌手、专辑、时长、播放状态和进度，并通过
只有 LRCLIB 找不到歌词时，Windows UI Automation 才会读取 Apple Music 已显示的歌词行；兼容提供 `CurrentLine` 的版本，
也兼容仅提供虚拟化 `Line` 列表的新版本。此过程不读取
Apple ID、密码、Cookie 或令牌。为匹配首选同步歌词，曲目信息会发送给 LRCLIB；LRCLIB
无结果时才启用 Apple Music 界面后备读取。窗口、字体和逐歌曲时间偏移设置只保存在本机。

## 状态

Windows 版本处于早期可用阶段。macOS 版本已实现功能骨架，但尚未在 macOS 环境编译和实机校准。

本仓库目前未附带开源许可证；在许可证确定前，默认版权规则仍然适用。
