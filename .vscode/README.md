# VS Code 配置说明

## 权限配置

本项目已配置 VS Code 设置以减少权限提示：

### 已配置的文件

- `.vscode/settings.json` - 工作区设置，包含权限和开发环境配置
- `.vscode/tasks.json` - 任务配置，提供常用命令
- `.vscode/launch.json` - 调试配置，支持 API 调试
- `.vscode/extensions.json` - 推荐扩展列表

### 主要权限设置

```json
{
    "security.workspace.trust.enabled": true,
    "security.workspace.trust.banner": "never",
    "security.workspace.trust.startupPrompt": "never",
    "task.allowAutomaticTasks": "on",
    "debug.allowBreakpointsEverywhere": true
}
```

## 使用方法

### 1. 信任工作区

打开项目后，VS Code 会提示信任工作区，点击"信任"即可。

### 2. 安装推荐扩展

VS Code 会提示安装推荐扩展，点击安装即可获得最佳开发体验。

### 3. 使用任务面板

- `Ctrl+Shift+P` 打开命令面板
- 输入 "Tasks: Run Task"
- 选择需要的任务：
  - 构建项目
  - 运行所有测试
  - 启动 ClickHouse 服务
  - 运行示例 API

### 4. 调试 API

- `F5` 或调试面板启动调试
- 选择 "启动 LP.ClickHouse.Api"
- API 将在调试模式下运行，支持断点调试

## 常用快捷键

- `Ctrl+Shift+B` - 构建项目
- `Ctrl+Shift+T` - 运行测试
- `F5` - 启动调试
- `Ctrl+Shift+P` - 命令面板

## 故障排除

如果仍有权限提示：

1. 确保已信任工作区
2. 检查 VS Code 版本（推荐最新稳定版）
3. 重启 VS Code
4. 清除 VS Code 缓存：`Ctrl+Shift+P` → "Developer: Reload Window"
