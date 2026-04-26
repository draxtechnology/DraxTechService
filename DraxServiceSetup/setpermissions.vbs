' setpermissions.vbs — installer custom action.
' Ensures C:\ProgramData\DraxTechnology exists and grants Authenticated Users
' (SID *S-1-5-11, locale-independent) Modify permissions with inheritance.
' Idempotent — safe to run on every install/repair.

Option Explicit
Dim sh, fso, folderPath
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

folderPath = sh.ExpandEnvironmentStrings("%ProgramData%") & "\DraxTechnology"

If Not fso.FolderExists(folderPath) Then
    fso.CreateFolder(folderPath)
End If

' (OI)(CI)M = Object Inherit + Container Inherit + Modify
sh.Run "icacls """ & folderPath & """ /grant *S-1-5-11:(OI)(CI)M", 0, True
