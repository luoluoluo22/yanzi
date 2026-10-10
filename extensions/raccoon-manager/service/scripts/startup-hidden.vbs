Option Explicit
Dim shell, files, scriptPath, powershell, command, exitCode
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
scriptPath = files.BuildPath(files.GetParentFolderName(WScript.ScriptFullName), "startup.ps1")
powershell = shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
command = """" & powershell & """ -NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & scriptPath & """ -Recover"
' GUI script host launches PowerShell hidden from creation, before argument parsing.
exitCode = shell.Run(command, 0, True)
WScript.Quit exitCode
