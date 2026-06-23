# ---------------------------------------------------------------------------
# scripts/uecada.ps1 — UECADA one-stop CLI.
#
# Components:
#   infra          : root docker compose (mysql, timescaledb, commanddb, ai-api, backend)
#   das            : total_das/DAS              (das-mosquitto, das-node-red, das-simulator)
#   equip-sim      : total_das/equip-sim        (nodered-line01/02/03)
#   command-center : total_das/equip-sim        (command-center node-red + OPC UA server)
#   xdas           : total_das/X_DAS            (x-das-node-red)
#   backend-api    : unreal-backend/BeApi       (dotnet run, Windows local — NOT docker)
#   frontend       : frontend/UECADA_3          (npm run dev, Windows local — NOT docker)
#
# start order (all):
#   das → infra → equip-sim → command-center → xdas → backend-api → frontend
# (das 가 먼저 떠야 das_das-internal 을 만들어 infra 가 attach 가능)
#
# backend-api / frontend 는 Windows 로컬 프로세스이므로 Start-Process 로
# 백그라운드 기동하고 (terminal 을 닫아도 살아있음), PID 는
# ~/.uecada/state.json 에 저장한다. stdout/stderr 는 logs/<name>.log /
# logs/<name>.err.log 파일로 리다이렉트.
# stop  : taskkill /T /F 로 트리 종료 (npm 의 node 자식까지)
# logs  : Get-Content -Wait 로 파일 tail (Ctrl+C 로 detach)
# ---------------------------------------------------------------------------

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("start", "stop", "restart", "status", "logs", "help", "-h", "--help")]
    [string]$Action = "help",

    [Parameter(Position = 1)]
    [string]$Component = ""
)

$ErrorActionPreference = "Stop"

# Force UTF-8 console
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

# ---------- paths ----------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir

$Paths = @{
    "infra"          = $RepoRoot
    "das"            = (Join-Path $RepoRoot "total_das\DAS")
    "equip-sim"      = (Join-Path $RepoRoot "total_das\equip-sim")
    "command-center" = (Join-Path $RepoRoot "total_das\equip-sim")
    "xdas"           = (Join-Path $RepoRoot "total_das\X_DAS")
    "backend-api"    = (Join-Path $RepoRoot "unreal-backend\BeApi")
    "frontend"       = (Join-Path $RepoRoot "frontend\UECADA_3")
}

# default start order
#
# 의존성 그래프 (외부 네트워크 관점):
#   total-das-net    : external — Ensure-ExternalNetwork 가 미리 만들어줘야 함
#   factory-net      : external — Ensure-ExternalNetwork 가 미리 만들어줘야 함
#   das_das-internal : das compose 가 첫 기동 시 만들어준다 (infra 의 backend 가 참조)
#
# 따라서 das 가 infra 보다 먼저 떠야 한다.
#   das         → das_das-internal 생성
#   infra       → das_das-internal + total-das-net 참조
#   equip-sim   → factory-net 참조
#   command-center → factory-net 참조
#   xdas        → total-das-net + factory-net 참조
$StartOrder = @("das", "infra", "equip-sim", "command-center", "xdas", "backend-api", "frontend")
$StopOrder  = @("frontend", "backend-api", "xdas", "command-center", "equip-sim", "infra", "das")

# components which use docker compose
$DockerComponents = @("infra", "das", "equip-sim", "command-center", "xdas")

# components which run as Windows native processes (Start-Process, NOT docker, NOT Start-Job)
$JobComponents = @("backend-api", "frontend")

# ---------- pid store ----------
#
# uecada 는 backend-api / frontend 를 PowerShell Start-Job 이 아니라
# Start-Process 로 띄운다. 이유:
#   - uecada.cmd 는 powershell.exe -File ... 를 매번 새로 띄우므로,
#     Start-Job 으로 만든 Job 은 그 PowerShell 프로세스가 끝날 때 같이 소멸된다.
#   - Start-Process 는 부모와 무관한 새 OS 프로세스라 터미널을 닫아도 살아있다.
#
# state file: ~/.uecada/state.json
#   {
#     "backend-api": { "pid": 1234, "started_at": "...", "log": "..." },
#     "frontend":    { "pid": 5678, "started_at": "...", "log": "..." }
#   }
$StateDir  = Join-Path $HOME ".uecada"
$StateFile = Join-Path $StateDir "state.json"
$LogDir    = Join-Path $RepoRoot "logs"

