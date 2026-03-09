# VS Code 全局权限配置指南

## 概述

为了减少 VS Code 中的权限提示，需要在全局设置中配置相关选项。本指南提供完整的配置步骤。

## 配置步骤

### 1. 打开用户设置

- 方法1: `Ctrl+Shift+P` → "Preferences: Open User Settings (JSON)"
- 方法2: `Ctrl+,` 打开设置 → 点击右上角的 `{}` 图标

### 2. 添加全局设置

在 `settings.json` 中添加以下配置：

```json
{
    // ========== 安全和信任设置 ==========
    "security.workspace.trust.enabled": true,
    "security.workspace.trust.banner": "never",
    "security.workspace.trust.startupPrompt": "never",

    // ========== 任务和终端设置 ==========
    "task.allowAutomaticTasks": "on",
    "terminal.integrated.shellIntegration.enabled": true,
    "terminal.integrated.enablePersistentSessions": true,

    // ========== 调试设置 ==========
    "debug.allowBreakpointsEverywhere": true,
    "debug.openDebug": "neverOpen",
    "debug.showBreakpointsInOverviewRuler": true,

    // ========== 文件和编辑器设置 ==========
    "files.trimTrailingWhitespace": true,
    "files.insertFinalNewline": true,
    "editor.formatOnSave": true,
    "editor.codeActionsOnSave": {
        "source.organizeImports": "explicit",
        "source.fixAll": "explicit"
    },

    // ========== Git 设置 ==========
    "git.autofetch": true,
    "git.enableSmartCommit": true,
    "git.confirmSync": false,

    // ========== 扩展设置 ==========
    "extensions.ignoreRecommendations": false,
    "extensions.showRecommendationsOnlyOnDemand": false,

    // ========== 网络和代理设置 ==========
    "http.proxyStrictSSL": false,
    "http.systemCertificates": true,

    // ========== 遥测和隐私设置 ==========
    "telemetry.telemetryLevel": "off",

    // ========== C# 特定设置 ==========
    "csharp.suppressDotnetInstallWarning": true,
    "csharp.suppressDotnetRestoreNotification": true,
    "csharp.suppressBuildAssetsNotification": true,
    "csharp.suppressProjectJsonWarning": true,
    "csharp.suppressHiddenDiagnostics": false,

    // ========== Docker 设置 ==========
    "docker.showStartPage": false,
    "docker.containers.groupBy": "Compose Project Name",

    // ========== 测试设置 ==========
    "dotnet-test-explorer.testProjectPath": "**/*Tests.csproj",

    // ========== 搜索和文件排除 ==========
    "search.exclude": {
        "**/node_modules": true,
        "**/bin": true,
        "**/obj": true,
        "**/.git": true,
        "**/.vs": true,
        "**/packages": true,
        "**/TestResults": true,
        "**/coverage": true,
        "**/dist": true,
        "**/build": true
    },
    "files.exclude": {
        "**/bin": true,
        "**/obj": true,
        "**/.git": true,
        "**/.vs": true,
        "**/packages": true,
        "**/TestResults": true,
        "**/coverage": true,
        "**/dist": true,
        "**/build": true
    },

    // ========== 工作区设置 ==========
    "window.openFoldersInNewWindow": "on",

    // ========== 其他优化 ==========
    "workbench.editor.enablePreview": false,
    "workbench.editor.showTabs": "multiple",
    "workbench.startupEditor": "none",
    "workbench.activityBar.visible": true,
    "workbench.statusBar.visible": true
}
```

### 3. 保存设置

保存 `settings.json` 文件后，重启 VS Code。

## 推荐安装的扩展

在扩展市场安装以下扩展（全局推荐）：

### 必装扩展
- **C#** (ms-dotnettools.csharp) - C# 语言支持
- **C# Dev Kit** (ms-dotnettools.csdevkit) - C# 开发工具包
- **.NET Install Tool** (ms-dotnettools.vscode-dotnet-runtime) - .NET 运行时安装工具

### 开发效率扩展
- **PowerShell** (ms-vscode.powershell) - PowerShell 支持
- **Docker** (ms-azuretools.vscode-docker) - Docker 支持
- **Remote Containers** (ms-vscode-remote.remote-containers) - 容器开发
- **GitLens** (eamodio.gitlens) - Git 增强功能

### 测试和调试扩展
- **.NET Core Test Explorer** (formulahendry.dotnet-test-explorer) - .NET 测试浏览器
- **Test Adapter Converter** (ms-vscode.test-adapter-converter) - 测试适配器转换器

### 代码质量扩展
- **ESLint** (dbaeumer.vscode-eslint) - JavaScript/TypeScript 代码检查
- **Prettier** (esbenp.prettier-vscode) - 代码格式化
- **Code Spell Checker** (streetsidesoftware.code-spell-checker) - 拼写检查

## 验证配置

配置完成后，验证以下功能是否正常：

1. **信任提示**: 打开新项目时不应再出现信任提示
2. **任务执行**: 可以直接运行任务而不需额外确认
3. **终端集成**: 终端命令执行顺畅
4. **调试**: 可以直接启动调试会话
5. **扩展**: 推荐扩展自动提示安装

## 故障排除

### 如果仍有权限提示

1. **检查设置**: 确认所有设置都已正确添加
2. **重启 VS Code**: 完全关闭并重新打开
3. **检查扩展**: 确保相关扩展已安装并启用
4. **清除缓存**: `Ctrl+Shift+P` → "Developer: Reload Window"

### 特定问题

- **终端权限**: 检查 PowerShell 执行策略
- **网络权限**: 检查防火墙和代理设置
- **文件权限**: 检查文件夹权限设置

## 备份和恢复

建议定期备份 `settings.json` 文件：

```powershell
# 备份设置
Copy-Item $env:APPDATA\Code\User\settings.json $env:APPDATA\Code\User\settings.json.backup

# 恢复设置
Copy-Item $env:APPDATA\Code\User\settings.json.backup $env:APPDATA\Code\User\settings.json
```

## 更新说明

VS Code 更新时，某些设置可能需要重新配置。建议：
- 关注 VS Code 更新日志
- 定期检查设置是否仍然有效
- 考虑使用 Settings Sync 同步设置到多台设备