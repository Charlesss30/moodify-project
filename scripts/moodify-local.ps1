param(
    [ValidateSet("Start", "Status", "Stop")]
    [string]$Action = "Status",
    [switch]$WithoutAI,
    [string]$ListenAddress = "127.0.0.1"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$backendDir = Join-Path $root "backend"
$adminDir = Join-Path $root "admin-web"
$aiDir = Join-Path $root "ai-service"
$modelDir = Join-Path $root "code-train\train-KLCN\output\roberta_large_asl_en_01\best_model"
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

function Test-ExpectedProcess($Entry) {
    $process = Get-ProcessRecord ([int]$Entry.Pid)
    if (-not $process -or $process.CommandLine -notlike "*$($Entry.CommandMatch)*") {
        return $false
    }
    if ($Entry.Port -gt 0) {
        $listener = Get-Listener ([int]$Entry.Port)
        if (-not $listener) { return $false }
        if ([int]$listener.OwningProcess -eq [int]$Entry.Pid) { return $true }
        $worker = Get-ProcessRecord ([int]$listener.OwningProcess)
        return $null -ne $worker -and
            [int]$worker.ParentProcessId -eq [int]$Entry.Pid -and
            $worker.CommandLine -like "*$($Entry.CommandMatch)*"
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
        $process = Get-ProcessRecord ([int]$entry.Pid)
        if ($process -and $process.CommandLine -notlike "*$($entry.CommandMatch)*") {
            throw "$($entry.Name): PID $($entry.Pid) no longer matches the recorded executable; refusing to stop it."
        }
        $processIds = @()
        if ($entry.PSObject.Properties["WorkerPid"] -and [int]$entry.WorkerPid -ne [int]$entry.Pid) {
            $recordedWorker = Get-ProcessRecord ([int]$entry.WorkerPid)
            if ($recordedWorker -and
                ([int]$recordedWorker.ParentProcessId -ne [int]$entry.Pid -or
                 $recordedWorker.CommandLine -notlike "*$($entry.CommandMatch)*")) {
                throw "$($entry.Name): recorded worker PID $($entry.WorkerPid) no longer matches its verified service parent; refusing to stop it."
            }
            if ($recordedWorker) { $processIds += [int]$recordedWorker.ProcessId }
        }
        if ($entry.Port -gt 0) {
            $listener = Get-Listener ([int]$entry.Port)
            if ($listener) {
                if ([int]$listener.OwningProcess -eq [int]$entry.Pid) {
                    if (-not $process) {
                        throw "$($entry.Name): port $($entry.Port) is owned by an unverifiable recorded PID; refusing to stop it."
                    }
                }
                else {
                    $worker = Get-ProcessRecord ([int]$listener.OwningProcess)
                    if (-not $worker -or
                        [int]$worker.ParentProcessId -ne [int]$entry.Pid -or
                        $worker.CommandLine -notlike "*$($entry.CommandMatch)*") {
                        throw "$($entry.Name): port $($entry.Port) has changed to an unowned process; refusing to stop it."
                    }
                    if ($processIds -notcontains [int]$worker.ProcessId) {
                        $processIds += [int]$worker.ProcessId
                    }
                }
            }
        }
        if ($process) {
            $processIds += [int]$entry.Pid
        }
        elseif ($processIds.Count -eq 0) {
            Write-Output "$($entry.Name): process already exited."
            continue
        }
        foreach ($processId in $processIds) {
            if (Get-ProcessRecord $processId) {
                Stop-Process -Id $processId
                try {
                    Wait-Process -Id $processId -Timeout 10 -ErrorAction Stop
                }
                catch {
                    if (Get-ProcessRecord $processId) {
                        throw "$($entry.Name): PID $processId did not exit after 10 seconds."
                    }
                }
            }
        }
        Write-Output "$($entry.Name): stopped PID $($entry.Pid)."
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
    try {
        $runtime = Invoke-RestMethod -Uri "$($aiUrl)health/ready" -TimeoutSec 3
        Write-Output "Inference: Ready; model=$($runtime.model_version); device=$($runtime.inference_device) ($($runtime.device_name))"
    }
    catch {
        Write-Output "Inference: unavailable"
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
        $null -ne (Get-ProcessRecord ([int]$_.Pid))
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

    if (-not $WithoutAI) {
        $python = Join-Path $aiDir ".venv\Scripts\python.exe"
        $artifact = Join-Path $modelDir "model.safetensors"
        if (-not (Test-Path -LiteralPath $python) -or -not (Test-Path -LiteralPath $artifact)) {
            throw "The AI virtual environment or model artifact is missing. See ai-service\README.md."
        }
        $freeRamMiB = [math]::Floor((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024)
        if ($freeRamMiB -lt 2500) {
            throw "Only $freeRamMiB MiB physical RAM is free; at least 2500 MiB is required for this local model."
        }
        $nvidiaSmi = Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
        if ($nvidiaSmi) {
            $freeVramOutput = & $nvidiaSmi.Source --query-gpu=memory.free --format=csv,noheader,nounits 2>$null
            if ($LASTEXITCODE -eq 0 -and $freeVramOutput) {
                $freeVramMiB = @($freeVramOutput | ForEach-Object { [int]("$($_)".Trim()) } |
                    Where-Object { $_ -ge 0 } | Measure-Object -Minimum).Minimum
                if ($null -ne $freeVramMiB -and $freeVramMiB -lt 2500) {
                    throw "Only $freeVramMiB MiB GPU memory is free; at least 2500 MiB is required for the tested CUDA model."
                }
            }
        }
        $actualHash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
        if ($actualHash -ne $modelHash) {
            throw "Model artifact checksum mismatch. Expected $modelHash; found $actualHash."
        }
        $inferenceEntry = Start-HiddenProcess -Name "inference" -FilePath $python `
            -Arguments "-m uvicorn main:app --host 127.0.0.1 --port $aiPort --workers 1" `
            -WorkingDirectory $aiDir -Port $aiPort -CommandMatch "uvicorn main:app" `
            -Environment @{
                AI_MODEL_ENABLED = "true"
                MODEL_SHA256 = $modelHash
                MODEL_DIR = $modelDir
            }
        $runtime = Invoke-Readiness "$($aiUrl)health/ready" 240 "roberta_large_asl_en_01"
        if ($runtime.artifact_hash -ne $modelHash.ToLowerInvariant()) {
            throw "Inference readiness returned an unexpected artifact checksum."
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
            Save-State $script:state
        }
        Write-Output "Inference ready: $($runtime.model_version), $($runtime.inference_device) ($($runtime.device_name))."
    }

    $buildOutput = & dotnet build (Join-Path $backendDir "mood_recommendation.csproj") `
        --no-restore --configuration Release 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Backend build failed: $($buildOutput -join ' ')"
    }
    $backendDll = Join-Path $backendDir "bin\Release\net10.0\mood_recommendation.dll"
    if (-not (Test-Path -LiteralPath $backendDll)) {
        throw "Backend build succeeded but output DLL is missing: $backendDll"
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
        Write-Output "Runtime is ready, but registry activation is separate. Sign in as Admin/AIEngineer, validate the candidate and activate it in Admin > Models."
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