function Ensure-StateDir {
    if (-not (Test-Path $StateDir)) { New-Item -ItemType Directory -Path $StateDir | Out-Null }
}
function Ensure-LogDir {
    if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir | Out-Null }
}

function Read-State {
    Ensure-StateDir
    if (-not (Test-Path $StateFile)) { return @{} }
    try {
        $raw = Get-Content $StateFile -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) { return @{} }
        $obj = $raw | ConvertFrom-Json -ErrorAction Stop
        $h = @{}
        foreach ($p in $obj.PSObject.Properties) {
            $entry = @{}
            foreach ($pp in $p.Value.PSObject.Properties) {
                $entry[$pp.Name] = $pp.Value
            }
            $h[$p.Name] = $entry
        }
        return $h
    } catch {
        Write-Warning "state.json 파싱 실패 — 빈 값으로 reset 합니다. ($_)"
        return @{}
    }
}

function Write-State {
    param([hashtable]$Map)
    Ensure-StateDir
    ($Map | ConvertTo-Json -Depth 4) | Set-Content -Path $StateFile -Encoding UTF8
}

function Set-ProcessEntry {
    param([string]$Name, [int]$ProcessId, [string]$LogPath)
    $m = Read-State
    $m[$Name] = @{
        pid        = $ProcessId
        started_at = (Get-Date).ToString("o")
        log        = $LogPath
    }
    Write-State -Map $m
}

function Get-ProcessEntry {
    param([string]$Name)
    $m = Read-State
    if ($m.ContainsKey($Name)) { return $m[$Name] }
    return $null
}

function Remove-ProcessEntry {
    param([string]$Name)
    $m = Read-State
    if ($m.ContainsKey($Name)) {
        $m.Remove($Name) | Out-Null
        Write-State -Map $m
    }
}

function Test-PidAlive {
    param([int]$ProcessId)
    if ($ProcessId -le 0) { return $false }
    $p = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    return ($null -ne $p)
}

# ---------- docker helpers ----------

function Ensure-ExternalNetwork {
    param([Parameter(Mandatory = $true)][string]$Name)

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    docker network inspect $Name *> $null
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev

    if ($code -ne 0) {
        Write-Host "==> creating docker network: $Name"
        docker network create $Name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "failed to create docker network '$Name' (is Docker Desktop running?)" }
    }
}

function Invoke-DockerCompose {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Component,

        # NOTE: PowerShell 의 자동변수 $Args 와 이름이 겹치면 함수 파라미터로 들어온
        # 배열이 splatting 시 빈 인자로 전개되어 docker 가 help 만 출력하는 버그가 있다.
        # 그래서 명시적으로 $ComposeArgs 라는 이름을 쓴다.
        [Parameter(Mandatory = $true)]
        [string[]]$ComposeArgs
    )
    $dir = $Paths[$Component]
    if (-not (Test-Path $dir)) {
        throw "[$Component] directory not found: $dir"
    }

    Push-Location $dir
    try {
        if ($Component -eq "command-center") {
            # command-center 는 전용 env / compose 파일을 쓰는 wrapper 스크립트로 위임
            $cmdMap = @{
                "up"     = "up"
                "down"   = "down"
                "ps"     = "ps"
                "logs"   = "logs"
            }
            $sub = $ComposeArgs[0]
            if (-not $cmdMap.ContainsKey($sub)) {
                throw "[command-center] unsupported docker action: $sub"
            }
            & .\scripts\up-command-center.ps1 $cmdMap[$sub]
        } else {
            & docker compose @ComposeArgs
        }
        if ($LASTEXITCODE -ne 0) {
            throw "[$Component] docker compose $($ComposeArgs -join ' ') failed (exit $LASTEXITCODE)"
        }
    } finally {
        Pop-Location
    }
}

# ---------- start/stop per component ----------

function Start-Component {
    param([string]$Name)

    if (-not $Paths.ContainsKey($Name)) {
        throw "Unknown component: $Name"
    }

    Write-Host ""
    Write-Host "==> [$Name] start"

    switch ($Name) {
        "infra"          { Invoke-DockerCompose -Component $Name -ComposeArgs @("up","-d") }
        "das"            { Invoke-DockerCompose -Component $Name -ComposeArgs @("up","-d","--build") }
        "equip-sim"      {
            Push-Location $Paths[$Name]
            try {
                & .\scripts\up-all.ps1 up
                if ($LASTEXITCODE -ne 0) { throw "[equip-sim] up-all.ps1 failed" }
            } finally { Pop-Location }
        }
        "command-center" { Invoke-DockerCompose -Component $Name -ComposeArgs @("up") }
        "xdas"           { Invoke-DockerCompose -Component $Name -ComposeArgs @("up","-d","--build") }
        "backend-api"    { Start-BackendApiJob }
        "frontend"       { Start-FrontendJob }
    }
}

