param(
    [Parameter(Mandatory = $true)]
    [string]$StagePath,

    [Parameter(Mandatory = $true)]
    [string]$ZipPath
)

# بعضی اوقات بلافاصله از xcopy، آنتی‌ویروس (مخصوصاً Windows Defender) دارد فایل‌های
# .exe/.dll تازه‌کپی‌شده را برای اسکن real-time می‌گیرد و برای چند دهم ثانیه قفلشان
# می‌کند. اگر Compress-Archive در همون لحظه بخواد فایل را بخواند، خطای "being used by
# another process" می‌گیرد. این اسکریپت چند بار با یک مکث کوتاه تلاش می‌کند.

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

$maxAttempts = 6
$delaySeconds = 3

for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    try {
        Compress-Archive -Path (Join-Path $StagePath '*') -DestinationPath $ZipPath -Force -ErrorAction Stop
        Write-Host "Zip created: $ZipPath"
        exit 0
    } catch {
        Write-Host "Attempt $attempt/$maxAttempts failed: $($_.Exception.Message)"
        if (Test-Path $ZipPath) {
            Remove-Item $ZipPath -Force -ErrorAction SilentlyContinue
        }
        if ($attempt -lt $maxAttempts) {
            Start-Sleep -Seconds $delaySeconds
        }
    }
}

Write-Host "ERROR: Could not create zip after $maxAttempts attempts."
Write-Host "This is usually caused by antivirus real-time scanning briefly locking freshly copied .exe/.dll files."
Write-Host "Try running pack.bat again, or temporarily exclude the project/stage folder in Windows Defender while packaging."
exit 1
