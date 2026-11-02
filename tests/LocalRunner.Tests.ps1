# Run with: powershell -NoProfile -File tests/LocalRunner.Tests.ps1
# All process/listener/stop operations below are fixtures; no OS process is stopped.
$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Path (Join-Path $projectRoot ".local-build") -Force | Out-Null
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $projectRoot "scripts\moodify-local.ps1"), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw "Runner syntax invalid." }
$functions = @("Test-RecordedProcessIdentity", "Get-VerifiedManagedProcesses", "Test-ExpectedProcess", "Stop-ManagedProcesses", "Get-ModelSelection", "Test-VaCandidate")
foreach ($name in $functions) {
    $definition = $ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst]
    }, $false) | Where-Object Name -eq $name | Select-Object -First 1
    if (-not $definition) { throw "Missing runner function: $name" }
    Invoke-Expression $definition.Extent.Text
}
$script:checks = 0
function Assert($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
function Reset-Fixture {
    $script:records = @{}
    $script:listeners = @{}
    $script:stopped = @()
}
function Get-ProcessRecord([int]$ProcessId) { return $script:records[$ProcessId] }
function Get-Listener([int]$Port) { return $script:listeners[$Port] }
function Stop-Process([int]$Id) {
    $script:stopped += $Id
    [void]$script:records.Remove($Id)
}
function Wait-Process([int]$Id, [int]$Timeout, $ErrorAction) {}
function Record([int]$Number, [string]$Command, [int]$Parent = 1, [string]$Created = "2026-10-29T00:00:00Z") {
    return [pscustomobject]@{ ProcessId=$Number; CommandLine=$Command; ParentProcessId=$Parent; CreationDate=[DateTime]::Parse($Created).ToUniversalTime() }
}
$script:statePath = Join-Path $projectRoot (".local-build\runner-identity-test-" + [Guid]::NewGuid().ToString("N") + ".json")
function Fixture-State($Entry) {
    '{}' | Set-Content -LiteralPath $script:statePath
    return [pscustomobject]@{ Processes=@($Entry) }
}
try {
    Reset-Fixture
    $legacy = [pscustomobject]@{ Name="backend"; Pid=100; Port=5170; CommandMatch="mood_recommendation.dll" }
    $script:records[100] = Record 100 "XboxService.exe"
    Assert (@(Get-VerifiedManagedProcesses $legacy).Count -eq 0) "Reused Xbox PID must not count as a live stack."
    Stop-ManagedProcesses (Fixture-State $legacy) | Out-Null
    Assert ($script:stopped.Count -eq 0 -and $script:records.ContainsKey(100)) "Stop must leave a reused Xbox PID untouched."
    Assert (-not (Test-Path -LiteralPath $script:statePath)) "Obsolete state should be removable."

    Reset-Fixture
    $entry = [pscustomobject]@{ Name="backend"; Pid=100; Port=5170; CommandMatch="mood_recommendation.dll"; CreatedAtUtc="2026-10-29T00:00:00Z" }
    $script:records[100] = Record 100 "dotnet mood_recommendation.dll" 1 "2026-10-29T00:00:05Z"
    Assert (@(Get-VerifiedManagedProcesses $entry).Count -eq 0) "Same command with a different creation time is not the recorded process."
    $script:records[100] = Record 100 "dotnet mood_recommendation.dll"
    Assert (@(Get-VerifiedManagedProcesses $entry).Count -eq 1) "A verified parent still counts before its port opens."
    Assert (-not (Test-ExpectedProcess $entry)) "No listening port is not a healthy service."
    $script:listeners[5170] = [pscustomobject]@{OwningProcess=100}
    Assert (Test-ExpectedProcess $entry) "Matching process and listener should be healthy."

    Reset-Fixture
    $script:listeners[5170] = [pscustomobject]@{OwningProcess=900}
    $script:records[900] = Record 900 "unmanaged-other-server.exe"
    $refused = $false
    try { Stop-ManagedProcesses (Fixture-State $legacy) | Out-Null } catch { $refused = $true }
    Assert ($refused -and $script:stopped.Count -eq 0) "An unrelated port owner must not be stopped."

    Reset-Fixture
    $ai = [pscustomobject]@{
        Name="inference"; Pid=100; Port=8001; CommandMatch="uvicorn main:app"
        CreatedAtUtc="2026-10-29T00:00:00Z"; WorkerPid=200; WorkerCreatedAtUtc="2026-10-29T00:00:01Z"
    }
    $script:records[100] = Record 100 "python -m uvicorn main:app"
    $script:records[200] = Record 200 "python -m uvicorn main:app" 100 "2026-10-29T00:00:01Z"
    $script:listeners[8001] = [pscustomobject]@{OwningProcess=200}
    Assert ((@(Get-VerifiedManagedProcesses $ai).Count -eq 2) -and (Test-ExpectedProcess $ai)) "Uvicorn launcher and worker should both be recognized."
    Stop-ManagedProcesses (Fixture-State $ai) | Out-Null
    Assert (($script:stopped -join ",") -eq "200,100") "Stop the verified worker before its launcher."

    Reset-Fixture
    $script:records[100] = Record 100 "XboxService.exe"
    $script:records[200] = Record 200 "python -m uvicorn main:app" 100 "2026-10-29T00:00:01Z"
    $script:listeners[8001] = [pscustomobject]@{OwningProcess=200}
    Stop-ManagedProcesses (Fixture-State $ai) | Out-Null
    Assert (($script:stopped -join ",") -eq "200" -and $script:records.ContainsKey(100)) "A verified orphan worker may stop, but its reused parent PID must remain untouched."

    Reset-Fixture
    $script:records[100] = Record 100 "python -m uvicorn main:app"
    $script:records[200] = Record 200 "python -m uvicorn main:app" 100 "2026-10-29T00:00:06Z"
    $script:listeners[8001] = [pscustomobject]@{OwningProcess=200}
    Assert (@(Get-VerifiedManagedProcesses $ai).Count -eq 1) "A reused worker PID must not bypass its timestamp through port discovery."
    $refused = $false
    try { Stop-ManagedProcesses (Fixture-State $ai) | Out-Null } catch { $refused = $true }
    Assert ($refused -and $script:stopped.Count -eq 0) "Do not stop a replaced worker or its launcher while ownership is ambiguous."

    Reset-Fixture
    $pending = [pscustomobject]@{Name="inference";Pid=100;Port=8001;CommandMatch="uvicorn main:app";CreatedAtUtc="2026-10-29T00:00:00Z"}
    $script:records[100] = Record 100 "python -m uvicorn main:app"
    $script:records[200] = Record 200 "python -m uvicorn main:app" 100 "2026-10-29T00:00:01Z"
    $script:listeners[8001] = [pscustomobject]@{OwningProcess=200}
    Assert (@(Get-VerifiedManagedProcesses $pending).Count -eq 2) "Recognize a genuine startup worker before its PID is saved."

    Reset-Fixture
    Assert (@(Get-VerifiedManagedProcesses $legacy).Count -eq 0) "An empty/stale state must not count as running."
    $selectionMarker = $script:statePath + ".selection"
    try {
        $selected = Get-ModelSelection $selectionMarker "roberta_va_regression"
        Assert ($selected.Version -eq "roberta_va_regression" -and -not $selected.Controlled) "Legacy fallback should remain compatible."
        'roberta_new_classifier' | Set-Content -LiteralPath $selectionMarker
        Assert ((Get-ModelSelection $selectionMarker).Version -eq "roberta_new_classifier") "An active classifier must be selected by version."
        '{"revision":3,"active_version":null}' | Set-Content -LiteralPath ($selectionMarker + ".lifecycle.json")
        $selected = Get-ModelSelection $selectionMarker "roberta_va_regression"
        Assert ($selected.Disabled -and -not $selected.Version) "Authoritative deactivation must override stale legacy marker and fallback."
        '{"revision":4,"active_version":"../escape"}' | Set-Content -LiteralPath ($selectionMarker + ".lifecycle.json")
        $refused=$false
        try { $null=Get-ModelSelection $selectionMarker } catch { $refused=$true }
        Assert $refused "A path outside model roots must be refused."
    }
    finally {
        Remove-Item -LiteralPath $selectionMarker,($selectionMarker + ".lifecycle.json") -ErrorAction SilentlyContinue
    }
    Write-Output "PASS: $script:checks runner identity, lifecycle selection and shutdown assertions. No OS process was stopped."
}
finally {
    Remove-Item -LiteralPath $script:statePath -ErrorAction SilentlyContinue
}