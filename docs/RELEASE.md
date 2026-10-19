# 启序 · StartFlow v1.0.0

Windows 11 x64 的本地启动任务管理器。登录后自行选择方案并运行，支持顺序延迟与统一时间轴、独立重试、方案队列、托盘、双语主题和配置导入导出。无需账号或另装 .NET 运行时。

附件：

- `StartFlow-1.0.0-win-x64-Setup.exe` — 当前用户安装版。
- `StartFlow-1.0.0-win-x64-Portable.zip` — 完整解压到可写目录后运行。
- `StartFlow-1.0.0-Source.zip` — 与构建相应的完整源码与制作脚本。
- `SHA256SUMS.txt` — 下载完整性校验。

第一次使用：新建方案 → 添加项目 → 方案选项设置时间 → 运行。登录启动默认关闭，需在设置中自行开启。更新前建议导出备份；便携版保留 Data，更新程序文件后检查启动路径。卸载默认清理自己的配置，先导出备份。

限制：默认浏览器决定标签页/窗口；请求提交不代表加载完成；窗口状态可能被目标忽略；执行策略与 UAC 遵循 Windows。无真实代码签名证书，不宣称 SmartScreen 信任。完整实测情况以随附验证报告为准。

验证：本地 24 项自动化及 Windows 集成检查通过，实际目标启动、安装/升级/卸载、便携存储与退出已有记录。真正重新登录、睡眠/解锁、真实 UAC、部分托盘/拖拽/导入交互及多显示器 DPI 仍需人工回归；这些场景不宣称已全部通过。远程构建结果可在 [GitHub Actions](https://github.com/you2032zhu/StartFlow/actions/workflows/build.yml) 查看。

License: GPL-3.0-only。允许商业使用；分发覆盖的修改版本须遵守相应源码等许可证义务。

[使用与制作教程](https://github.com/you2032zhu/StartFlow/blob/HEAD/docs/用户与制作教程.md) · [完整验证报告](https://github.com/you2032zhu/StartFlow/blob/HEAD/docs/验证报告.md) · [报告问题](https://github.com/you2032zhu/StartFlow/issues)
