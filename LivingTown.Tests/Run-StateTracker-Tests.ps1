$ErrorActionPreference = 'Stop'

function Assert-Equal($actual, $expected, [string]$message) {
    if ($actual -ne $expected) {
        throw "$message`nExpected: $expected`nActual:   $actual"
    }
}

function Assert-True($condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$newtonsoftPath = Join-Path $repoRoot 'LivingTown\bin\Debug\net6.0\Newtonsoft.Json.dll'
if (-not (Test-Path $newtonsoftPath)) {
    throw "Missing Newtonsoft.Json assembly at $newtonsoftPath. Build the mod once before running tests."
}

$stub = @"
namespace StardewModdingAPI
{
    public enum LogLevel { Trace, Debug, Info, Warn, Error, Alert }
    public interface IMonitor
    {
        void Log(string message, LogLevel level = LogLevel.Debug);
    }
}
"@

$sourceFiles = @(
    (Join-Path $repoRoot 'LivingTown\src\state\TrackerStateModels.cs'),
    (Join-Path $repoRoot 'LivingTown\src\state\GameStateTracker.cs')
)

$typeDefinition = $stub + [Environment]::NewLine + (($sourceFiles | ForEach-Object { Get-Content $_ -Raw }) -join [Environment]::NewLine)
Add-Type -TypeDefinition $typeDefinition -ReferencedAssemblies $newtonsoftPath

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('LivingTownStateTests_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    $trackerType = [LivingTown.State.GameStateTracker]
    $statePath = Join-Path $tempRoot 'game-state.json'
    $tracker = New-Object $trackerType -ArgumentList @($null, $statePath)

    0..5 | ForEach-Object { $tracker.RecordDialogue('Sebastian', "topic-$_") }
    $daily = $tracker.GetDailyState('Sebastian')
    $persistent = $tracker.GetPersistentState('Sebastian')
    Assert-Equal $daily.DialoguesToday 6 'Dialogue count should increment.'
    Assert-Equal $daily.RecentTopics.Count 5 'Recent topics should be trimmed to five.'
    Assert-Equal $daily.RecentTopics[0] 'topic-1' 'Oldest topic should be dropped first.'
    Assert-Equal $persistent.SocialFatigue 6 'Dialogue should add one fatigue each time.'

    $tracker.RecordGift('Sebastian', 'Sashimi', [LivingTown.State.GiftTaste]::Like)
    $daily = $tracker.GetDailyState('Sebastian')
    $persistent = $tracker.GetPersistentState('Sebastian')
    Assert-Equal $daily.GiftsToday 1 'Gift count should increment.'
    Assert-Equal $daily.LastGiftItem 'Sashimi' 'Last gift item should be recorded.'
    Assert-Equal ([int]$daily.LastGiftTaste) ([int][LivingTown.State.GiftTaste]::Like) 'Gift taste should be recorded.'
    Assert-Equal $persistent.TotalGifts 1 'Persistent total gifts should increment.'
    Assert-Equal $persistent.SocialFatigue 11 'Gift should add five fatigue.'

    $tracker.ResetDaily('Y1_Day2')
    $daily = $tracker.GetDailyState('Sebastian')
    Assert-Equal $daily.DialoguesToday 0 'Daily dialogue count should reset.'
    Assert-Equal $daily.GiftsToday 0 'Daily gift count should reset.'
    Assert-Equal $daily.RecentTopics.Count 0 'Recent topics should reset.'

    $tracker.EndDay()
    $persistent = $tracker.GetPersistentState('Sebastian')
    Assert-Equal $persistent.SocialFatigue 8 'No-interaction day should decay fatigue by three.'

    $shipmentsDay10 = [System.Collections.Generic.List[LivingTown.State.ShippingRecord]]::new()
    $shipmentsDay10.Add([LivingTown.State.ShippingRecord]@{ ItemName = 'Blueberry'; Quantity = 100 })
    $tracker.RecordShipping($shipmentsDay10, 10)

    $shipmentsDay11 = [System.Collections.Generic.List[LivingTown.State.ShippingRecord]]::new()
    $shipmentsDay11.Add([LivingTown.State.ShippingRecord]@{ ItemName = 'Blueberry'; Quantity = 200 })
    $tracker.RecordShipping($shipmentsDay11, 11)

    $crop = $tracker.GetEconomyState().Crops['Blueberry']
    Assert-Equal $crop.ConsecutiveDays 2 'Consecutive shipment days should accumulate.'
    Assert-Equal $crop.TotalDumped 300 'Total dumped quantity should accumulate.'

    $reloaded = New-Object $trackerType -ArgumentList @($null, $statePath)
    $reloadedPersistent = $reloaded.GetPersistentState('Sebastian')
    $reloadedCrop = $reloaded.GetEconomyState().Crops['Blueberry']
    Assert-Equal $reloadedPersistent.TotalDialogues 6 'Persistent dialogue total should survive reload.'
    Assert-Equal $reloadedPersistent.TotalGifts 1 'Persistent gift total should survive reload.'
    Assert-Equal $reloadedCrop.TotalDumped 300 'Economy totals should survive reload.'

    $summary = $reloaded.GetStateForPrompt('Sebastian')
    Assert-True ($summary.Contains('Social fatigue: 8')) 'Prompt summary should include persistent fatigue.'

    Write-Host 'State tracker tests passed.'
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force
    }
}