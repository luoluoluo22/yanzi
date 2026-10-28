param([Parameter(Mandatory=$true)][string]$AppId)
$ErrorActionPreference='Stop'
if ($AppId -notmatch '^asdk_app_[a-zA-Z0-9]+$') { throw 'Use the verified ChatGPT Connection App ID.' }
$projectRoot=Split-Path $PSScriptRoot -Parent
$packageRoot=Join-Path $projectRoot '.runtime/web-package/yanzi'
New-Item (Join-Path $packageRoot 'assets') -ItemType Directory -Force|Out-Null
New-Item (Join-Path $packageRoot 'skills/yanzi') -ItemType Directory -Force|Out-Null
$manifest=@{
    '$schema'='https://agent-plugins.org/schemas/1.0.0/plugin.schema.json'
    name='yanzi';version='0.1.0';description='Yanzi everyday tools: miniapps, Windows apps, devices, and WeChat File Transfer Assistant.'
    author=@{name='Yanzi'}
    extensions=@{'com.openai'=@{
        apps='./.app.json'
        interface=@{displayName='燕子';shortDescription='小程序与日常工具';longDescription='通过本机燕子调用已安装小程序、应用和设备能力，支持明确指定的微信文件传输助手消息。';developerName='Yanzi';category='Productivity';capabilities=@('Interactive');defaultPrompt='检查燕子连接，并列出可使用的小程序。';logo='./assets/yanzi.png';composerIcon='./assets/yanzi.png'}
    }}
}
$files=@{
    'plugin.json'=$manifest
    '.app.json'=@{apps=@{yanzi=@{id=$AppId}}}
    'mcp.json'=@{'$schema'='https://agent-plugins.org/schemas/1.0.0/mcp.schema.json';mcpServers=@{yanzi=@{type='streamable-http';url='https://yanzi-mcp.luoluoluo.cc.cd/mcp'}}}
}
foreach($file in $files.Keys){[IO.File]::WriteAllText((Join-Path $packageRoot $file),($files[$file]|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
Copy-Item (Join-Path $projectRoot 'assets/yanzi.png') (Join-Path $packageRoot 'assets/yanzi.png') -Force
Copy-Item (Join-Path $projectRoot 'skills/yanzi/SKILL.md') (Join-Path $packageRoot 'skills/yanzi/SKILL.md') -Force
$archive=Join-Path $projectRoot '.runtime/yanzi-web-0.1.0.zip'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream=[IO.File]::Open($archive,[IO.FileMode]::Create)
$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($file in Get-ChildItem $packageRoot -File -Force -Recurse){
        $relative=$file.FullName.Substring($packageRoot.Length).TrimStart('\','/').Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,('yanzi/'+$relative))|Out-Null
    }
}finally{$zip.Dispose();$stream.Dispose()}
Write-Output $archive
