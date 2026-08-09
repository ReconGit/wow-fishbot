param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'fishing-controller.txt')
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'WowFishbot\WowFishbot.csproj'
$dllPath = Join-Path $PSScriptRoot 'WowFishbot\bin\Release\net9.0\WowFishbot.dll'
$configPath = Join-Path $PSScriptRoot 'fishing-controller.json'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet_home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Write-NewLogContent {
    param(
        [string]$Path,
        [long]$Position
    )

    if (-not (Test-Path -LiteralPath $Path)) { return $Position }
    $length = (Get-Item -LiteralPath $Path).Length
    if ($length -lt $Position) { $Position = 0L }
    $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    $stream = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
    try {
        $null = $stream.Seek($Position, 'Begin')
        $reader = [System.IO.StreamReader]::new($stream)
        try {
            $text = $reader.ReadToEnd()
            $nextPosition = $stream.Position
            if ($text) { Write-Host -NoNewline $text }
        }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    return $nextPosition
}

$configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (-not (Get-Process -Name $configuration.ProcessName -ErrorAction SilentlyContinue)) {
    throw "Game client process '$($configuration.ProcessName)' is not running."
}

dotnet build $projectPath -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source
$fileLoggingEnabled = $configuration.EnableFileLogging -eq $true
$arguments = if ($fileLoggingEnabled) {
    '"{0}" --debug-privilege --parent-pid {1} --output "{2}"' -f $dllPath, $PID, $OutputPath
}
else {
    '"{0}" --debug-privilege --parent-pid {1}' -f $dllPath, $PID
}
$controller = Start-Process -FilePath $dotnetExe -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru

Write-Host 'Approve UAC. Focus the game client. Press the configured start key.'
try {
    if (-not $fileLoggingEnabled) {
        Write-Host 'File logging is disabled.'
        while (-not $controller.HasExited) { Start-Sleep -Milliseconds 250 }
    }
    else {
        while (-not (Test-Path -LiteralPath $OutputPath)) {
            if ($controller.HasExited) { throw "Controller exited with code $($controller.ExitCode)." }
            Start-Sleep -Milliseconds 100
        }

        $position = 0L
        while (-not $controller.HasExited) {
            $position = Write-NewLogContent -Path $OutputPath -Position $position
            Start-Sleep -Milliseconds 100
        }
        $position = Write-NewLogContent -Path $OutputPath -Position $position
    }
    $controller.Refresh()
    if ($controller.ExitCode -ne 0) {
        throw "Controller exited with code $($controller.ExitCode)."
    }
}
finally {
    if (-not $controller.HasExited) {
        try { Stop-Process -Id $controller.Id -ErrorAction Stop }
        catch { Write-Warning 'Could not stop the elevated controller; press F8 in-game.' }
    }
}
