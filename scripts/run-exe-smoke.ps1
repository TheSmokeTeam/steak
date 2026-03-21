param(
    [string]$PublishedExePath = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\publish\win-x64\Steak.exe"),
    [string]$BaseUrl = "http://127.0.0.1:4040",
    [string]$BootstrapServers = "localhost:9092",
    [string]$ApiExportDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) "verification-data\smoke-exports-api"),
    [string]$UiExportDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) "verification-data\smoke-exports-ui"),
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent

function Stop-SteakProcess {
    param([System.Diagnostics.Process]$Process)

    if (-not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        $Process.WaitForExit()
    }
}

function Wait-ForHttpReady {
    param(
        [string]$Url,
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            return $false
        }

        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return $true
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    return $false
}

if (-not (Test-Path $PublishedExePath)) {
    throw "Published executable was not found at '$PublishedExePath'. Run dotnet publish first."
}

$workingDirectory = Split-Path $PublishedExePath -Parent
$stdoutPath = Join-Path $workingDirectory "smoke.stdout.log"
$stderrPath = Join-Path $workingDirectory "smoke.stderr.log"
Remove-Item $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

$process = Start-Process -FilePath $PublishedExePath `
    -ArgumentList "--urls", $BaseUrl `
    -WorkingDirectory $workingDirectory `
    -PassThru `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath

try {
    if (-not (Wait-ForHttpReady -Url $BaseUrl -Process $process -TimeoutSeconds $TimeoutSeconds)) {
        $stdout = if (Test-Path $stdoutPath) { Get-Content $stdoutPath -Raw } else { "" }
        $stderr = if (Test-Path $stderrPath) { Get-Content $stderrPath -Raw } else { "" }
        throw "Steak did not become ready at $BaseUrl.`nSTDOUT:`n$stdout`nSTDERR:`n$stderr"
    }

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "local-kafka-smoke.ps1") `
        -BaseUrl $BaseUrl `
        -BootstrapServers $BootstrapServers `
        -ExportDirectory $ApiExportDirectory `
        -ExpectedExportDirectory $ApiExportDirectory `
        -TimeoutSeconds $TimeoutSeconds

    Push-Location $repoRoot
    try {
        $env:BASE_URL = $BaseUrl
        $env:UI_EXPORT_DIR = $UiExportDirectory
        $env:UI_SMOKE_TIMEOUT_MS = [string]($TimeoutSeconds * 1000)
        npm run ui-smoke
    }
    finally {
        Remove-Item Env:BASE_URL -ErrorAction SilentlyContinue
        Remove-Item Env:UI_EXPORT_DIR -ErrorAction SilentlyContinue
        Remove-Item Env:UI_SMOKE_TIMEOUT_MS -ErrorAction SilentlyContinue
        Pop-Location
    }
}
finally {
    Stop-SteakProcess -Process $process
    Remove-Item $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
}
