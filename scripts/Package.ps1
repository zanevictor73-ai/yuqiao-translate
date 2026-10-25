param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(-not $SkipBuild){& (Join-Path $root 'build.ps1'); & (Join-Path $root 'tests\Run-Tests.ps1')}
$version='1.10.0'
$release=Join-Path $root 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveHashes=@()
foreach($edition in @('Claude','General')){
 $exe=Join-Path $root ('dist\Yuqiao.'+$edition+'.exe')
 if(-not(Test-Path -LiteralPath $exe)){throw "Missing $edition executable; run build.ps1 first"}
 if([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -ne ($version+'.0')){throw 'Executable version mismatch'}
 $name='Yuqiao-'+$edition+'-v'+$version+'-windows'
 $folder=Join-Path $release $name
 New-Item -ItemType Directory -Path $folder -Force | Out-Null
 $files=@(
  @{Source=$exe;Name=('Yuqiao.'+$edition+'.exe')},
  @{Source=(Join-Path $root 'LICENSE');Name='LICENSE'},
  @{Source=(Join-Path $root 'docs\QUICKSTART.zh-CN.md');Name='Quick-start.zh-CN.md'},
  @{Source=(Join-Path $root 'docs\QUICKSTART.en.md');Name='Quick-start.en.md'}
 )
 foreach($file in $files){Copy-Item -LiteralPath $file.Source -Destination (Join-Path $folder $file.Name) -Force}
 # Explicit members prevent local logs/preferences from entering a release.
 $manifest=@($files | ForEach-Object {$_.Name})
 $checksums=@($manifest | ForEach-Object {((Get-FileHash -LiteralPath (Join-Path $folder $_) -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$_})
 [IO.File]::WriteAllLines((Join-Path $folder 'SHA256SUMS.txt'),$checksums,(New-Object Text.UTF8Encoding($false)))
 $manifest+='SHA256SUMS.txt'
 $zip=Join-Path $release ($name+'.zip')
 if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
 $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($member in $manifest){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $folder $member),($name+'/'+$member),[IO.Compression.CompressionLevel]::Optimal) | Out-Null}}
 finally{$archive.Dispose()}
 $archiveHashes+=((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+[IO.Path]::GetFileName($zip)
}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$archiveHashes,(New-Object Text.UTF8Encoding($false)))
$archiveHashes
