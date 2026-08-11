Option Explicit

Dim shell, fso, root, script, command

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
script = fso.BuildPath(root, "LAPTOP QA\App\Start Laptop QA Local.ps1")

If Not fso.FileExists(script) Then
    script = fso.BuildPath(root, "Start Laptop QA Local.ps1")
End If

If Not fso.FileExists(script) Then
    MsgBox "Laptop QA startup script was not found:" & vbCrLf & vbCrLf & script, vbCritical, "Laptop QA"
    WScript.Quit 1
End If

command = "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File " & Chr(34) & script & Chr(34)
shell.Run command, 0, False
