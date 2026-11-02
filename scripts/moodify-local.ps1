param(
    [ValidateSet("Start", "Status", "Stop")]
    [string]$Action = "Status",
    [switch]$WithoutAI,
    [switch]$WithoutLegacyClassifier,
    [string]$ListenAddress = "127.0.0.1"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$backendDir = Join-Path $root "backend"
$adminDir = Join-Path $root "admin-web"
$aiDir = Join-Path $root "ai-service"
$modelDir = Join-Path $root "code-train\train-KLCN\output\roberta_large_asl_en_01\best_model"
$vaModelRoot = Join-Path $root "code-train\train-KLCN\output"
$vaModelDir = Join-Path $vaModelRoot "roberta_va_regression"
$viModelRoot = Join-Path $root "code-train\train-KLCN\output\vietnamese-emotion"
$modelHash = "EA865317D9EB7CF1B39008062276BAE441801A7D719D33177A3DDD1851AF7B09"
$apiPort = 5170
$adminPort = 5174
$aiPort = 8001
$localApiHost = if ($ListenAddress -eq "0.0.0.0") { "127.0.0.1" } else { $ListenAddress }
$apiUrl = "http://${localApiHost}:${apiPort}"
$aiUrl = "http://127.0.0.1:${aiPort}/"
$stateDir = Join-Path $env:LOCALAPPDATA "Moodify"
$statePath = Join-Path $stateDir "local-services.json"
$logDir = Join-Path $stateDir "logs"

New-Item -ItemType Directory -Force -Path $stateDir, $logDir | Out-Null

function Get-Listener([int]$Port) {
    return Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

function Get-ProcessRecord([int]$ProcessId) {
    return Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
}

function Read-State {
    if (-not (Test-Path -LiteralPath $statePath)) { return $null }
    return Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
}

function Save-State($State) {
    $tempPath = "$statePath.tmp"
    $State | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $tempPath -Encoding UTF8
    Move-Item -LiteralPath $tempPath -Destination $statePath -Force
}

function Test-RecordedProcessIdentity($Process, $Entry, [switch]$Worker) {
    if (-not $Process -or $Process.CommandLine -notlike "*$($Entry.CommandMatch)*") {
        return $false
    }
    $timestamp = $null
    if ($Worker) {
        if ([int]$Process.ParentProcessId -ne [int]$Entry.Pid) { return $false }
        if ($Entry.PSObject.Properties["WorkerPid"] -and
            [int]$Process.ProcessId -eq [int]$Entry.WorkerPid -and
            $Entry.PSObject.Properties["WorkerCreatedAtUtc"]) {
            $timestamp = $Entry.WorkerCreatedAtUtc
        }
    }
    elseif ($Entry.PSObject.Properties["CreatedAtUtc"]) {
        $timestamp = $Entry.CreatedAtUtc
    }
    if ($timestamp) {
        try {
            $expected = [DateTimeOffset]::Parse([string]$timestamp).UtcDateTime
            $actual = ([DateTime]$Process.CreationDate).ToUniversalTime()
            if ([math]::Abs(($actual - $expected).TotalMilliseconds) -gt 1) { return $false }
        }
        catch { return $false }
    }
    return $true
}

function Get-VerifiedManagedProcesses($Entry) {
    $parent = Get-ProcessRecord ([int]$Entry.Pid)
    $parentMatches = Test-RecordedProcessIdentity $parent $Entry
    $verified = @{}
    if ($parentMatches) { $verified[[int]$parent.ProcessId] = $parent }
    if ($Entry.PSObject.Properties["WorkerPid"]) {
        $worker = Get-ProcessRecord ([int]$Entry.WorkerPid)
        if (Test-RecordedProcessIdentity $worker $Entry -Worker) {
            $verified[[int]$worker.ProcessId] = $worker
        }
    }
    # Startup can fail before a newly launched worker has been saved to state.
    # Discover it only beneath a verified parent, never through the port alone.
    if ($parentMatches -and $Entry.Port -gt 0) {
        $listener = Get-Listener ([int]$Entry.Port)
        if ($listener -and [int]$listener.OwningProcess -ne [int]$Entry.Pid) {
            $worker = Get-ProcessRecord ([int]$listener.OwningProcess)
            if (Test-RecordedProcessIdentity $worker $Entry -Worker) {
                $verified[[int]$worker.ProcessId] = $worker
            }
        }
    }
    return @($verified.Values)
}

function Test-ExpectedProcess($Entry) {
    $process = Get-ProcessRecord ([int]$Entry.Pid)
    if (-not (Test-RecordedProcessIdentity $process $Entry)) { return $false }
    if ($Entry.Port -gt 0) {
        $listener = Get-Listener ([int]$Entry.Port)
        if (-not $listener) { return $false }
        if ([int]$listener.OwningProcess -eq [int]$Entry.Pid) { return $true }
        $worker = Get-ProcessRecord ([int]$listener.OwningProcess)
        return Test-RecordedProcessIdentity $worker $Entry -Worker
    }
    return $true
}

function Invoke-Readiness([string]$Uri, [int]$TimeoutSeconds, [string]$ExpectedVersion = "") {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = "No response received."
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-RestMethod -Uri $Uri -TimeoutSec 3
            if ($ExpectedVersion -and $response.model_version -ne $ExpectedVersion) {
                throw "Unexpected model version from $Uri."
            }
            return $response
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Seconds 2
        }
    }
    throw "Readiness timeout for $Uri after $TimeoutSeconds seconds. Last error: $lastError"
}

