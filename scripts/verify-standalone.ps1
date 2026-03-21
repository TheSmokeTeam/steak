param(
    [string]$PublishedExePath = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\publish\win-x64\Steak.exe"),
    [int]$PublishFolderPort = 4055,
    [int]$ExeOnlyPort = 4056,
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

function Get-LogContent {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return ""
    }

    return Get-Content $Path -Raw
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
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return $true
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    return $false
}

function Test-SteakLaunch {
    param(
        [string]$WorkingDirectory,
        [int]$Port
    )

    $exePath = Join-Path $WorkingDirectory "Steak.exe"
    $stdoutPath = Join-Path $WorkingDirectory "stdout.log"
    $stderrPath = Join-Path $WorkingDirectory "stderr.log"
    $url = "http://127.0.0.1:$Port"

    Remove-Item $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

    $process = Start-Process -FilePath $exePath `
        -ArgumentList "--urls", $url `
        -WorkingDirectory $WorkingDirectory `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath

    try {
        if (-not (Wait-ForHttpReady -Url $url -Process $process -TimeoutSeconds $TimeoutSeconds)) {
            $stdout = Get-LogContent -Path $stdoutPath
            $stderr = Get-LogContent -Path $stderrPath
            throw "Steak did not become ready at $url.`nSTDOUT:`n$stdout`nSTDERR:`n$stderr"
        }

        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -ne 200) {
            throw "Expected HTTP 200 from $url but got $($response.StatusCode)."
        }

        Remove-Item $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue

        return [pscustomobject]@{
            Url = $url
            StatusCode = $response.StatusCode
            WorkingDirectory = $WorkingDirectory
        }
    }
finally {
    Stop-SteakProcess -Process $process
    Remove-Item $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
}
}

if (-not (Test-Path $PublishedExePath)) {
    throw "Published executable was not found at '$PublishedExePath'. Run dotnet publish first."
}

$publishFolder = Split-Path $PublishedExePath -Parent
$exeOnlyFolder = Join-Path $repoRoot "artifacts\verification\exe-only"

if (Test-Path $exeOnlyFolder) {
    Remove-Item $exeOnlyFolder -Recurse -Force
}

New-Item -ItemType Directory -Path $exeOnlyFolder -Force | Out-Null
Copy-Item $PublishedExePath (Join-Path $exeOnlyFolder "Steak.exe")

$publishFolderResult = Test-SteakLaunch -WorkingDirectory $publishFolder -Port $PublishFolderPort
$exeOnlyResult = Test-SteakLaunch -WorkingDirectory $exeOnlyFolder -Port $ExeOnlyPort

[pscustomobject]@{
    publishFolder = $publishFolderResult
    exeOnly = $exeOnlyResult
} | ConvertTo-Json -Depth 4
