param(
  [Parameter(Mandatory=$true)][string]$StateFile,
  [Parameter(Mandatory=$true)][string]$CommandFile
)

$ErrorActionPreference = 'Stop'
$diagnosticFile = "$StateFile.overlay-error.log"
trap {
  try { ($_ | Out-String) | Add-Content -LiteralPath $diagnosticFile -Encoding UTF8 } catch {}
  break
}
"overlay-start $([DateTime]::UtcNow.ToString('o'))" | Write-Output
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -ReferencedAssemblies System.Windows.Forms.dll -TypeDefinition @"
using System.Windows.Forms;
public class RaccoonOverlayForm : Form {
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams {
        get {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }
}
"@

function Write-Command([string]$Action, [string]$LeaseId) {
  $dir = Split-Path -Parent $CommandFile
  New-Item -ItemType Directory -Path $dir -Force | Out-Null
  $tmp = "$CommandFile.$PID.tmp"
  @{ action = $Action; leaseId = $LeaseId; at = (Get-Date).ToString('o') } |
    ConvertTo-Json -Compress |
    Set-Content -LiteralPath $tmp -Encoding UTF8
  Move-Item -LiteralPath $tmp -Destination $CommandFile -Force
}

$form = New-Object RaccoonOverlayForm
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$form.TopMost = $true
$form.ShowInTaskbar = $false
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Size = New-Object System.Drawing.Size(390, 126)
$form.BackColor = [System.Drawing.Color]::FromArgb(31, 33, 36)
$form.Opacity = 0.96
$form.Padding = New-Object System.Windows.Forms.Padding(14, 10, 14, 10)

$working = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.Location = New-Object System.Drawing.Point(($working.Right - $form.Width - 18), ($working.Top + 18))

$title = New-Object System.Windows.Forms.Label
$title.AutoSize = $false
$title.Location = New-Object System.Drawing.Point(14, 10)
$title.Size = New-Object System.Drawing.Size(360, 24)
$title.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 10.5, [System.Drawing.FontStyle]::Bold)
$title.ForeColor = [System.Drawing.Color]::White
$title.Text = '浣熊 · AI 正在操作电脑'
$form.Controls.Add($title)

$task = New-Object System.Windows.Forms.Label
$task.AutoEllipsis = $true
$task.Location = New-Object System.Drawing.Point(14, 38)
$task.Size = New-Object System.Drawing.Size(360, 22)
$task.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)
$task.ForeColor = [System.Drawing.Color]::FromArgb(215, 218, 223)
$form.Controls.Add($task)

$status = New-Object System.Windows.Forms.Label
$status.Location = New-Object System.Drawing.Point(14, 66)
$status.Size = New-Object System.Drawing.Size(170, 30)
$status.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 8.5)
$status.ForeColor = [System.Drawing.Color]::FromArgb(160, 166, 173)
$form.Controls.Add($status)

$pause = New-Object System.Windows.Forms.Button
$pause.Location = New-Object System.Drawing.Point(205, 70)
$pause.Size = New-Object System.Drawing.Size(78, 32)
$pause.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$pause.FlatAppearance.BorderSize = 0
$pause.BackColor = [System.Drawing.Color]::FromArgb(62, 66, 72)
$pause.ForeColor = [System.Drawing.Color]::White
$pause.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 8.5)
$pause.Text = '暂停 AI'
$form.Controls.Add($pause)

$release = New-Object System.Windows.Forms.Button
$release.Location = New-Object System.Drawing.Point(292, 70)
$release.Size = New-Object System.Drawing.Size(82, 32)
$release.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$release.FlatAppearance.BorderSize = 0
$release.BackColor = [System.Drawing.Color]::FromArgb(83, 58, 60)
$release.ForeColor = [System.Drawing.Color]::White
$release.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 8.5)
$release.Text = '释放鼠标'
$form.Controls.Add($release)

$script:leaseId = ''
$script:paused = $false

$pause.Add_Click({
  if ($script:leaseId) { Write-Command 'togglePause' $script:leaseId }
})
$release.Add_Click({
  if ($script:leaseId) { Write-Command 'release' $script:leaseId }
})

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 250
$timer.Add_Tick({
  if (-not (Test-Path -LiteralPath $StateFile)) { return }
  try {
    $state = Get-Content -LiteralPath $StateFile -Raw -Encoding UTF8 | ConvertFrom-Json
  } catch { return }
  if (-not $state.active) {
    $timer.Stop()
    $form.Close()
    return
  }
  try {
    $expires = [DateTime]::Parse([string]$state.expiresAt)
    if ((Get-Date).ToUniversalTime() -gt $expires.ToUniversalTime()) {
      $timer.Stop()
      $form.Close()
      return
    }
  } catch {}

  $script:leaseId = [string]$state.leaseId
  $script:paused = [bool]$state.paused
  $title.Text = if ($script:paused) { '浣熊 · AI 已暂停' } else { '浣熊 · AI 正在操作电脑' }
  $pause.Text = if ($script:paused) { '继续 AI' } else { '暂停 AI' }
  $task.Text = [string]$state.task

  $start = [DateTime]::Parse([string]$state.acquiredAt)
  $elapsed = (Get-Date).ToUniversalTime() - $start.ToUniversalTime()
  $status.Text = "$($state.owner)  ·  $([int]$elapsed.TotalMinutes):$('{0:00}' -f $elapsed.Seconds)"
})

$form.Add_Shown({ 'overlay-shown' | Write-Output; $timer.Start() })
[System.Windows.Forms.Application]::Run($form)