function Get-ModelSelection([string]$MarkerPath, [string]$FallbackVersion = "") {
    $lifecyclePath = $MarkerPath + ".lifecycle.json"
    $controlled = $false
    $version = $FallbackVersion
    if (Test-Path -LiteralPath $lifecyclePath) {
        $state = Get-Content -LiteralPath $lifecyclePath -Raw | ConvertFrom-Json
        if (-not $state.PSObject.Properties["revision"] -or -not $state.PSObject.Properties["active_version"]) {
            throw "Invalid model lifecycle state: $lifecyclePath"
        }
        $version = [string]$state.active_version
        $controlled = $true
    }
    elseif (Test-Path -LiteralPath $MarkerPath) {
        $version = (Get-Content -LiteralPath $MarkerPath -Raw).Trim()
        $controlled = $true
    }
    $disabled = $controlled -and (-not $version -or $version -eq "none")
    if ($version -and -not $disabled -and $version -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{2,79}$') {
        throw "Invalid selected model version. Review $MarkerPath; it was not overwritten."
    }
    return [pscustomobject]@{ Version = $(if ($disabled) { "" } else { $version }); Disabled = $disabled; Controlled = $controlled }
}

function Test-VaCandidate([string]$CandidateDirectory, [string]$Version) {
    $checkpoint = Join-Path $CandidateDirectory ($Version + ".pt")
    if (-not (Test-Path -LiteralPath $checkpoint)) { $checkpoint = Join-Path $CandidateDirectory "model.pt" }
    $legacy = (Test-Path -LiteralPath $checkpoint) -and (Test-Path -LiteralPath (Join-Path $CandidateDirectory "tokenizer\tokenizer.json"))
    $six = (Test-Path -LiteralPath (Join-Path $CandidateDirectory "best_model\model.safetensors")) -and
        (Test-Path -LiteralPath (Join-Path $CandidateDirectory "best_model\tokenizer.json")) -and
        (Test-Path -LiteralPath (Join-Path $CandidateDirectory "label_config.json")) -and
        (Test-Path -LiteralPath (Join-Path $CandidateDirectory "manifest.json"))
    return $legacy -or $six
}

