param(
    [Parameter(Mandatory = $true)]
    [string]$StagePath
)

# این اسکریپت یک لانچر NETFASTVIP.vbs داخل $StagePath می‌سازد.
# چرا vbs به‌جای .lnk؟ چون بعضی آنتی‌ویروس‌ها فایل‌های .lnk را (به‌خصوص وقتی به یک
# اجرایی داخل پوشه‌ی دیگر اشاره می‌کنند) به‌عنوان الگوی رایج بدافزارهای "shortcut
# dropper" شناسایی و حذف می‌کنند. یک .vbs ساده و بدون obfuscation ریسک بسیار
# کمتری دارد، هیچ پنجره‌ی کنسولی هم باز نمی‌کند (چون با wscript.exe اجرا می‌شود).
# مسیر exe همیشه با WScript.ScriptFullName به‌صورت نسبی محاسبه می‌شود، پس فرقی
# نمی‌کند کاربر زیپ را در چه پوشه‌ای اکسترکت کرده باشد.

$vbsPath = Join-Path $StagePath 'NETFASTVIP.vbs'

$vbsContent = @'
Dim fso, shell, scriptDir, exePath
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = scriptDir & "\App\NETFASTVIP.exe"
If fso.FileExists(exePath) Then
    shell.Run """" & exePath & """", 1, False
Else
    MsgBox "NETFASTVIP.exe پیدا نشد:" & vbCrLf & exePath, vbExclamation, "NETFASTVIP"
End If
'@

Set-Content -Path $vbsPath -Value $vbsContent -Encoding ASCII
Write-Host "Launcher created: $vbsPath"
