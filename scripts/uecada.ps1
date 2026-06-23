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
#   infra → das → equip-sim → command-center → xdas → backend-api → frontend
#
# backend-api / frontend 는 Windows 로컬 프로세스이므로 PowerShell Start-Job 으로
# 백그라운드 기동하고, Job-Id 는 ~/.uecada/jobs.json 에 저장한다.
# stop 시 저장된 Job-Id 로 Stop-Job + Remove-Job 한다.
# logs 시 Receive-Job -Keep -Wait 로 follow 한다.
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
$StartOrder = @("infra", "das", "equip-sim", "command-center", "xdas", "backend-api", "frontend")
$StopOrder  = @("frontend", "backend-api", "xdas", "command-center", "equip-sim", "das", "infra")

# components which use docker compose
$DockerComponents = @("infra", "das", "equip-sim", "command-center", "xdas")

# components which run as Start-Job (Windows native processes)
$JobComponents = @("backend-api", "frontend")

# ---------- job-id store ----------
$JobsDir  = Join-Path $HOME ".uecada"
$JobsFile = Join-Path $JobsDir "jobs.json"

function Ensure-JobsDir {
    if (-not (Test-Path $JobsDir)) {
        New-Item -ItemType Directory -Path $JobsDir | Out-Null
    }
}

function Read-JobsFile {
    Ensure-JobsDir
    if (-not (Test-Path $JobsFile)) {
        return @{}
    }
    try {
        $raw = Get-Content $JobsFile -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) { return @{} }
        $obj = $raw | ConvertFrom-Json -ErrorAction Stop
        # convert PSCustomObject → hashtable
        $h = @{}
        foreach ($p in $obj.PSObject.Properties) {
            $h[$p.Name] = [int]$p.Value
        }
        return $h
    } catch {
        Write-Warning "jobs.json 파싱 실패 — 빈 값으로 reset 합니다. ($_)"
        return @{}
    }
}

function Write-JobsFile {
    param([hashtable]$Map)
    Ensure-JobsDir
    ($Map | ConvertTo-Json -Compress) | Set-Content -Path $JobsFile -Encoding UTF8
}

function Set-JobId {
    param([string]$Name, [int]$JobId)
    $m = Read-JobsFile
    $m[$Name] = $JobId
    Write-JobsFile -Map $m
}

function Get-JobId {
    param([string]$Name)
    $m = Read-JobsFile
    if ($m.ContainsKey($Name)) { return [int]$m[$Name] }
    return $null
}

function Remove-JobIdEntry {
    param([string]$Name)
    $m = Read-JobsFile
    if ($m.ContainsKey($Name)) {
        $m.Remove($Name)
        Write-JobsFile -Map $m
    }
}

# ---------- docker helpers ----------

function Invoke-DockerCompose {
    param(
        [string]$Component,
        [string[]]$Args
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
            $sub = $Args[0]
            if (-not $cmdMap.ContainsKey($sub)) {
                throw "[command-center] unsupported docker action: $sub"
            }
            & .\scripts\up-command-center.ps1 $cmdMap[$sub]
        } else {
            & docker compose @Args
        }
        if ($LASTEXITCODE -ne 0) {
            throw "[$Component] docker compose $($Args -join ' ') failed (exit $LASTEXITCODE)"
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
        "infra"          { Invoke-DockerCompose -Component $Name -Args @("up","-d") }
        "das"            { Invoke-DockerCompose -Component $Name -Args @("up","-d","--build") }
        "equip-sim"      {
            Push-Location $Paths[$Name]
            try {
                & .\scripts\up-all.ps1 up
                if ($LASTEXITCODE -ne 0) { throw "[equip-sim] up-all.ps1 failed" }
            } finally { Pop-Location }
        }
        "command-center" { Invoke-DockerCompose -Component $Name -Args @("up") }
        "xdas"           { Invoke-DockerCompose -Component $Name -Args @("up","-d","--build") }
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
        "infra"          { Invoke-DockerCompose -Component $Name -Args @("down") }
        "das"            { Invoke-DockerCompose -Component $Name -Args @("down") }
        "equip-sim"      {
            Push-Location $Paths[$Name]
            try {
                & .\scripts\up-all.ps1 down
            } finally { Pop-Location }
        }
        "command-center" { Invoke-DockerCompose -Component $Name -Args @("down") }
        "xdas"           { Invoke-DockerCompose -Component $Name -Args @("down") }
        "backend-api"    { Stop-LocalJob -Name "backend-api" }
        "frontend"       { Stop-LocalJob -Name "frontend" }
    }
}

# ---------- backend-api / frontend (Start-Job) ----------

