param(
    [string]$BaseUrl = "http://127.0.0.1:4040",
    [string]$BootstrapServers = "localhost:9092",
    [string]$SourceTopic = "orders",
    [string]$BatchTargetTopic = "payments",
    [string]$ExportDirectory = "",
    [string]$ExpectedExportDirectory = "",
    [int]$PublishCount = 3,
    [int]$TimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$requestExportDir = if ([string]::IsNullOrWhiteSpace($ExportDirectory)) {
    Join-Path $repoRoot "verification-data\smoke-exports-api"
}
else {
    $ExportDirectory
}

$exportDir = if ([string]::IsNullOrWhiteSpace($ExpectedExportDirectory)) {
    $requestExportDir
}
else {
    $ExpectedExportDirectory
}

function Write-Step {
    param([string]$Message)
    Write-Host ("== " + $Message)
}

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [string]$Description,
        [int]$TimeoutSeconds = 30,
        [int]$PollMilliseconds = 1000
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (& $Condition) {
            return
        }

        Start-Sleep -Milliseconds $PollMilliseconds
    }

    throw "Timed out waiting for $Description."
}

if (Test-Path $exportDir) {
    Remove-Item -Recurse -Force $exportDir
}

New-Item -ItemType Directory -Force -Path $exportDir | Out-Null

Write-Step "Connecting to Steak"
$connectBody = @{
    settings = @{
        bootstrapServers = $BootstrapServers
    }
} | ConvertTo-Json -Depth 5

$session = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/connection" -ContentType "application/json" -Body $connectBody
$sessionId = $session.connectionSessionId
if ([string]::IsNullOrWhiteSpace($sessionId)) {
    throw "Steak did not return a connectionSessionId."
}

Write-Step "Listing topics"
$topics = @(Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/topics?connectionSessionId=$sessionId")
$topicNames = @($topics | ForEach-Object { $_.name })
if ($topicNames -notcontains $SourceTopic) {
    throw "Source topic '$SourceTopic' was not discovered. Topics: $($topicNames -join ', ')"
}

if ($topicNames -notcontains $BatchTargetTopic) {
    throw "Batch target topic '$BatchTargetTopic' was not discovered. Topics: $($topicNames -join ', ')"
}

Write-Step "Publishing sample messages"
for ($index = 1; $index -le $PublishCount; $index++) {
    $payload = @{
        orderId = $index
        status = "queued"
        source = "local-kafka-smoke"
    } | ConvertTo-Json -Compress

    $publishBody = @{
        connectionSessionId = $sessionId
        topic = $SourceTopic
        envelope = @{
            app = "Steak"
            topic = $SourceTopic
            keyBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("order-$index"))
            valueBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
            headers = @(
                @{
                    key = "content-type"
                    valueBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("application/json"))
                }
            )
        }
    } | ConvertTo-Json -Depth 8

    $publishResult = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/publish" -ContentType "application/json" -Body $publishBody
    if ($publishResult.topic -ne $SourceTopic) {
        throw "Publish returned an unexpected topic: $($publishResult.topic)"
    }
}

Write-Step "Starting live view on source topic"
$viewBody = @{
    connectionSessionId = $sessionId
    topic = $SourceTopic
    offsetMode = "Earliest"
    maxMessages = 50
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/view-sessions" -ContentType "application/json" -Body $viewBody | Out-Null

$sourceViewStatus = $null
Wait-Until -Description "source topic view session to receive messages" -TimeoutSeconds $TimeoutSeconds -Condition {
    $script:sourceViewStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/view-sessions"
    return [int64]$script:sourceViewStatus.receivedCount -ge $PublishCount
}

Invoke-RestMethod -Method Delete -Uri "$BaseUrl/api/view-sessions" | Out-Null

Write-Step "Starting consume export"
$groupId = "steak-smoke-" + [Guid]::NewGuid().ToString("N")
$consumeBody = @{
    connectionSessionId = $sessionId
    topic = $SourceTopic
    groupId = $groupId
    offsetMode = "Earliest"
    maxMessages = $PublishCount
    destination = @{
        transportKind = "FileSystem"
        fileSystem = @{
            path = $requestExportDir
        }
    }
} | ConvertTo-Json -Depth 8

Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/consume-jobs" -ContentType "application/json" -Body $consumeBody | Out-Null

$consumeStatus = $null
Wait-Until -Description "consume export to write files" -TimeoutSeconds $TimeoutSeconds -Condition {
    $script:consumeStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/consume-jobs"
    return [int64]$script:consumeStatus.exportedCount -ge $PublishCount
}

Invoke-RestMethod -Method Delete -Uri "$BaseUrl/api/consume-jobs" | Out-Null

$exportedFiles = @(Get-ChildItem -Path $exportDir -Filter *.json | Sort-Object Name)
if ($exportedFiles.Count -lt $PublishCount) {
    throw "Expected at least $PublishCount exported files but found $($exportedFiles.Count)."
}

Write-Step "Starting batch publish"
$batchBody = @{
    connectionSessionId = $sessionId
    topicOverride = $BatchTargetTopic
    maxMessages = $exportedFiles.Count
    source = @{
        transportKind = "FileSystem"
        fileSystem = @{
            path = $requestExportDir
        }
    }
} | ConvertTo-Json -Depth 8

Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/batch-publish" -ContentType "application/json" -Body $batchBody | Out-Null

$batchStatus = $null
Wait-Until -Description "batch publish to finish" -TimeoutSeconds $TimeoutSeconds -Condition {
    $script:batchStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/batch-publish"
    return [int64]$script:batchStatus.publishedCount -ge $exportedFiles.Count
}

Invoke-RestMethod -Method Delete -Uri "$BaseUrl/api/batch-publish" | Out-Null

if ([int64]$batchStatus.totalEnvelopes -lt [int64]$exportedFiles.Count) {
    throw "Batch publish did not report the expected total envelope count."
}

Write-Step "Starting live view on batch target topic"
$targetViewBody = @{
    connectionSessionId = $sessionId
    topic = $BatchTargetTopic
    offsetMode = "Earliest"
    maxMessages = 50
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/view-sessions" -ContentType "application/json" -Body $targetViewBody | Out-Null

$targetViewStatus = $null
Wait-Until -Description "batch target view session to receive messages" -TimeoutSeconds $TimeoutSeconds -Condition {
    $script:targetViewStatus = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/view-sessions"
    return [int64]$script:targetViewStatus.receivedCount -ge $exportedFiles.Count
}

Invoke-RestMethod -Method Delete -Uri "$BaseUrl/api/view-sessions" | Out-Null

Write-Step "Smoke run succeeded"
[pscustomobject]@{
    ConnectionSessionId = $sessionId
    Topics = $topicNames
    SourceViewReceived = [int64]$sourceViewStatus.receivedCount
    ExportedFiles = @($exportedFiles | ForEach-Object { $_.Name })
    BatchPublished = [int64]$batchStatus.publishedCount
    BatchTotalEnvelopes = [int64]$batchStatus.totalEnvelopes
    TargetViewReceived = [int64]$targetViewStatus.receivedCount
    ExportDirectory = $exportDir
} | ConvertTo-Json -Depth 6
