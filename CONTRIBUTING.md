# 贡献指南（CONTRIBUTING）

感谢你关注 **准点 · 定时关机管家**。这是一款 Windows 平台绿色单文件定时关机工具，支持多种定时模式与电源动作。

## 开发环境

- 操作系统：Windows 10 / 11 x64
- SDK：.NET 8 SDK（8.0.x，验证环境 8.0.428）
- 语言：C# 12 / WinForms（`net8.0-windows`）
- 图标重新生成（可选）：Python 3.10+ 与 Pillow

## 本地搭建

```bash
# 克隆仓库后进入 src 上级目录
cd 准点定时关机管家

# 还原依赖
dotnet restore src/PowerOffManager.csproj

# 编译
dotnet build src/PowerOffManager.csproj -c Release
```

## 本地构建

```bash
# 产出 dist/PowerOffManager.exe（自包含单文件，约 68 MB）
dotnet publish src/PowerOffManager.csproj -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None \
  -o dist
```

> 打包产物（`dist/`、EXE、`bin/`、`obj/`）不入库，请通过上述命令本地生成。

## 运行与验证

```bash
# 从源码运行
dotnet run --project src/PowerOffManager.csproj -c Release

# 验证单文件启动：手动启动显示主界面；带 --autostart 仅托盘运行
dist\PowerOffManager.exe
dist\PowerOffManager.exe --autostart
```

> 功能冒烟请注意：电源动作会真实影响系统，测试时优先用"锁屏 / 关显示器"等可恢复动作，避免误关机。

## 提交规范

- 保持"绿色单文件、本地离线、不写注册表"的核心承诺，任何改动不应破坏
- 涉及功能变更请同步更新 README 与 NOTICE；新增模式/动作请同步更新界面与配置兼容逻辑
- 关机执行相关改动务必保持：后台线程执行、提权校验、强制标志、幂等闸门，避免界面假死或静默失败
- 提交前请跑通"本地构建"与"运行与验证"，确认 0 编译错误
- 新增能力（如新定时模式）请先开 Issue 讨论再提 PR

## 问题反馈与 PR

- 提交 Issue 时请说明系统版本、.NET 版本、复现步骤与预期行为
- PR 请聚焦单一改动，描述变更动机与验证方式
- 作者联系方式：微信 TheSun_Also_Rises
