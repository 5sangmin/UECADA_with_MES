# scripts/up-command-center.ps1
# ----------------------------------------------------------------------
# Command Center (Node-RED) bring-up.
#
# 단독 기동 가능 — line-das 들이 떠있지 않아도 OPC UA Server 만 자체적으로 뜬다.
# factory-net 만 미리 만들어져 있으면 됨.
#
# Usage:
#   .\scripts\up-command-center.ps1               # up
#   .\scripts\up-command-center.ps1 down          # down
#   .\scripts\up-command-center.ps1 logs          # docker compose logs -f
#   .\scripts\up-command-center.ps1 ps            # docker compose ps
# ----------------------------------------------------------------------

param(
    [Parameter(Position = 0)]
    [ValidateSet("up", "down", "logs", "ps", "status")]
    [string]$Command = "up"
)

$ErrorActionPreference = "Stop"

# Force UTF-8 on console to avoid cp949 garbling
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
    $env:PYTHONIOENCODING = "utf-8"
} catch { }

# 스크립트는 equip-sim/ 안에서 docker compose 를 실행해야 한다.
# Set-Location 을 그대로 쓰면 종료 후 호출자의 cwd 가 바뀌어 남는 문제가 있어,
# Push-Location + try/finally Pop-Location 패턴으로 격리한다.
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$EquipSimDir = Join-Path $ScriptDir ".."

$EnvFile     = ".env.command-center"
$ComposeFile = "docker-compose.command-center.yml"

function Ensure-Network {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    docker network inspect factory-net *> $null
    $code = $LASTEXITCODE
    $ErrorActionPreference = $prev

    if ($code -ne 0) {
        Write-Host "==> creating docker network: factory-net"
        docker network create factory-net | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "failed to create factory-net" }
    } else {
        Write-Host "==> factory-net already exists"
    }
}

function Check-EnvFile {
    if (-not (Test-Path $EnvFile)) {
        throw ".env file not found: $EnvFile  (운영 전에 COMMANDDB_PASSWORD 등을 채워주세요)"
    }
    $content = Get-Content $EnvFile -Raw
    if ($content -match "__CHANGE_ME__") {
        Write-Warning "$EnvFile 에 __CHANGE_ME__ placeholder 가 남아있습니다. 실제 비밀값으로 교체하세요."
    }
}

Push-Location $EquipSimDir
try {
    switch ($Command) {
        "up" {
            Ensure-Network
            Check-EnvFile
            Write-Host "==> docker compose --env-file $EnvFile -f $ComposeFile up -d --build"
            docker compose --env-file $EnvFile -f $ComposeFile up -d --build
            if ($LASTEXITCODE -ne 0) { throw "compose up failed" }
            Write-Host ""
            Write-Host "==================================================================="
            Write-Host " Command Center UP"
            Write-Host "   Node-RED UI       : http://localhost:5888"
            Write-Host "   OPC UA Server     : opc.tcp://localhost:5160"
            Write-Host ""
            Write-Host "   네트워크          : factory-net (line-das 와 공유)"
            Write-Host "   컨테이너 이름     : command-center"
            Write-Host "==================================================================="
        }
        "down" {
            Write-Host "==> docker compose --env-file $EnvFile -f $ComposeFile down"
            docker compose --env-file $EnvFile -f $ComposeFile down
            Write-Host "==> NOTE: factory-net 은 유지됨. 제거하려면: docker network rm factory-net"
        }
        "logs" {
            docker compose --env-file $EnvFile -f $ComposeFile logs -f
        }
        { @("ps", "status") -contains $_ } {
            docker compose --env-file $EnvFile -f $ComposeFile ps
        }
    }
}
finally {
    Pop-Location
}
