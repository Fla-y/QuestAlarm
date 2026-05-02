param(
    [string] $ApiUrl = "http://localhost:5055",
    [string] $Configuration = "Debug",
    [switch] $SkipFakeClientPublish
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$fakeClientProject = Join-Path $repoRoot "QuestAlarm.FakeChallengeClient\QuestAlarm.FakeChallengeClient.csproj"
$apiProject = Join-Path $repoRoot "QuestAlarm.Api\QuestAlarm.Api.csproj"
$consoleProject = Join-Path $repoRoot "QuestAlarm.console\QuestAlarm.Console.csproj"
$fakeClientOutput = Join-Path $env:LOCALAPPDATA "QuestAlarm\Tools\FakeChallengeClient"
$storageRoot = Join-Path $env:LOCALAPPDATA "QuestAlarm"

Write-Host "QuestAlarm dev launcher"
Write-Host "Repo:          $repoRoot"
Write-Host "API URL:       $ApiUrl"
Write-Host "Storage root:  $storageRoot"
Write-Host "Fake client:   $fakeClientOutput"
Write-Host ""

if (-not $SkipFakeClientPublish) {
    Write-Host "Publishing fake challenge client..."
    dotnet publish $fakeClientProject -c $Configuration -o $fakeClientOutput
    Write-Host ""
}

Write-Host "Starting QuestAlarm.Api..."
$apiProcess = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList @(
        "run",
        "--project",
        $apiProject,
        "--configuration",
        $Configuration,
        "--urls",
        $ApiUrl
    ) `
    -PassThru `
    -WindowStyle Hidden

try {
    $healthUrl = "$ApiUrl/health"
    $isReady = $false

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 2 | Out-Null
            $isReady = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $isReady) {
        throw "QuestAlarm.Api did not become ready at $healthUrl."
    }

    Write-Host "QuestAlarm.Api is ready."
    Write-Host "Starting QuestAlarm.Console..."
    Write-Host "Exit the console app to stop the dev API."
    Write-Host ""

    dotnet run --project $consoleProject --configuration $Configuration
}
finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Write-Host ""
        Write-Host "Stopping QuestAlarm.Api..."
        Stop-Process -Id $apiProcess.Id -Force
    }
}
