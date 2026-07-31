# macOS 原生版

macOS 版使用 Swift、SwiftUI 与 AppKit 实现，目标系统为 macOS 13 或更高版本。当前代码已包含
完整功能骨架，但因为开发环境不是 macOS，尚未经过 Xcode 编译和 Music 实机辅助功能树校准。

## 已实现功能

- 透明、无边框、可缩放的桌面歌词悬浮窗。
- 当前句、下一句、渐变进度填色和文字阴影。
- 窗口大小变化时自动缩放字号。
- 菜单栏常驻图标和独立管理界面。
- 置顶、隐藏、退出以及关闭到菜单栏。
- 锁定后悬浮窗整体鼠标穿透。
- 未锁定时仅歌词黑框和 6 像素缩放边缘接收鼠标，其余透明区域穿透。
- 鼠标在歌词区域停留 1 秒后出现独立解锁按钮；按钮之外仍然穿透。
- 每次将歌词调慢/调快 0.5 秒，并按歌曲保存偏移。
- 保存窗口位置、大小、锁定、置顶、字体和颜色设置。
- 精选 Mac 字体列表，仅显示本机已经安装的字体。
- 手动颜色和按歌手自动配色，包括合作歌手与组合色。
- LRCLIB 同步歌词优先；找不到时尝试读取 Music 自带歌词。
- LRCLIB 的严格歌手搜索、宽松标题搜索、时长/歌手/专辑综合排序。
- LRCLIB 多候选版本切换与逐歌曲选择记忆。
- 普通整句着色（默认）与卡拉 OK 扫色模式，快捷栏和管理界面均可切换并保存。
- 默认关闭的 Music 实验自动对时；稳定时间戳填色与单调时钟平滑推进。
- Music 前奏或间奏三个点的伴奏状态识别框架。
- 本地 `.lrc` 导入、编辑、永久覆盖以及 LRCLIB 离线缓存。
- 自定义歌手配色增删、多人渐变顺序和 JSON 配色库导入/导出。
- 约 30 帧卡拉 OK 扫色、高字密度快句自适应和官方歌词高置信度停滞恢复。

## 项目结构

```text
macos/
├── Package.swift
├── Resources/
│   ├── Info.plist
│   ├── AppIcon.icns
│   └── AppIcon.iconset/
├── scripts/build-app.sh
└── Sources/AppleMusicDesktopLyricsMac/
    ├── AppDelegate.swift
    ├── AppleMusicPlayer.swift
    ├── AppleMusicAccessibilityLyricsProvider.swift
    ├── LyricsCoordinator.swift
    ├── LRCLIBClient.swift
    ├── LRC.swift
    ├── OverlayView.swift
    ├── OverlayWindowController.swift
    ├── ManagementView.swift
    ├── ArtistColorEngine.swift
    ├── SettingsStore.swift
    └── Models.swift
```

## 在 Xcode 中运行

1. 将整个仓库复制或克隆到 Mac。
2. 安装 Xcode 15 或更新版本，并至少启动一次完成组件安装。
3. 在 Finder 中双击 `macos/Package.swift`，或者在终端运行：

   ```bash
   cd macos
   open Package.swift
   ```

4. 在 Xcode 顶部选择 `AppleMusicDesktopLyricsMac` scheme 和 `My Mac`。
5. 按 `Command-R` 运行。

Swift Package 方式便于先解决编译问题和调试辅助功能读取。确认运行稳定后，可使用下方脚本生成标准 `.app`。

## 生成 .app

```bash
cd macos
chmod +x scripts/build-app.sh
./scripts/build-app.sh
```

输出位置：

```text
macos/dist/Apple Music Desktop Lyrics.app
```

当前脚本使用本地构建，不包含 Developer ID 分发签名与公证。若系统提示应用来自未知开发者，可在
“系统设置 → 隐私与安全性”中确认打开。正式分发时需要在 Xcode 中配置开发者团队、签名和公证。

## 首次权限设置

首次运行时需要两类权限：

### 自动化 → Music

用于通过 Music AppleScript 字典读取当前歌曲、歌手、专辑、时长、播放位置和播放状态。

路径通常为：

```text
系统设置 → 隐私与安全性 → 自动化 → Apple Music 桌面歌词 → Music
```

### 辅助功能

LRCLIB 找不到同步歌词时用于官方后备显示；用户主动开启实验选项后，也可用于只读自动对时。

路径通常为：

```text
系统设置 → 隐私与安全性 → 辅助功能 → Apple Music 桌面歌词
```

