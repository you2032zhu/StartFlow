# 启序 · StartFlow

[English](README.en.md) · [零基础使用与制作教程](docs/用户与制作教程.md) · [验证记录](docs/验证报告.md)

[下载最新版](https://github.com/you2032zhu/StartFlow/releases/latest) · [报告问题](https://github.com/you2032zhu/StartFlow/issues) · [构建记录](https://github.com/you2032zhu/StartFlow/actions/workflows/build.yml)

Windows 11 的本地启动任务管理器。登录后自行选择一套方案并点击运行，按顺序延迟或统一时间轴打开应用、商店应用、网站、文件、文件夹、快捷方式，或执行脚本和命令。

**这是实际 WPF 软件，默认不启用登录启动，也不自动执行默认方案。** 无账号、云服务、遥测、广告、后台更新或网页加载探测。

## 使用

从 [GitHub Releases](https://github.com/you2032zhu/StartFlow/releases) 下载 `StartFlow-1.0.0-win-x64-Setup.exe` 或 `StartFlow-1.0.0-win-x64-Portable.zip`。发行附件同时提供完整相应源码和 SHA-256 清单；自行构建的本地成品位于 `artifacts`。

- 安装版：当前用户安装，配置在 `%LOCALAPPDATA%\StartFlow`。
- 便携版：完整解压，双击 `StartFlow.exe`。配置在程序旁 `Data`，不要只移动 exe。
- 两版都自带 .NET，无需普通用户安装运行时。不要同时运行两个副本；同一用户共用一个实例和一个真实登录启动入口。
- 从左侧新建方案，再添加项目。使用“方案选项”设置调度模式；从“设置与数据 → 常规与登录启动”开启登录启动。
- 默认关闭时询问留在托盘或退出，支持记住选择。退出或停止只取消未来任务，不关闭已打开的软件。

## 构建

需要 Windows 11 x64、.NET SDK **10.0.402**（`global.json`，允许同补丁带更新）。没有应用层 NuGet 依赖。安装包使用 Inno Setup **7.1.0 x64**。

```powershell
dotnet build StartFlow.sln -c Release -m:1
powershell -NoProfile -File scripts/Build.ps1 -WindowsIntegration
powershell -NoProfile -File scripts/Publish.ps1 -InnoCompiler "C:\你的工具目录\ISCC.exe"
```

测试使用无第三方依赖的可执行测试项目，**不是** `dotnet test`；`Build.ps1` 会检查其退出码。集成测试临时写入唯一命名的当前用户启动值后删除，并在 `.test-data` 创建和清理目标，不使用用户配置。

## 工程

| 目录 | 职责 |
|---|---|
| `src/StartFlow.Core` | 模型、校验、原子存储、导入、登录策略、可取消调度 |
| `src/StartFlow.Windows` | 目标启动、进程识别、应用发现、HKCU 登录入口、单实例 |
| `src/StartFlow.App` | WPF 视图、MVVM、统一双语资源、主题、托盘 |
| `tests/StartFlow.Tests` | 可控时钟调度测试和实际 Windows 集成测试 |
| `installer` / `scripts` | 安装包、构建、自包含发布和源码打包 |
| `docs` / `.github` | 教程、取舍、验证、发行说明、CI、问题模板 |

## 平台限制

默认浏览器及其设置决定最终窗口、标签页和登录行为，无法保证所有浏览器均使用同一窗口。启动请求成功不等于软件已准备好或网页加载成功。窗口最大化/最小化是启动建议，目标可能忽略。无法读取进程路径时显示检测不可靠并尝试启动。带参数的软件默认不据模糊进程名跳过；可明确配置进程身份。快捷方式保留原始 .lnk 启动语义，不对所有 shell 快捷方式强行去重。

PowerShell 遵守当前执行策略；不会关闭 UAC。已创建的脚本进程退出前会占用当前方案的完成状态，后续项目仍按原计划提交。停止会停止等待退出结果，不会杀死该进程。已有且尚未处理的 Windows UAC 对话框无法由应用安全关闭；停止会等待正在提交的请求返回，期间不能重新运行。

本地首版未使用真实代码签名证书。请自行检查源码与 SHA-256；不要把构建成功等同于已取得 SmartScreen 信誉。完整验证状态见验证报告。

## 许可证

GPL-3.0-only，完整正文见 [LICENSE](LICENSE)。允许商业使用。向他人分发 GPL 覆盖的修改版或目标代码时，须按照许可证向接收者提供相应源码、许可和必要说明等；具体以正式许可证为准。第三方运行时及工具说明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。仓库维护账号：[you2032zhu](https://github.com/you2032zhu)。
