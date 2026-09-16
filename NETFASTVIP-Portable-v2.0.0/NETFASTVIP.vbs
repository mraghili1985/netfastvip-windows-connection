Dim fso, shell, scriptDir, exePath
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = scriptDir & "\App\NETFASTVIP.exe"
If fso.FileExists(exePath) Then
    shell.Run """" & exePath & """", 1, False
Else
    MsgBox "NETFASTVIP.exe ???????? ??????:" & vbCrLf & exePath, vbExclamation, "NETFASTVIP"
End If
