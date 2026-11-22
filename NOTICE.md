# NOTICE — 准点定时关机管家

Copyright 2026 TheSun_Also_Rises

本仓库中的原创源码、图标、文档与交互原型以 **Apache License 2.0** 授权，许可全文见 [LICENSE](LICENSE)。

## 第三方组件

| 组件 | 参考版本 | 许可 | 用途 | 许可全文 / 来源 |
| --- | --- | --- | --- | --- |
| Microsoft .NET 8 Runtime / WindowsDesktop (WinForms) | 8.0.x | MIT | 运行时与 UI 框架，随自包含 EXE 打包 | https://github.com/dotnet/runtime/blob/main/LICENSE.TXT |
| Microsoft.Windows.SDK.NET 相关 Win32 API | Windows 10/11 SDK | 微软 SDK 许可 | P/Invoke 调用电源管理、空闲检测等系统 API（仅引用，不分发） | 随 Windows SDK 提供 |
| Python Pillow | 10.x+ | HPND | 仅构建期用于程序化生成应用图标，不进入运行时 | https://github.com/python-pillow/Pillow/blob/main/LICENSE |

> 参考版本为本项目的开发与验证环境；使用更新版本通常兼容，但不在本项目测试范围内。

## 二进制分发说明

- 本项目源码运行时仅依赖 .NET 8 Windows Desktop 框架；发布产物为 **self-contained 单文件 EXE**，将 .NET 运行时与依赖库一并打包（静态捆绑），终端用户无需另行安装 .NET
- 本仓库**不提交** `dist/`、`.exe`、`bin/`、`obj/`、`.dotnet-sdk/` 等构建产物与本地 SDK，仅提交源码与资源
- 分发的 EXE 内含 Microsoft .NET 运行时（MIT 许可）；按 MIT 要求，分发时应随附其许可声明（见上表链接）
- 应用图标由 `assets/make_icon.py` 程序化绘制生成（原创，无外部素材授权问题），成品 ICO 存放于 `assets/`

## 联系方式

作者：TheSun_Also_Rises　|　微信：TheSun_Also_Rises