function Start-HiddenProcess(
    [string]$Name,
    [string]$FilePath,
    [string]$Arguments,
    [string]$WorkingDirectory,
    [int]$Port,
    [string]$CommandMatch,
    [hashtable]$Environment
) {
    if ($Port -gt 0) {
        $listener = Get-Listener $Port
        if ($listener) {
            $owner = Get-ProcessRecord ([int]$listener.OwningProcess)
            $ownerName = if ($owner) { $owner.Name } else { "unknown process" }
            throw "Port $Port is already occupied by $ownerName (PID $($listener.OwningProcess)); refusing to stop or replace it."
        }
    }

    $savedEnvironment = @{}
    foreach ($key in $Environment.Keys) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
        [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], "Process")
    }
    try {
        $stdout = Join-Path $logDir "$Name.stdout.log"
        $stderr = Join-Path $logDir "$Name.stderr.log"
        $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments `
            -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    }
    finally {
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], "Process")
        }
    }

    $entry = [pscustomobject]@{
        Name = $Name
        Pid = $process.Id
        CreatedAtUtc = $process.StartTime.ToUniversalTime().ToString("o")
        Port = $Port
        CommandMatch = $CommandMatch
        Stdout = $stdout
        Stderr = $stderr
    }
    $script:state.Processes += $entry
    Save-State $script:state
    return $entry
}

function Stop-ManagedProcesses($State) {
    if (-not $State) {
        Write-Output "No managed local-service state file exists; no process was stopped."
        return
    }
    $entries = @($State.Processes)
    [array]::Reverse($entries)
    foreach ($entry in $entries) {
        $owned = @(Get-VerifiedManagedProcesses $entry)
        if ($entry.Port -gt 0) {
            $listener = Get-Listener ([int]$entry.Port)
            if ($listener -and [int]$listener.OwningProcess -notin @($owned | ForEach-Object { [int]$_.ProcessId })) {
                throw "$($entry.Name): port $($entry.Port) is owned by an unverified process; refusing to stop it."
            }
        }
        if ($owned.Count -eq 0) {
            Write-Output "$($entry.Name): previous process exited or PID was reused; no unrelated process was stopped."
            continue
        }
        # Stop verified children before the launcher. Recheck identity immediately
        # before each stop so a process that exited in the meantime is not confused
        # with another process that Windows has assigned the same PID.
        foreach ($record in @($owned | Sort-Object { [int]$_.ProcessId -eq [int]$entry.Pid })) {
            $processId = [int]$record.ProcessId
            $current = Get-ProcessRecord $processId
            if (-not $current) { continue }
            if ($current.CommandLine -ne $record.CommandLine -or
                $current.CreationDate -ne $record.CreationDate) {
                throw "$($entry.Name): PID $processId changed during shutdown; refusing to stop it."
            }
            Stop-Process -Id $processId
            try { Wait-Process -Id $processId -Timeout 10 -ErrorAction Stop }
            catch {
                if (Get-ProcessRecord $processId) {
                    throw "$($entry.Name): PID $processId did not exit after 10 seconds."
                }
            }
        }
        Write-Output "$($entry.Name): stopped verified service processes."
    }
    Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
    Write-Output "Stopped managed processes only. Registry activation was not changed."
}

function Show-Status {
    $state = Read-State
    if (-not $state) {
        Write-Output "No managed local stack is recorded."
    }
    else {
        foreach ($entry in @($state.Processes)) {
            $healthy = Test-ExpectedProcess $entry
            Write-Output ("{0}: {1} (PID {2}, port {3})" -f $entry.Name, `
                $(if ($healthy) { "running" } else { "not running or identity changed" }), `
                $entry.Pid, $entry.Port)
        }
    }

    foreach ($port in @($aiPort, $apiPort, $adminPort)) {
        $listener = Get-Listener $port
        if ($listener) {
            $process = Get-ProcessRecord ([int]$listener.OwningProcess)
            $owner = if ($process) { "$($process.Name) PID $($process.ProcessId)" } else { "unknown process" }
            Write-Output "Port ${port}: listening ($owner)"
        }
        else {
            Write-Output "Port ${port}: closed"
        }
    }
    foreach ($runtimePath in @("health/ready", "health/va/ready", "health/vi/ready")) {
        $runtimeName = switch ($runtimePath) { "health/ready" { "Legacy classifier" }; "health/va/ready" { "English model" }; default { "Vietnamese model" } }
        try {
            $runtime = Invoke-RestMethod -Uri "$($aiUrl)$runtimePath" -TimeoutSec 3
            Write-Output "${runtimeName}: Ready; model=$($runtime.model_version); type=$($runtime.model_type); device=$($runtime.inference_device)"
        }
        catch { Write-Output "${runtimeName}: unavailable or disabled" }
    }
    try {
        $null = Invoke-WebRequest -Uri "$apiUrl/swagger/v1/swagger.json" -TimeoutSec 3 -UseBasicParsing
        Write-Output "Backend: responding at $apiUrl"
    }
    catch {
        Write-Output "Backend: unavailable at $apiUrl"
    }
    Write-Output "Registry activation is an authenticated Admin action; confirm active model in Admin > Models."
}

if ($Action -eq "Status") {
    Show-Status
    exit 0
}

if ($Action -eq "Stop") {
    Stop-ManagedProcesses (Read-State)
    exit 0
}

$existingState = Read-State
if ($existingState) {
    $liveEntries = @($existingState.Processes | Where-Object {
        @(Get-VerifiedManagedProcesses $_).Count -gt 0
    })
    if ($liveEntries.Count -gt 0) {
        $requestedAi = -not $WithoutAI
        if ([bool]$existingState.AiEnabled -ne $requestedAi) {
            throw "A managed stack is already running in a different AI mode. Run -Action Stop before changing modes."
        }
        if (@($existingState.Processes | Where-Object { -not (Test-ExpectedProcess $_) }).Count -eq 0) {
            Write-Output "Managed local stack is already running; no duplicate processes were started."
            Show-Status
            exit 0
        }
        throw "A partial managed stack is present. Run -Action Stop, inspect the service logs, then start again."
    }
    Write-Output "Discarding stale service records; reused PIDs were not stopped."
    Remove-Item -LiteralPath $statePath -Force
}

$state = [pscustomobject]@{
    StartedAt = [DateTime]::UtcNow.ToString("o")
    AiEnabled = -not $WithoutAI
    ApiUrl = $apiUrl
    Processes = @()
}
$script:state = $state

try {
    if ($ListenAddress -notin @("127.0.0.1", "0.0.0.0") -and
        $ListenAddress -notmatch '^(?:\d{1,3}\.){3}\d{1,3}$') {
        throw "ListenAddress must be 127.0.0.1, 0.0.0.0, or an IPv4 address."
    }
    if (-not (Test-NetConnection 127.0.0.1 -Port 27017 -InformationLevel Quiet)) {
        throw "MongoDB is not reachable at 127.0.0.1:27017. Start MongoDB before this local stack."
    }

    $readCheck = Join-Path $root "tests\bin\Debug\net10.0\Moodify.Integration.dll"
    if (-not (Test-Path -LiteralPath $readCheck)) {
        throw "The read-only Mongo preflight is missing. Build tests\Moodify.Integration.csproj first."
    }
    $inventory = & dotnet $readCheck --live-read 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Read-only Mongo preflight failed: $($inventory -join ' ')"
    }
    $inventorySummary = $inventory | Where-Object { "$_" -match '^PASS live MongoDB read-only inventory:' } |
        Select-Object -First 1
    if (-not $inventorySummary -or "$inventorySummary" -notmatch 'softDeleted=0') {
        throw "Refusing backend startup: the read-only inventory did not confirm softDeleted=0. Review Mongo before startup."
    }
    Write-Output $inventorySummary

    foreach ($port in @($apiPort, $adminPort)) {
        if (Get-Listener $port) {
            throw "Port $port is occupied by an unmanaged process. No existing process was stopped."
        }
    }
    if (-not $WithoutAI -and (Get-Listener $aiPort)) {
        throw "Port $aiPort is occupied by an unmanaged process. No existing process was stopped."
    }

    # Fail on merge/build errors before allocating memory to model loading.
    $buildOutput = & dotnet build (Join-Path $backendDir "mood_recommendation.csproj") `
        --configuration Release 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Backend build failed: $($buildOutput -join ' ')"
    }
    $backendDll = Join-Path $backendDir "bin\Release\net10.0\mood_recommendation.dll"
    if (-not (Test-Path -LiteralPath $backendDll)) {
        throw "Backend build succeeded but output DLL is missing: $backendDll"
    }

    if (-not $WithoutAI) {
        $python = Join-Path $aiDir ".venv\Scripts\python.exe"
        $artifact = Join-Path $modelDir "model.safetensors"
        $vaSelection = Get-ModelSelection (Join-Path $stateDir "va-active-model.txt") "roberta_va_regression"
        $viSelection = Get-ModelSelection (Join-Path $stateDir "vi-active-model.txt")
        if ($vaSelection.Version) { $vaModelDir = Join-Path $vaModelRoot $vaSelection.Version }
        $hasEmotionModel = (Test-Path -LiteralPath $artifact) -and -not $WithoutLegacyClassifier
        $hasVaModel = -not $vaSelection.Disabled -and (Test-VaCandidate $vaModelDir $vaSelection.Version)
        if ($vaSelection.Controlled -and -not $vaSelection.Disabled -and -not $hasVaModel) {
            throw "Selected English model artifacts are missing. Restore $($vaSelection.Version); the active selection was not changed."
        }
        $availableCandidates = @(Get-ChildItem -LiteralPath $vaModelRoot -Directory -ErrorAction SilentlyContinue | Where-Object {
            Test-VaCandidate $_.FullName $_.Name
        })
        if (-not (Test-Path -LiteralPath $python) -or (-not $hasEmotionModel -and -not $hasVaModel -and $availableCandidates.Count -eq 0)) {
            throw "The AI virtual environment and model assets are required. Run git lfs pull for the packaged weights; see ai-service\README.md."
        }
        $freeRamMiB = [math]::Floor((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024)
        $minimumRamMiB = if ($hasEmotionModel -and $hasVaModel) { 3500 } elseif ($hasEmotionModel) { 2500 } else { 1800 }
        if ($freeRamMiB -lt $minimumRamMiB) {
            Write-Warning "Only $freeRamMiB MiB physical RAM is free; $minimumRamMiB MiB is recommended for the configured local model(s). Loading may be slow. Close unused apps or use -WithoutAI if necessary; model readiness will still be checked."
        }
        $nvidiaSmi = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
        if ($nvidiaSmi) {
            $freeVramOutput = & $nvidiaSmi.Source --query-gpu=memory.free --format=csv,noheader,nounits 2>$null
            if ($LASTEXITCODE -eq 0 -and $freeVramOutput) {
                $freeVramMiB = @($freeVramOutput | ForEach-Object { [int]("$($_)".Trim()) } |
                    Where-Object { $_ -ge 0 } | Measure-Object -Minimum).Minimum
                $minimumVramMiB = if ($hasEmotionModel -and $hasVaModel) { 3500 } elseif ($hasEmotionModel) { 2500 } else { 1200 }
                if ($null -ne $freeVramMiB -and $freeVramMiB -lt $minimumVramMiB) {
                    Write-Warning "Only $freeVramMiB MiB GPU memory is free; $minimumVramMiB MiB is recommended. Attempting the configured models; startup still requires real readiness and stops managed services on failure."
                }
            }
        }
        if ($hasEmotionModel) {
            $actualHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
            if ($actualHash -ne $modelHash) {
                throw "Model artifact checksum mismatch. Expected $modelHash; found $actualHash."
            }
        }
        $inferenceEnvironment = @{
            AI_MODEL_ENABLED = if ($hasEmotionModel) { "true" } else { "false" }
            VA_MODEL_ENABLED = if ($hasVaModel) { "true" } else { "false" }
            MODEL_SHA256 = $modelHash
            MODEL_DIR = $modelDir
            VA_MODEL_DIR = $vaModelDir
            VA_MODELS_ROOT = $vaModelRoot
            VA_ACTIVE_MODEL_FILE = (Join-Path $stateDir "va-active-model.txt")
            VI_MODEL_ENABLED = "true"
            VI_MODELS_ROOT = $viModelRoot
            VI_ACTIVE_MODEL_FILE = (Join-Path $stateDir "vi-active-model.txt")
        }
        $inferenceEntry = Start-HiddenProcess -Name "inference" -FilePath $python `
            -Arguments "-m uvicorn main:app --host 127.0.0.1 --port $aiPort --workers 1" `
            -WorkingDirectory $aiDir -Port $aiPort -CommandMatch "uvicorn main:app" `
            -Environment $inferenceEnvironment
        Invoke-Readiness "$($aiUrl)health/live" 60 | Out-Null
        if ($hasEmotionModel) {
            $runtime = Invoke-Readiness "$($aiUrl)health/ready" 240 "roberta_large_asl_en_01"
            if ($runtime.artifact_hash -ne $modelHash.ToLowerInvariant()) {
                throw "Inference readiness returned an unexpected artifact checksum."
            }
            Write-Output "Emotion inference ready: $($runtime.model_version), $($runtime.inference_device) ($($runtime.device_name))."
        }
        if ($hasVaModel) {
            $vaRuntime = Invoke-Readiness "$($aiUrl)health/va/ready" 240 $vaSelection.Version
            Write-Output "English inference ready: $($vaRuntime.model_version), $($vaRuntime.inference_device)."
        }
        else {
            Write-Output "English runtime is inactive; stored candidates are available in AI Engineer. No candidate was auto-activated."
        }
        if ($viSelection.Version) {
            $viRuntime = Invoke-Readiness "$($aiUrl)health/vi/ready" 240 $viSelection.Version
            Write-Output "Vietnamese inference ready: $($viRuntime.model_version), $($viRuntime.inference_device)."
        }
        $inferenceListener = Get-Listener $aiPort
        if (-not $inferenceListener) {
            throw "Inference readiness passed, but port $aiPort has no listening process."
        }
        if ([int]$inferenceListener.OwningProcess -ne [int]$inferenceEntry.Pid) {
            $worker = Get-ProcessRecord ([int]$inferenceListener.OwningProcess)
            if (-not $worker -or
                [int]$worker.ParentProcessId -ne [int]$inferenceEntry.Pid -or
                $worker.CommandLine -notlike "*uvicorn main:app*") {
                throw "Inference port is owned by a process outside the recorded service tree."
            }
            $inferenceEntry | Add-Member -NotePropertyName WorkerPid -NotePropertyValue ([int]$worker.ProcessId) -Force
            $inferenceEntry | Add-Member -NotePropertyName WorkerCreatedAtUtc -NotePropertyValue ($worker.CreationDate.ToUniversalTime().ToString("o")) -Force
            Save-State $script:state
        }
    }

    $backendEnvironment = @{
        ASPNETCORE_ENVIRONMENT = "Development"
        ASPNETCORE_URLS = "http://${ListenAddress}:${apiPort}"
        AI__Enabled = if ($WithoutAI) { "false" } else { "true" }
        AI__ServiceUrl = $aiUrl
    }
    $null = Start-HiddenProcess -Name "backend" -FilePath "dotnet" `
        -Arguments "`"$backendDll`"" -WorkingDirectory $backendDir -Port $apiPort `
        -CommandMatch "mood_recommendation.dll" -Environment $backendEnvironment
    $null = Invoke-Readiness "$apiUrl/swagger/v1/swagger.json" 90
    Write-Output "Backend ready at $apiUrl (MongoDB mood_recommendation)."

    $node = (Get-Command node.exe -ErrorAction Stop).Source
    $vite = Join-Path $adminDir "node_modules\vite\bin\vite.js"
    if (-not (Test-Path -LiteralPath $vite)) {
        throw "Admin Web dependencies are missing. Run npm install in admin-web."
    }
    $null = Start-HiddenProcess -Name "admin-web" -FilePath $node `
        -Arguments "`"$vite`" --host 127.0.0.1 --port $adminPort --strictPort" `
        -WorkingDirectory $adminDir -Port $adminPort -CommandMatch "vite.js" `
        -Environment @{ MOODIFY_API_TARGET = $apiUrl }
    $null = Invoke-Readiness "http://127.0.0.1:${adminPort}/" 45
    Write-Output "Admin Web ready at http://127.0.0.1:${adminPort}/."
    if (-not $WithoutAI) {
        Write-Output "Runtime is ready; existing active versions were checked above. Deploy new VI/VA candidates through AI Engineer quality review. The GoEmotions registry is separate."
    }
    else {
        Write-Output "AI inference is disabled for this run; manual/rule-based recommendations remain available."
    }
    Write-Output "Flutter clients can now be started using the commands in docs/AI_IMPLEMENTATION.md."
}
catch {
    $failure = $_
    try { Stop-ManagedProcesses $script:state } catch { Write-Error $_ }
    throw $failure
}
