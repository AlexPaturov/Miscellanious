$ErrorActionPreference = "Stop"

$healthUrl = "https://localhost/health"
Write-Host "Health check URL: $healthUrl"

$maxAttempts = 12
$delaySec = 10
$timeoutSec = 20

for ($i = 1; $i -le $maxAttempts; $i++) {
    try {
        $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec $timeoutSec
        $code = $response.StatusCode
        Write-Host "Attempt $i/$maxAttempts: HTTP $code"

        if ($code -eq 200) {
            Write-Host "✅ Health check passed."
            exit 0
        }
    }
    catch {
        # Тут окажемся на 503/502/timeout/DNS и т.п.
        Write-Host "Attempt $i/$maxAttempts failed: $($_.Exception.Message)"
    }

    Start-Sleep -Seconds $delaySec
}

Write-Host "##[error]Health check failed: $healthUrl did not return 200"
exit 1