function Stop-Component {
    param([string]$Name)

    if (-not $Paths.ContainsKey($Name)) {
        throw "Unknown component: $Name"
    }

    Write-Host ""
    Write-Host "==> [$Name] stop"

    switch ($Name) {
        "infra"          { Invoke-DockerCompose -Component $Name -ComposeArgs @("down") }
        "das"            { Invoke-DockerCompose -Component $Name -ComposeArgs @("down") }
        "equip-sim"      {
            Push-Location $Paths[$Name]
            try {
                & .\scripts\up-all.ps1 down
            } finally { Pop-Location }
        }
        "command-center" { Invoke-DockerCompose -Component $Name -ComposeArgs @("down") }
        "xdas"           { Invoke-DockerCompose -Component $Name -ComposeArgs @("down") }
        "backend-api"    { Stop-LocalJob -Name "backend-api" }
        "frontend"       { Stop-LocalJob -Name "frontend" }
    }
}

# ---------- backend-api / frontend (Start-Process 기반) ----------

function Start-LocalProcess {
    <#
      공통 기동 헬퍼.
        - $WorkDir 에서
        - $FileName ($Arguments 와 함께) 를 WindowStyle Hidden 으로 띄우고
        - stdout/stderr 를 $LogPath / $LogPath.err.log 로 리다이렉트
        - PID 를 ~/.uecada/state.json 에 저장
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$WorkDir,
        [Parameter(Mandatory)][string]$FileName,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$LogPath
    )

    $existing = Get-ProcessEntry -Name $Name
    if ($null -ne $existing -and (Test-PidAlive -ProcessId ([int]$existing.pid))) {
        Write-Host "  $Name already running (PID $($existing.pid))"
        return
    }
    if ($null -ne $existing) { Remove-ProcessEntry -Name $Name }

    if (-not (Test-Path $WorkDir)) {
        throw "[$Name] working dir not found: $WorkDir"
    }

    Ensure-LogDir
    # 기존 로그는 .1 로 아카이브 (재기동 더 해도 .1 만 유지됨)
    if (Test-Path $LogPath) {
        Move-Item -Force -Path $LogPath -Destination "$LogPath.1" -ErrorAction SilentlyContinue
    }
    New-Item -ItemType File -Path $LogPath -Force | Out-Null

    # stderr 는 별도 .err.log 로 (Start-Process 는 stdout/stderr 같은 파일을 금지)
    $errLogPath = $LogPath -replace '\.log$', '.err.log'
    if (Test-Path $errLogPath) {
        Move-Item -Force -Path $errLogPath -Destination "$errLogPath.1" -ErrorAction SilentlyContinue
    }
    New-Item -ItemType File -Path $errLogPath -Force | Out-Null

    # 자식 프로세스가 UTF-8 로 출력하도록 환경 변수를 현재 세션에 추가한다.
    # Start-Process 는 현 세션의 $env: 를 그대로 자식에게 상속시킨다.
    # 일시적으로 설정한 값은 명시 해제하지 않음 — 어차피 이 세션은 cmd 안에서 뜨고
    # 몇 초 뒤 끊어진다.
    $env:DOTNET_SYSTEM_CONSOLE_ALLOWANSICOLORWHENREDIRECTED = "1"
    $env:PYTHONIOENCODING                                     = "utf-8"
    # .NET 은 OutputEncoding 을 기본적으로 조절하지만, 콘솔이 RedirectStandardOutput 로
    # 닫혀 있으면 일부 로컬에서 cp949 로 떨어지는 일이 있다.
    # appsettings 나 Program.cs 에 손대지 않으려면 읽는 쪽에서 -Encoding UTF8 하는 게
    # 가장 안전한 해법이라, logs follow 에서는 그렇게 처리한다 (Show-Logs 참고).

    $spArgs = @{
        FilePath               = $FileName
        ArgumentList           = $Arguments
        WorkingDirectory       = $WorkDir
        WindowStyle            = "Hidden"
        RedirectStandardOutput = $LogPath
        RedirectStandardError  = $errLogPath
        PassThru               = $true
    }
    $proc = Start-Process @spArgs
    if ($null -eq $proc) { throw "[$Name] Start-Process returned null" }

    Set-ProcessEntry -Name $Name -ProcessId $proc.Id -LogPath $LogPath
    Write-Host "  started $Name (PID $($proc.Id))"
    Write-Host "  logs: $LogPath"
}

