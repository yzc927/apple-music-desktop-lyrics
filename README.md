# Apple Music Desktop Lyrics

Apple Music 桌面歌词伴侣。当前提供 Windows 可运行版本和 macOS 原生预览实现。

完整安装、操作与故障排查请参阅 [中文详细使用说明](USER_GUIDE.zh-CN.md)。

## 仓库结构

- [`windows/`](windows/)：可运行的 Windows 桌面歌词应用（WPF / .NET）
- [`macos/`](macos/)：SwiftUI/AppKit 原生版本，可在 Mac 上用 Xcode 编译和实机调试
- [`ARCHITECTURE.md`](ARCHITECTURE.md)：跨平台边界、数据流和隐私说明
- [`assets/app-icon.png`](assets/app-icon.png)：Windows 与 macOS 共用的透明图标母版

## Windows 功能

- 透明桌面歌词、置顶、锁定、局部鼠标穿透、缩放与托盘管理
- 鼠标悬停快捷栏可直接上一首、播放／暂停和下一首
- 默认跟随 Apple Music：Apple Music 打开时自动显示歌词，退出时自动隐藏；首次启动会打开管理界面，Windows 登录启动由管理器单独控制，新安装默认关闭
- 优先使用 LRCLIB；找不到时才回退到官方歌词，Apple 自动对时作为默认关闭的实验选项
- 默认普通整句着色，也可切换卡拉 OK 稳定扫色；同一句进度只前进不回退，并在换句前自然完成
- 日语歌词中同时出现汉字和假名时，自动在汉字上方标注平假名读音；中文和其他语言保持原样
- 悬浮快捷栏与管理界面均可切换歌词显示模式，并提供精选字体切换
- 每首歌曲独立保存时间偏移和手动选择的歌词版本
- 按歌手、合作歌手或组合应用稳定单色/渐变配色
- 导入、编辑并永久覆盖单曲 `.lrc`，自动缓存已获取歌词供离线后备
- 支持 Enhanced LRC 词级时间戳与真实词级扫色；编辑器可逐行/逐词打点、微调和批量平移
- 在管理器中增删歌手配色、调整多人渐变顺序并导入/导出配色库
- 独立管理界面和本地窗口状态保存

## 快速运行

从源码运行需要 Windows 10 2004（19041）或更高版本（包括 Windows 11）以及 .NET 10 SDK：

```powershell
dotnet run --project .\windows\AppleMusicDesktopLyrics.csproj
```

发布独立的 Windows x64 单文件程序：

```powershell
.\scripts\publish-windows.ps1
```

自包含发布版不需要另外安装 .NET。当前公开包不做代码签名，浏览器下载后 Windows SmartScreen
可能显示“未知发布者”；请核对 Release 页面提供的 SHA-256 后再运行。
脚本只会向新的 `artifacts/windows-x64` 目录写入一个 EXE，并在 `artifacts` 下生成 SHA-256；
它拒绝覆盖已有目录，避免把本地截图、缓存或旧构建混入正式包。

## 数据与隐私

应用通过系统媒体会话读取当前曲目的歌名、歌手、专辑、时长、播放状态和进度。LRCLIB 找不到歌词时，
Windows UI Automation 会按“当前伴奏标记、`CurrentLine` 当前行、虚拟化 `Line` 列表”三个独立策略读取 Apple Music 已显示的歌词行作为后备；LRCLIB 可用且歌词面板已经打开时，
用户主动开启实验自动对时后，也会以只读方式将官方当前行作为校准信号；若连续确认的官方两句与 LRCLIB 正文明显无关，则清除该错误缓存并切换官方歌词。它兼容提供 `CurrentLine` 的版本，
也兼容仅提供虚拟化 `Line` 列表的新版本。此过程不读取 Apple ID、密码、Cookie 或令牌。为匹配首选同步歌词，
只有歌名和歌手会发送给 LRCLIB；专辑与歌曲时长仅在本机用于筛选返回候选。无结果时才会自动打开 Apple Music 歌词面板。窗口、字体、逐歌曲时间偏移和
歌词版本选择只保存在本机。Apple 后备连续读取失败时会明确显示不可用原因，并保留导入本地 LRC 和手动重新获取入口，不会冻结在旧歌词上。

## 状态

Windows 版本处于早期可用阶段。macOS 版本已实现功能骨架，但尚未在 macOS 环境编译和实机校准。

本仓库目前未附带开源许可证；在许可证确定前，默认版权规则仍然适用。