如果从 Xcode 运行，权限列表里可能显示 Xcode、SwiftPM 生成的可执行文件或应用名称。重新组装 `.app`
或改变签名后，macOS 可能把它视为新程序，需要重新授权。

## 歌词来源

顺序固定为：

1. 当前歌曲的本地 LRC 永久覆盖。
2. LRCLIB 同步歌词。
3. 之前成功获取的 LRCLIB 本地缓存。
4. Music 自带歌词辅助功能读取。
5. 显示“未找到同步歌词”。

LRCLIB 有逐行时间戳，适合稳定换句、伴奏空档和逐歌曲偏移。Music 自带歌词没有公开 API，后备实现会：

- 寻找 `com.apple.Music` 进程和主窗口。
- 收集窗口最右侧歌词区域的 `AXStaticText`。
- 优先根据选中状态、字体粗细和前景色对比度定位高亮行。
- 如果样式没有通过 AX 暴露，则使用歌词面板的垂直锚点作为后备判断。
- 将仅由 `•`、`.` 或 `·` 组成的当前文本识别为伴奏状态。

不同 macOS/Music 版本的辅助功能结构可能变化。这一部分必须在目标 Mac 上实测校准，但不会影响
LRCLIB 默认路径。

## 实机测试清单

### 基础播放

- 播放、暂停、拖动 Music 进度后，桌面歌词是否及时跟随。
- 切换歌曲后，歌名、歌手、颜色和偏移是否更新。
- 中文、日文、英文和多行歌词是否正确换行。
- 前奏、间奏和长空档是否不会提前给下一句填色。

### 窗口

- 拖动窗口并从四角缩放，字号是否同步变化。
- 退出后重新启动，位置和大小是否恢复。
- 多显示器、不同缩放比例、全屏应用和切换 Space 是否正常。
- 置顶关闭后是否回到普通窗口层级。

### 锁定穿透

- 锁定后能否点击歌词后面的网页、聊天框和按钮。
- 除解锁按钮外是否没有其他区域拦截点击。
- 停留 1 秒后解锁按钮是否出现，移出后是否消失。

### 菜单栏与管理

- 隐藏歌词后程序是否仍在菜单栏运行。
- 管理窗口能否切换字体、手动颜色、自动配色、锁定和置顶。
- 快捷栏的整行/麦克风图标能否切换普通与卡拉 OK 模式并在重启后保留。
- “退出”是否真正结束程序。

### 权限与后备歌词

- 不授予辅助功能权限时，LRCLIB 歌词是否仍可使用。
- LRCLIB 找不到时，授予权限并打开 Music 歌词面板后能否读到官方歌词。
- 拒绝自动化权限后，管理界面是否仍能打开且不会崩溃。

## 已知需要 Mac 实机确认的部分

- `NSAppleScript` 返回的 `player state` 在目标系统语言下是否始终包含 `playing`。
- Music 歌词区的 AX 文本、颜色和字体属性是否与当前启发式一致。
- 锁定窗口使用全局鼠标位置显示独立解锁按钮时，在多显示器坐标系下是否准确。
- Swift Package 从 Xcode 运行时的 Automation/TCC 授权身份。
- 无开发者签名的本地 `.app` 在目标 macOS 版本上的 Gatekeeper 行为。

## 调试建议

1. 先确认菜单栏图标出现，再检查悬浮窗。
2. 管理界面中的“歌词来源”应优先显示 `LRCLIB 同步歌词`，只有无结果时
   才显示 `Apple Music 官方歌词`。
3. 若曲目信息始终为空，在“系统设置 → 隐私与安全性 → 自动化”重新允许控制 Music。
4. 若只有官方后备歌词失败，先手动打开 Music 的歌词面板，再重新读取。
5. Xcode 编译错误请保留完整错误文本和对应 Swift 文件行号。
6. 实机 UI 或同步问题请同时提供 Music 歌词面板、悬浮窗、macOS 版本、Music 版本和显示器缩放信息。

## 与 Windows 版的差异

- Windows 通过系统媒体会话读取曲目；macOS 通过 Music AppleScript 字典读取。
- Windows 通过 UI Automation 读取官方后备歌词；macOS 使用 AXUIElement 辅助功能接口。
- Windows 使用任务栏托盘；macOS 使用菜单栏状态项。
- 两端共享相同的 LRCLIB 优先级、LRC 解析思路、歌手色表、逐歌曲偏移和交互目标，但设置文件格式
  与窗口实现是平台原生的。
