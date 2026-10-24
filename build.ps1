param([ValidateSet('All','Claude','General')][string]$Edition='All',[string]$OutputPath)
$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(-not (Test-Path -LiteralPath $framework)){$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
$compiler=Join-Path $framework 'csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.8 is required on Windows.'}
if($OutputPath -and $Edition -eq 'All'){throw 'Choose one edition when using OutputPath.'}
$references=@('System.Net.Http.dll','System.Web.Extensions.dll','System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object {'/r:'+(Join-Path $framework $_)}
$references+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll','System.Speech.dll') | ForEach-Object {'/r:'+(Join-Path (Join-Path $framework 'WPF') $_)}
$sourceRoot=Join-Path $PSScriptRoot 'src'
$editions=if($Edition -eq 'All'){@('Claude','General')}else{@($Edition)}
foreach($flavor in $editions){
 $destination=if($OutputPath){[IO.Path]::GetFullPath($OutputPath)}else{Join-Path $PSScriptRoot ('dist\Yuqiao.'+$flavor+'.exe')}
 New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
 if($flavor -eq 'General'){
  $sources=@('GeneralApp.cs','AssemblyInfo.cs','AppRuntime.cs','TranslationService.cs','TranslationPunctuation.cs','VoiceInputService.cs') | ForEach-Object {Join-Path $sourceRoot $_}
  $options=@('/main:Yuqiao.GeneralEntry','/define:GENERAL_EDITION')
 }else{
  $sources=@(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Where-Object {$_.Name -ne 'GeneralApp.cs'} | Sort-Object Name | ForEach-Object {$_.FullName})
  $options=@('/main:Yuqiao.Entry')
 }
 & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /langversion:5 "/out:$destination" "/win32manifest:$sourceRoot\app.manifest" @options @references @sources
 if($LASTEXITCODE -ne 0){throw ($flavor+' build failed.')}
 Get-Item -LiteralPath $destination | Select-Object Name,Length
}
