<#
.SYNOPSIS
  Profit Hub Execution Sidecar — tails the MT5 terminal journal and posts each
  "order #X ... done in Y ms" entry to the backend so it lands on the matching Trade.

.DESCRIPTION
  Read-only with respect to trading: it only reads the journal log file and makes
  HTTPS POSTs. It never touches MetaTrader, orders, or positions. Idempotent — the
  backend re-sets the same value if a line is sent twice, so restarting is safe.

  One instance per terminal/account (one journal file = one account login). Matching
  is by the closing order ticket the collector EA records on each Trade.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Send-ExecutionTimes.ps1 `
    -ApiUrl "https://profit-hub-service.onrender.com" `
    -IngestKey "<account ingest key>" `
    -LogDir "C:\Users\<you>\AppData\Roaming\MetaQuotes\Terminal\<HASH>\logs" `
    -AccountLogin 27505156
#>
param(
  [Parameter(Mandatory=$true)][string]$ApiUrl,
  [Parameter(Mandatory=$true)][string]$IngestKey,
  [Parameter(Mandatory=$true)][string]$LogDir,
  [int]$AccountLogin = 0,            # optional: only lines for this login ('NNN': ...)
  [int]$IntervalSec = 15
)

$ErrorActionPreference = 'Stop'
$endpoint  = ($ApiUrl.TrimEnd('/')) + '/api/ingest/executions'
$stateFile = Join-Path $env:TEMP ("ph_exec_offset_{0}.txt" -f $AccountLogin)
$rx        = [regex]'order #(\d+)\b.*?done in ([\d.]+) ms'

function Get-Offset { if (Test-Path $stateFile) { [int64](Get-Content $stateFile -Raw) } else { [int64]0 } }
function Set-Offset([int64]$o) { Set-Content -Path $stateFile -Value $o }

Write-Host "[exec-sidecar] posting to $endpoint every ${IntervalSec}s; log dir $LogDir"
$lastFile = ''
while ($true) {
  try {
    $logFile = Join-Path $LogDir ((Get-Date -Format 'yyyyMMdd') + '.log')
    if ($logFile -ne $lastFile) { Set-Offset 0; $lastFile = $logFile }  # new day -> fresh file

    if (Test-Path $logFile) {
      $offset = Get-Offset
      # Open shared so the terminal can keep writing while we read.
      $fs = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
      try {
        if ($offset -gt $fs.Length) { $offset = 0 }   # file rotated/truncated
        [void]$fs.Seek($offset, 'Begin')
        $sr = New-Object System.IO.StreamReader($fs)
        $text = $sr.ReadToEnd()
        $newOffset = $fs.Position
      } finally { $fs.Dispose() }

      $items = @()
      foreach ($line in ($text -split "`n")) {
        if ($AccountLogin -ne 0 -and ($line -notmatch ("'{0}'" -f $AccountLogin))) { continue }
        $m = $rx.Match($line)
        if ($m.Success) {
          $items += [pscustomobject]@{
            orderTicket = [int64]$m.Groups[1].Value
            executionMs = [decimal]$m.Groups[2].Value
          }
        }
      }

      if ($items.Count -gt 0) {
        $body = @{ items = $items } | ConvertTo-Json -Depth 4 -Compress
        try {
          $resp = Invoke-RestMethod -Uri $endpoint -Method Post -Body $body `
                    -ContentType 'application/json' -Headers @{ 'X-Ingest-Key' = $IngestKey } -TimeoutSec 20
          Write-Host ("[exec-sidecar] sent {0}, matched {1}" -f $items.Count, $resp.matched)
          Set-Offset $newOffset      # advance only after a successful POST
        } catch {
          Write-Warning "[exec-sidecar] POST failed: $($_.Exception.Message) (will retry next cycle)"
          # do NOT advance the offset -> re-read the same lines next cycle (backend is idempotent)
        }
      } else {
        Set-Offset $newOffset
      }
    }
  } catch {
    Write-Warning "[exec-sidecar] cycle error: $($_.Exception.Message)"
  }
  Start-Sleep -Seconds $IntervalSec
}