function Start-BackendApiJob {
    $existing = Get-JobId -Name "backend-api"
    if ($null -ne $existing) {
        $j = Get-Job -Id $existing -ErrorAction SilentlyContinue
        if ($null -ne $j -and $j.State -eq "Running") {
            Write-Host "  backend-api already running (Job $existing)"
            return
        }
        # stale entry
        Remove-JobIdEntry -Name "backend-api"
    }

    $dir = $Paths["backend-api"]
    if (-not (Test-Path $dir)) {
        throw "[backend-api] not found: $dir"
    }

    $job = Start-Job -Name "uecada-backend-api" -ScriptBlock {
        param($d)
        Set-Location $d
        dotnet run
    } -ArgumentList $dir

    Set-JobId -Name "backend-api" -JobId $job.Id
    Write-Host "  started backend-api as Job $($job.Id)"
    Write-Host "  BeApi:   http://localhost:5082"
    Write-Host "  logs:    uecada logs backend-api"
}

function Start-FrontendJob {
    $existing = Get-JobId -Name "frontend"
    if ($null -ne $existing) {
        $j = Get-Job -Id $existing -ErrorAction SilentlyContinue
        if ($null -ne $j -and $j.State -eq "Running") {
            Write-Host "  frontend already running (Job $existing)"
            return
        }
        Remove-JobIdEntry -Name "frontend"
    }

    $dir = $Paths["frontend"]
    if (-not (Test-Path $dir)) {
        throw "[frontend] not found: $dir"
    }

    $job = Start-Job -Name "uecada-frontend" -ScriptBlock {
        param($d)
        Set-Location $d
        npm run dev
    } -ArgumentList $dir

    Set-JobId -Name "frontend" -JobId $job.Id
    Write-Host "  started frontend as Job $($job.Id)"
    Write-Host "  Vite:   http://127.0.0.1:5173"
    Write-Host "  logs:   uecada logs frontend"
}

function Stop-LocalJob {
    param([string]$Name)

    $id = Get-JobId -Name $Name
    if ($null -eq $id) {
        Write-Host "  [$Name] no recorded Job — nothing to stop"
        return
    }

    $j = Get-Job -Id $id -ErrorAction SilentlyContinue
    if ($null -eq $j) {
        Write-Host "  [$Name] Job $id no longer exists — clearing record"
        Remove-JobIdEntry -Name $Name
        return
    }

    Write-Host "  stopping [$Name] Job $id ($($j.State))"
    Stop-Job -Id $id -ErrorAction SilentlyContinue | Out-Null
    Remove-Job  -Id $id -Force -ErrorAction SilentlyContinue | Out-Null
    Remove-JobIdEntry -Name $Name
}

# ---------- status ----------

function Show-Status {
    Write-Host ""
    Write-Host "=== UECADA status ==="

    foreach ($name in $StartOrder) {
        if ($DockerComponents -contains $name) {
            $dir = $Paths[$name]
            if (-not (Test-Path $dir)) {
                Write-Host ("  {0,-16} : (not found: $dir)" -f $name)
                continue
            }
            Push-Location $dir
            try {
                if ($name -eq "command-center") {
                    $out = & docker compose --env-file .env.command-center -f docker-compose.command-center.yml ps --format "{{.Service}}|{{.State}}" 2>$null
                } else {
                    $out = & docker compose ps --format "{{.Service}}|{{.State}}" 2>$null
                }
                if ([string]::IsNullOrWhiteSpace($out)) {
                    Write-Host ("  {0,-16} : (no containers)" -f $name)
                } else {
                    $lines = $out -split "`r?`n" | Where-Object { $_ -ne "" }
                    Write-Host ("  {0,-16} :" -f $name)
                    foreach ($l in $lines) {
                        $parts = $l -split '\|', 2
                        Write-Host ("      - {0,-20} {1}" -f $parts[0], $parts[1])
                    }
                }
            } finally {
                Pop-Location
            }
        } elseif ($JobComponents -contains $name) {
            $id = Get-JobId -Name $name
            if ($null -eq $id) {
                Write-Host ("  {0,-16} : (no job recorded)" -f $name)
            } else {
                $j = Get-Job -Id $id -ErrorAction SilentlyContinue
                if ($null -eq $j) {
                    Write-Host ("  {0,-16} : Job $id (missing)" -f $name)
                } else {
                    Write-Host ("  {0,-16} : Job $id ({1})" -f $name, $j.State)
                }
            }
        }
    }
    Write-Host ""
}

# ---------- logs ----------

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
            Invoke-DockerCompose -Component $Name -Args @("logs")
        } else {
            Push-Location $Paths[$Name]
            try {
                & docker compose logs -f
            } finally {
                Pop-Location
            }
        }
    } elseif ($JobComponents -contains $Name) {
        $id = Get-JobId -Name $Name
        if ($null -eq $id) {
            throw "[$Name] no recorded Job — start it first with: uecada start $Name"
        }
        $j = Get-Job -Id $id -ErrorAction SilentlyContinue
        if ($null -eq $j) {
            throw "[$Name] Job $id no longer exists"
        }
        Write-Host "==> follow Job $id ($Name)  (Ctrl+C to detach)"
        Receive-Job -Id $id -Keep -Wait
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
  logs <component>        follow logs (docker logs -f / Receive-Job -Wait)
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
  uecada start backend-api        # start only BeApi (dotnet run as Start-Job)
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
