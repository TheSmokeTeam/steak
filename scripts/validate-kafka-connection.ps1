param(
    [string]$ExecutablePath = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\publish\win-x64\Steak.exe"),
    [int]$Port = 4060,
    [string]$BootstrapServers = "localhost:9092",
    [string]$Username = "admin",
    [string]$Password = "admin",
    [string]$SecurityProtocol = "SaslPlaintext",
    [string]$SaslMechanism = "Plain",
    [string]$LogLevel = "Debug",
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"

function Stop-ValidationProcess {
    param([System.Diagnostics.Process]$Process)

    if ($null -ne $Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        $Process.WaitForExit()
    }
}

function Wait-ForHealth {
    param(
        [string]$Url,
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            throw "Steak exited before becoming healthy. Exit code: $($Process.ExitCode)."
        }

        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for Steak health endpoint at $Url."
}

function Get-ResponseBody {
    param($Response)

    if ($null -eq $Response) {
        return $null
    }

    $stream = $Response.GetResponseStream()
    if ($null -eq $stream) {
        return $null
    }

    $reader = New-Object System.IO.StreamReader($stream)
    return $reader.ReadToEnd()
}

if (-not (Test-Path $ExecutablePath)) {
    throw "Executable not found: $ExecutablePath"
}

$workingDirectory = Split-Path $ExecutablePath -Parent
$validationRoot = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\validation"
$runRoot = Join-Path $validationRoot ("port-" + $Port)
$stdoutPath = Join-Path $runRoot "stdout.log"
$stderrPath = Join-Path $runRoot "stderr.log"

New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
Remove-Item $stdoutPath,$stderrPath -Force -ErrorAction SilentlyContinue

$process = $null
$result = [ordered]@{
    port = $Port
    bootstrapServers = $BootstrapServers
    username = $Username
    securityProtocol = $SecurityProtocol
    saslMechanism = $SaslMechanism
    rootStatusCode = $null
    topicsRequestSucceeded = $false
    topicsStatusCode = $null
    topicCount = $null
    errorDetail = $null
    exceptionMessage = $null
    stdoutPath = $stdoutPath
    stderrPath = $stderrPath
}

try {
    $process = Start-Process -FilePath $ExecutablePath `
        -ArgumentList "--port", $Port, "--log-level", $LogLevel, "--open-browser", "false" `
        -WorkingDirectory $workingDirectory `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath

    Wait-ForHealth -Url "http://127.0.0.1:$Port/api/health" -Process $process -TimeoutSeconds $TimeoutSeconds
    $rootResponse = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/" -UseBasicParsing -TimeoutSec $TimeoutSeconds
    $result.rootStatusCode = $rootResponse.StatusCode

    $connectPayload = @{
        settings = @{
            bootstrapServers = $BootstrapServers
            username = $Username
            password = $Password
            securityProtocol = $SecurityProtocol
            saslMechanism = $SaslMechanism
            clientId = "validation-$Port"
        }
    } | ConvertTo-Json -Depth 8

    $connectResponse = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$Port/api/connection" -ContentType "application/json" -Body $connectPayload
    $result.connectionSessionId = $connectResponse.connectionSessionId

    try {
        $topicsResponse = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/api/topics?connectionSessionId=$($connectResponse.connectionSessionId)" -UseBasicParsing -TimeoutSec $TimeoutSeconds
        $result.topicsRequestSucceeded = $topicsResponse.StatusCode -eq 200
        $result.topicsStatusCode = $topicsResponse.StatusCode
        if ($topicsResponse.Content) {
            $topics = $topicsResponse.Content | ConvertFrom-Json
            if ($topics -is [System.Array]) {
                $result.topicCount = $topics.Count
            }
            elseif ($null -ne $topics) {
                $result.topicCount = 1
            }
        }
    }
    catch {
        $response = $_.Exception.Response
        $result.topicsStatusCode = if ($response) { [int]$response.StatusCode } else { $null }
        $result.errorDetail = Get-ResponseBody -Response $response
        $result.exceptionMessage = $_.Exception.Message
    }
}
finally {
    Stop-ValidationProcess -Process $process
    $result.stdoutTail = if (Test-Path $stdoutPath) { (Get-Content $stdoutPath -Tail 80) -join [Environment]::NewLine } else { "" }
    $result.stderrTail = if (Test-Path $stderrPath) { (Get-Content $stderrPath -Tail 80) -join [Environment]::NewLine } else { "" }
}

$result | ConvertTo-Json -Depth 6
