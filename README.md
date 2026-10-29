# 截图录像

Windows 桌面截图和录像程序。关闭主窗口后留在托盘，从托盘菜单退出。

## 功能

- 区域截图、当前显示器全屏截图。截图后可画笔、箭头、矩形、文字、马赛克，支持撤销和重做。
- 截图会复制到剪贴板，也可以保存为 PNG、贴到桌面。
- 区域录像、当前显示器全屏录像。分辨率不超过 2K（2560×1440），不放大画面。帧率 30 或 60。
- 录像前可分别开关系统声音和麦克风。暂停时画面和声音一起停。
- 输出 MP4（H.264 + AAC）。优先使用硬件编码。
- 截图和录像不能同时进行。

## 默认快捷键

可在设置里修改。

| 操作 | 快捷键 |
| --- | --- |
| 区域截图 | Ctrl+Alt+A |
| 全屏截图 | Ctrl+Alt+S |
| 区域录像 | Ctrl+Alt+R |
| 停止录像 | Ctrl+Alt+Q |

## 保存位置

默认目录是「视频\截图录像」，下面有「截图」和「录像」两个文件夹。设置保存在 `%AppData%\截图录像\settings.json`。

## 环境

- Windows 10 版本 2004（10.0.19041）或更高，64 位。
- 构建需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
- 录像依赖 [ScreenRecorderLib](https://github.com/sskodje/ScreenRecorderLib) 7.0.1，以及 Microsoft Visual C++ 2015–2022（x64）运行库。

## 构建

在项目目录执行：

```powershell
dotnet build 截图录像.csproj -c Release -p:Platform=x64
```

生成可直接双击运行的程序：

```powershell
dotnet publish 截图录像.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=false -o publish
```

运行 `publish\截图录像.exe`。
