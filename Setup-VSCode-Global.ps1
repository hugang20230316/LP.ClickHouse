# VS Code 全局配置自动设置脚本

Write-Host "VS Code 全局权限配置脚本" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Green

# VS Code 用户设置路径
$settingsPath = "$env:APPDATA\Code\User\settings.json"
$backupPath = "$env:APPDATA\Code\User\settings.json.backup"

Write-Host "设置文件路径: $settingsPath" -ForegroundColor Yellow

# 检查 VS Code 是否安装
if (!(Test-Path "$env:APPDATA\Code")) {
    Write-Host "错误: 未找到 VS Code 用户数据目录。请确保 VS Code 已安装。" -ForegroundColor Red
    exit 1
}

# 备份现有设置
if (Test-Path $settingsPath) {
    Write-Host "备份现有设置..." -ForegroundColor Cyan
    Copy-Item $settingsPath $backupPath -Force
    Write-Host "备份已保存到: $backupPath" -ForegroundColor Green
}

# 全局配置内容
$globalSettings = @'
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
'@

# 检查现有设置并合并
if (Test-Path $settingsPath) {
    Write-Host "检测到现有设置，正在合并..." -ForegroundColor Cyan
    try {
        $existingSettings = Get-Content $settingsPath -Raw | ConvertFrom-Json

        # 解析新设置
        $newSettings = $globalSettings | ConvertFrom-Json

        # 合并设置（新设置优先）
        $mergedSettings = $existingSettings
        foreach ($property in $newSettings.PSObject.Properties) {
            $mergedSettings | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
        }

        # 转换回 JSON
        $finalSettings = $mergedSettings | ConvertTo-Json -Depth 10
    }
    catch {
        Write-Host "警告: 无法解析现有设置，将使用新设置覆盖。" -ForegroundColor Yellow
        $finalSettings = $globalSettings
    }
}
else {
    Write-Host "创建新的设置文件..." -ForegroundColor Cyan
    $finalSettings = $globalSettings
}

# 写入设置文件
try {
    $finalSettings | Out-File $settingsPath -Encoding UTF8 -Force
    Write-Host "全局设置已成功应用！" -ForegroundColor Green
    Write-Host "" -ForegroundColor White
    Write-Host "配置内容包括:" -ForegroundColor Cyan
    Write-Host "- 工作区信任设置（不再提示信任）" -ForegroundColor White
    Write-Host "- 任务自动执行权限" -ForegroundColor White
    Write-Host "- 调试权限设置" -ForegroundColor White
    Write-Host "- 终端集成优化" -ForegroundColor White
    Write-Host "- C# 开发环境优化" -ForegroundColor White
    Write-Host "- Docker 和测试工具配置" -ForegroundColor White
    Write-Host "" -ForegroundColor White
    Write-Host "请重启 VS Code 以使设置生效。" -ForegroundColor Yellow
    Write-Host "" -ForegroundColor White
    Write-Host "如需恢复备份，请运行:" -ForegroundColor Cyan
    Write-Host "Copy-Item '$backupPath' '$settingsPath'" -ForegroundColor White
}
catch {
    Write-Host "错误: 无法写入设置文件。$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "" -ForegroundColor White
Write-Host "脚本执行完成。" -ForegroundColor Green