function Start-BackendApiJob {
    Start-LocalProcess `
        -Name "backend-api" `
        -WorkDir $Paths["backend-api"] `
        -FileName "dotnet" `
        -Arguments @("run") `
        -LogPath (Join-Path $LogDir "backend-api.log")
    Write-Host "  BeApi:   http://localhost:5082"
    Write-Host "  logs:    uecada logs backend-api"
}

function Start-FrontendJob {
    # npm 은 Windows 에서 npm.cmd 이다. Start-Process FilePath 에 .cmd 를 직접
    # 넘기면 일부 PowerShell 버전에서 자식만 띄우고 부모 cmd 가 바로 죽어버려서
    # tree kill 이 동작 안한다. 그래서 cmd /c 로 명시적으로 감싼다.
    Start-LocalProcess `
        -Name "frontend" `
        -WorkDir $Paths["frontend"] `
        -FileName "cmd.exe" `
        -Arguments @("/c", "npm", "run", "dev") `
        -LogPath (Join-Path $LogDir "frontend.log")
    Write-Host "  Vite:    http://127.0.0.1:5173"
    Write-Host "  logs:    uecada logs frontend"
}

function Stop-LocalJob {
    param([string]$Name)

    $entry = Get-ProcessEntry -Name $Name
    if ($null -eq $entry) {
        Write-Host "  [$Name] no recorded process — nothing to stop"
        return
    }

    $procId = [int]$entry.pid
    if (-not (Test-PidAlive -ProcessId $procId)) {
        Write-Host "  [$Name] PID $procId no longer alive — clearing record"
        Remove-ProcessEntry -Name $Name
        return
    }

    Write-Host "  stopping [$Name] PID $procId (tree kill)"
    # taskkill /T /F 는 자식 프로세스도 함께 종료 — npm 의 경우 node 손자와
    # cmd 부모를 모두 정리하려면 필수.
    & taskkill /PID $procId /T /F 2>$null | Out-Null
    Remove-ProcessEntry -Name $Name
}

# ---------- status ----------

function Get-ComposePsLines {
    # docker compose ps 의 stderr 를 흡수하고 stdout 만 줄 배열로 돌려준다.
    # PowerShell 의 ErrorActionPreference=Stop 환경에서 docker 가 stderr 로
    # 경고(예: 'The "LINE_ID" variable is not set...') 만 찍어도 NativeCommandError
    # 를 던지는 문제를 회피하기 위해 게이트로 둘러쌜다.
    param([string[]]$ComposeArgs)

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = & docker compose @ComposeArgs ps --format "{{.Service}}|{{.State}}" 2>$null
    } finally {
        $ErrorActionPreference = $prev
    }
    if ($null -eq $raw) { return @() }
    return ($raw -split "`r?`n") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Write-StatusLines {
    param([string]$Name, [string[]]$Lines, [string]$Suffix = "")

    if ($null -eq $Lines -or $Lines.Count -eq 0) {
        $label = if ([string]::IsNullOrEmpty($Suffix)) { $Name } else { "$Name ($Suffix)" }
        Write-Host ("  {0,-22} : (no containers)" -f $label)
        return
    }
    $label = if ([string]::IsNullOrEmpty($Suffix)) { $Name } else { "$Name ($Suffix)" }
    Write-Host ("  {0,-22} :" -f $label)
    foreach ($l in $Lines) {
        $parts = $l -split '\|', 2
        $svc   = if ($parts.Count -ge 1) { $parts[0] } else { $l }
        $state = if ($parts.Count -ge 2) { $parts[1] } else { "" }
        Write-Host ("      - {0,-22} {1}" -f $svc, $state)
    }
}

