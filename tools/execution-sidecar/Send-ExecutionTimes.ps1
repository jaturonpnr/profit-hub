<#
.SYNOPSIS
  Profit Hub Execution Sidecar — tails the MT5 terminal journal and posts each
  "order #X ... done in Y ms" entry to the backend so it lands on the matching Trade.

.DESCRIPTION
  Read-only with respect to trading: it only reads the journal log file(s) and makes
  HTTPS POSTs. It never touches MetaTrader, orders, or positions. Idempotent — the
  backend re-sets the same value if a line is sent twice, so restarting is safe.

  By default it follows TODAY's log file only (and rolls to the next day's file at
  midnight). Pass -Backfill to first scan EVERY *.log in the folder once (to fill
  execution time for older trades), then continue following today's file.

  One instance per terminal/account (one journal file = one account login). Matching
  is by the closing order ticket the collector EA records on each Trade.

.EXAMPLE
  # Normal (today onward):
  powershell -ExecutionPolicy Bypass -File Send-ExecutionTimes.ps1 `
    -ApiUrl "https://profit-hub-service.onrender.com" `
    -IngestKey "<account ingest key>" `
    -LogDir "C:\Users\<you>\AppData\Roaming\MetaQuotes\Terminal\<HASH>\logs" `
    -AccountLogin 27505156

.EXAMPLE
  # One-time historical backfill, then keep following:
  powershell -ExecutionPolicy Bypass -File Send-ExecutionTimes.ps1 -Backfill `
    -ApiUrl "..." -IngestKey "..." -LogDir "...\logs" -AccountLogin 27505156
#>
param(
  [Parameter(Mandatory=$true)][string]$ApiUrl,
  [Parameter(Mandatory=$true)][string]$IngestKey,
  [Parameter(Mandatory=$true)][string]$LogDir,
  [int]$AccountLogin = 0,            # optional: only lines for this login ('NNN': ...)
  [int]$IntervalSec = 15,
  [switch]$Backfill                  # scan all *.log once before following today's
)

$ErrorActionPreference = 'Stop'
$endpoint  = ($ApiUrl.TrimEnd('/')) + '/api/ingest/executions'
$stateFile = Join-Path $env:TEMP ("ph_exec_offset_{0}.txt" -f $AccountLogin)
$rx        = [regex]'order #(\d+)\b.*?done in ([\d.]+) ms'
$maxBatch  = 1000                    # well under the backend's 5000 cap

function Get-Offset { if (Test-Path $stateFile) { [int64](Get-Content $stateFile -Raw) } else { [int64]0 } }
function Set-Offset([int64]$o) { Set-Content -Path $stateFile -Value $o }

# Parse journal text into [{orderTicket, executionMs}] items (optionally filtered by login).
function Parse-Lines([string]$text) {
  $out = @()
  foreach ($line in ($text -split "`n")) {
    if ($AccountLogin -ne 0 -and ($line -notmatch ("'{0}'" -f $AccountLogin))) { continue }
    $m = $rx.Match($line)
    if ($m.Success) {
      $out += [pscustomobject]@{
        orderTicket = [int64]$m.Groups[1].Value
        executionMs = [decimal]$m.Groups[2].Value
      }
    }
  }
  return ,$out
}

# POST items in chunks; returns total matched. Throws on HTTP failure (caller decides).
function Send-Items([object[]]$items) {
  $total = 0
  for ($i = 0; $i -lt $items.Count; $i += $maxBatch) {
    $chunk = $items[$i..([Math]::Min($i + $maxBatch - 1, $items.Count - 1))]
    $body  = @{ items = $chunk } | ConvertTo-Json -Depth 4 -Compress
    $resp  = Invoke-RestMethod -Uri $endpoint -Method Post -Body $body `
               -ContentType 'application/json' -Headers @{ 'X-Ingest-Key' = $IngestKey } -TimeoutSec 30
    $total += [int]$resp.matched
  }
  return $total
}

# Read a file fully even while the terminal holds it open for writing.
function Read-Shared([string]$path, [int64]$from = 0) {
  $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
  try {
    if ($from -gt $fs.Length) { $from = 0 }   # rotated/truncated
    [void]$fs.Seek($from, 'Begin')
    $sr = New-Object System.IO.StreamReader($fs)
    return [pscustomobject]@{ Text = $sr.ReadToEnd(); End = $fs.Position }
  } finally { $fs.Dispose() }
}

# --- One-time backfill over every *.log in the folder (oldest first) ---
if ($Backfill) {
  Write-Host "[exec-sidecar] backfill: scanning all *.log in $LogDir"
  $files = Get-ChildItem -Path $LogDir -Filter '*.log' | Sort-Object Name
  $all = @()
  foreach ($f in $files) {
    try { $all += Parse-Lines (Read-Shared $f.FullName).Text }
    catch { Write-Warning "[exec-sidecar] skip $($f.Name): $($_.Exception.Message)" }
  }
  Write-Host ("[exec-sidecar] backfill: {0} execution lines across {1} files" -f $all.Count, $files.Count)
  if ($all.Count -gt 0) {
    try { $m = Send-Items $all; Write-Host "[exec-sidecar] backfill matched $m trades" }
    catch { Write-Warning "[exec-sidecar] backfill POST failed: $($_.Exception.Message)" }
  }
}

# --- Live follow: today's file only, advancing the offset after a successful POST ---
Write-Host "[exec-sidecar] following today's log; posting to $endpoint every ${IntervalSec}s"
$lastFile = ''
while ($true) {
  try {
    $logFile = Join-Path $LogDir ((Get-Date -Format 'yyyyMMdd') + '.log')
    if ($logFile -ne $lastFile) { Set-Offset 0; $lastFile = $logFile }  # new day -> fresh file

    if (Test-Path $logFile) {
      $r = Read-Shared $logFile (Get-Offset)
      $items = Parse-Lines $r.Text
      if ($items.Count -gt 0) {
        try {
          $m = Send-Items $items
          Write-Host ("[exec-sidecar] sent {0}, matched {1}" -f $items.Count, $m)
          Set-Offset $r.End            # advance only after a successful POST
        } catch {
          Write-Warning "[exec-sidecar] POST failed: $($_.Exception.Message) (will retry next cycle)"
          # do NOT advance the offset -> re-read the same lines next cycle (idempotent)
        }
      } else {
        Set-Offset $r.End
      }
    }
  } catch {
    Write-Warning "[exec-sidecar] cycle error: $($_.Exception.Message)"
  }
  Start-Sleep -Seconds $IntervalSec
}
