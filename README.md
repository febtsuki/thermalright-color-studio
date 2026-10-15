# Thermalright Color Studio

为 **Thermalright PS120 DIGITAL / LF8 面板**制作的 Windows 配色与监控工具。

[English](README.en.md) · [下载 0.3.6](https://github.com/febtsuki/thermalright-color-studio/releases/latest) · [SignalRGB 接入](docs/SignalRGB.md)

![Color Studio 界面，数字为模拟示例](docs/screenshots/studio.png)

这是独立开源项目，与 Thermalright、TRCC 或 SignalRGB 官方无隶属关系。当前是经过实机验证的早期预览，专注于颜色控制，不保证支持所有 Thermalright 设备。

## 能做什么

- 自选 1–16 色渐变、任意角度、径向、镜像、环绕、条纹和按指标区域拼色。
- 流动、往返、呼吸、扫光、波纹，以及温度/负载联动。
- 内置 HSV 色轮和方形取色窗，HEX/RGB 编辑，深色和浅色界面。
- 真实 CPU/GPU 温度、功率、频率和负载；CPU/GPU 轮播与 °C/°F。
- 启动自动连接、关闭转入托盘、设置保存、预设导入导出、撤销/重做。
- **SignalRGB 全局灯效同步**，保留本软件生成的真实数字；数据中断时可回到独立配色。
- **0–100% 空间高斯平滑**，柔化 SignalRGB 色块之间的生硬过渡。
- **同步调色滤镜**：曝光、亮度增益、饱和度、对比度，带旁路开关与一键重置。
- 可选登录自启动，无需每次手动启动脚本。

## 已验证的环境

| 项目 | 范围 |
| --- | --- |
| 系统 | Windows 11 x64 |
| 面板 | PS120 DIGITAL，USB `0416:8001`，握手 `PM=49 / SUB=96`，LF8 93 段 |
| SignalRGB | 2.5.74、2.5.80 |
| 传感器 | Intel CPU / NVIDIA GPU 实机验证；使用 LibreHardwareMonitor |

相同 VID/PID 不代表相同布局。程序会检查身份，不向未知布局发送颜色。其他型号与休眠/断线恢复尚未全面验证；没有风扇 PWM 或固件修改功能。

## 使用

1. 从 [Releases](https://github.com/febtsuki/thermalright-color-studio/releases) 下载 `thermalright-color-studio-v0.3.6-win-x64.zip` 并解压到可写目录。
2. 完全退出原版 TRCC，包括系统托盘，避免同时写入 USB 面板。
3. 双击 `ThermalrightColorStudio.exe`。程序会请求管理员权限，用于读取底层传感器；.NET 运行库已包含。
4. CPU 底层传感器还需要单独安装官方 [PawnIO 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0)。本包不包含或自动安装驱动。
5. 启动后自动连接受支持的面板；传感器初始化可能需要数十秒。缺失或过期的读数留空，不显示伪造的零。

设置保存在 EXE 旁的 `data/`，升级时复制该文件夹即可保留配置。关闭行为可选“后台运行（系统托盘）”或“直接退出”。左侧“退出软件”始终完全退出。

### SignalRGB 与平滑

请参阅 [接入说明](docs/SignalRGB.md)。两款软件需要同时运行；Color Studio 独占 USB，SignalRGB 通过本机回环网络发送颜色。0.3.6 会优先把插件安装到正在运行的 SignalRGB 版本目录；已有程序目录安装可在版本更新后自动恢复。恢复后可能仍需重启 SignalRGB 加载，详见接入说明。

在“灯光来源”选择 SignalRGB 同步，调节“空间平滑”。**0% 保留原色，40% 可作为初次对比值**。强度越高，颜色越柔和，也会减少局部反差。平滑发生在生成数字之前，不会模糊数字形状。每个实体段仍只能显示一种颜色。

“同步调色”提供曝光 ±3 EV、亮度增益/饱和度/对比度 0–200%。它在平滑后处理颜色，可补偿平滑后的亮度与色彩反差。默认曝光 0、其他 100%，保留原效果；取消“启用调色”可旁路对比。纯黑保持熄灭，重置只还原调色，不改变平滑。整体亮度仍在最后控制输出。

### 登录自启动

在解压目录打开 PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./Manage-Autostart.ps1
```

确认管理员授权后，创建当前用户登录后延迟 15 秒运行的计划任务。取消时追加 `-Disable`。移动目录后需重新注册，以更新 EXE 路径。当前登录自启动会显示主窗口，关闭后可转入托盘。SignalRGB 的自启动由其自身设置管理。

## 从源码构建

需要 Windows x64 和 .NET SDK **10.0.401**。

```powershell
./build.ps1
```

输出在 `publish/`。NuGet 依赖由 `packages.lock.json` 锁定。运行不涉及硬件的 64 项检查：

```powershell
dotnet ./src/ThermalrightColorStudio/bin/Release/net10.0-windows/win-x64/ThermalrightColorStudio.dll --self-test ./test-results.txt
Get-Content ./test-results.txt
```

GitHub Actions 在 Windows 上执行同样的构建和检查。测试与实机验证范围见 [验证记录](docs/Validation.md)；协议与实现见 [架构说明](docs/Architecture.md)。

## 贡献与许可

欢迎问题报告、兼容设备测试和 PR，见 [CONTRIBUTING.md](CONTRIBUTING.md)。

本项目采用 **GPL-3.0**。LF8 布局、数字段分组和线序改编自 [Lexonight1/thermalright-trcc-linux](https://github.com/Lexonight1/thermalright-trcc-linux)，保留其许可与署名。依赖及相关源码链接见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。不分发原版 TRCC 二进制、专有库或反编译源码。