function Show-Status {
    Write-Host ""
    Write-Host "=== UECADA status ==="

    foreach ($name in $StartOrder) {
        try {
            if ($DockerComponents -contains $name) {
                $dir = $Paths[$name]
                if (-not (Test-Path $dir)) {
                    Write-Host ("  {0,-22} : (not found: $dir)" -f $name)
                    continue
                }
                Push-Location $dir
                try {
                    switch ($name) {
                        "command-center" {
                            $lines = Get-ComposePsLines -ComposeArgs @("--env-file",".env.command-center","-f","docker-compose.command-center.yml")
                            Write-StatusLines -Name $name -Lines $lines
                        }
                        "equip-sim" {
                            # equip-sim 은 .env.line01/02/03 으로 3번 등록되어 있다.
                            foreach ($env in @(".env.line01", ".env.line02", ".env.line03")) {
                                $lines = Get-ComposePsLines -ComposeArgs @("--env-file", $env)
                                $tag = $env -replace '^\.env\.', ''
                                Write-StatusLines -Name $name -Lines $lines -Suffix $tag
                            }
                        }
                        default {
                            $lines = Get-ComposePsLines -ComposeArgs @()
                            Write-StatusLines -Name $name -Lines $lines
                        }
                    }
                } finally {
                    Pop-Location
                }
            } elseif ($JobComponents -contains $name) {
                $entry = Get-ProcessEntry -Name $name
                if ($null -eq $entry) {
                    Write-Host ("  {0,-22} : (not started)" -f $name)
                } else {
                    $procId = [int]$entry.pid
                    if (Test-PidAlive -ProcessId $procId) {
                        Write-Host ("  {0,-22} : PID $procId (running)" -f $name)
                    } else {
                        Write-Host ("  {0,-22} : PID $procId (dead) — 'uecada stop $name' 이나 재기동 시 기록 정리됨" -f $name)
                    }
                }
            }
        } catch {
            Write-Warning "[$name] status failed: $_"
        }
    }
    Write-Host ""
}

# ---------- logs ----------

function Invoke-FileTail {
    <#
    .SYNOPSIS
        Tail a file (UTF-8) and stream new content until the user presses 'q' or ESC.

    .DESCRIPTION
        Mimics `tail -f` with two differences:
        - UTF-8 decoding is forced (한글 깨짐 방지).
        - Press 'q' or ESC to stop following — the underlying process keeps running.
          Ctrl+C still works, but cmd.exe asks "Terminate batch job?" which is noisy.
    #>
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$TailLines = 50,
        [int]$PollMs    = 200
    )

    # Step 1. 초기 tail 출력
    if (Test-Path $Path) {
        try {
            Get-Content -Path $Path -Tail $TailLines -Encoding UTF8 | ForEach-Object {
                Write-Host $_
            }
        } catch {
            Write-Warning "initial tail failed: $_"
        }
    }

    # Step 2. FileStream + StreamReader 로 끝부터 follow
    $fs     = $null
    $reader = $null
    try {
        $fs = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite
        )
        $reader = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
        [void]$fs.Seek(0, [System.IO.SeekOrigin]::End)

        while ($true) {
            # 새 내용 읽기
            $chunk = $reader.ReadToEnd()
            if (-not [string]::IsNullOrEmpty($chunk)) {
                # 줄바꿈 유지 위해 Write-Host -NoNewline
                Write-Host -NoNewline $chunk
            }

            # 키 입력 감지 — q 또는 ESC 이면 종료
            if ($Host.UI.RawUI.KeyAvailable) {
                $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
                # VirtualKeyCode 27 = ESC, Character 'q'/'Q'
                if ($key.VirtualKeyCode -eq 27 -or $key.Character -eq 'q' -or $key.Character -eq 'Q') {
                    Write-Host ""
                    Write-Host "==> follow 종료 (프로세스는 계속 실행 중)"
                    break
                }
            }

            Start-Sleep -Milliseconds $PollMs
        }
    } catch {
        Write-Warning "file tail aborted: $_"
    } finally {
        if ($null -ne $reader) { $reader.Dispose() }
        if ($null -ne $fs)     { $fs.Dispose() }
    }
}

function Show-Logs {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "Usage: uecada logs <component>"
    }
    if (-not $Paths.ContainsKey($Name)) {
        throw "Unknown component: $Name"
    }

    if ($DockerComponents -contains $Name) {
        if ($Name -eq "command-center") {
            Invoke-DockerCompose -Component $Name -ComposeArgs @("logs")
        } else {
            Push-Location $Paths[$Name]
            try {
                & docker compose logs -f
            } finally {
                Pop-Location
            }
        }
    } elseif ($JobComponents -contains $Name) {
        $entry = Get-ProcessEntry -Name $Name
        if ($null -eq $entry) {
            throw "[$Name] not started — run: uecada start $Name"
        }
        $procId  = [int]$entry.pid
        $logFile = [string]$entry.log
        if (-not (Test-Path $logFile)) {
            throw "[$Name] log file not found: $logFile"
        }
        $alive = Test-PidAlive -ProcessId $procId
        $tag   = if ($alive) { "running" } else { "DEAD" }
        Write-Host "==> follow [$Name] PID $procId ($tag)"
        Write-Host "    q / ESC : follow 종료 (대상 프로세스는 그대로 살아있음)"
        Write-Host "    stdout  : $logFile"
        $errLog = $logFile -replace '\.log$', '.err.log'
        if (Test-Path $errLog) { Write-Host "    stderr  : $errLog" }
        Write-Host ("-" * 70)

        Invoke-FileTail -Path $logFile -TailLines 50
    }
}

# ---------- help ----------

function Show-Help {
    @"
uecada — one-stop CLI for the UECADA stack

USAGE
  uecada <action> [<component>]

ACTIONS
  start [<component>]     start all (default) or a single component
  stop  [<component>]     stop  all (default) or a single component
  restart [<component>]   stop + start
  status                  show docker compose ps + Job states
  logs <component>        follow logs (docker logs -f / Get-Content -Wait)
  help                    show this help

COMPONENTS  (start order)
  infra            root docker compose (mysql, timescaledb, commanddb, ai-api, backend)
  das              total_das/DAS               (das-mosquitto, das-node-red, das-simulator)
  equip-sim        total_das/equip-sim         (nodered-line01/02/03)
  command-center   total_das/equip-sim         (command-center node-red + OPC UA server)
  xdas             total_das/X_DAS             (x-das-node-red)
  backend-api      unreal-backend/BeApi        (dotnet run on Windows — NOT docker)
  frontend         frontend/UECADA_3           (npm run dev on Windows — NOT docker)

EXAMPLES
  uecada start                    # start everything
  uecada start backend-api        # start only BeApi (dotnet run as background process)
  uecada logs frontend            # follow Vite logs
  uecada status                   # full status board
  uecada stop                     # stop everything
"@ | Write-Host
}

# ---------- main ----------

function Resolve-Targets {
    param([string]$Component, [string[]]$DefaultOrder)
    if ([string]::IsNullOrWhiteSpace($Component)) {
        return $DefaultOrder
    }
    if (-not $Paths.ContainsKey($Component)) {
        throw "Unknown component: $Component  (use: uecada help)"
    }
    return @($Component)
}

switch ($Action) {
    "help" { Show-Help; exit 0 }
    "-h"   { Show-Help; exit 0 }
    "--help" { Show-Help; exit 0 }

    "start" {
        $targets = Resolve-Targets -Component $Component -DefaultOrder $StartOrder

        # 전체 기동(=Component 미지정) 일 때만, 외부 네트워크 두 개를 미리 보장한다.
        # 단독 컴포넌트 기동 시에는 해당 컴포넌트가 직접 의존하는 네트워크 외에는 만들지 않는다.
        if ([string]::IsNullOrWhiteSpace($Component)) {
            Ensure-ExternalNetwork -Name "total-das-net"
            Ensure-ExternalNetwork -Name "factory-net"
        }

        foreach ($t in $targets) { Start-Component -Name $t }
        Write-Host ""
        Write-Host "==> done. Try: uecada status"
    }
    "stop" {
        $targets = Resolve-Targets -Component $Component -DefaultOrder $StopOrder
        foreach ($t in $targets) {
            try { Stop-Component -Name $t } catch { Write-Warning "[$t] stop failed: $_" }
        }
        Write-Host ""
        Write-Host "==> done."
    }
    "restart" {
        $stopTargets  = Resolve-Targets -Component $Component -DefaultOrder $StopOrder
        $startTargets = Resolve-Targets -Component $Component -DefaultOrder $StartOrder
        foreach ($t in $stopTargets) {
            try { Stop-Component -Name $t } catch { Write-Warning "[$t] stop failed: $_" }
        }
        foreach ($t in $startTargets) { Start-Component -Name $t }
    }
    "status" { Show-Status }
    "logs"   { Show-Logs -Name $Component }
}
