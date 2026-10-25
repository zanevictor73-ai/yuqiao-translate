param([string]$ProductPath=(Join-Path $PSScriptRoot '..\dist\Yuqiao.Claude.exe'),[string]$GeneralPath=(Join-Path $PSScriptRoot '..\dist\Yuqiao.General.exe'))
$ErrorActionPreference='Stop'
$product=(Resolve-Path -LiteralPath $ProductPath).Path
$hash=(Get-FileHash -LiteralPath $product -Algorithm SHA256).Hash
$directory=Join-Path $PSScriptRoot '..\artifacts\tests'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
Copy-Item -LiteralPath $product -Destination (Join-Path $directory ([IO.Path]::GetFileName($product))) -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if(-not(Test-Path -LiteralPath $framework)){$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'}
$compiler=Join-Path $framework 'csc.exe'
$references=@('System.dll','System.Core.dll','System.Xaml.dll','System.Net.Http.dll') | ForEach-Object {'/r:'+(Join-Path $framework $_)}
$references+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object {'/r:'+(Join-Path (Join-Path $framework 'WPF') $_)}
$checks=[ordered]@{ConversationV19Probe=37;AutoFillV18Probe=31;SettingsVoiceV19Probe=24;UiStateProbe=31;FloatingViewsProbe=15;FloatingStateProbe=14;ReadingScrollV191Probe=22;EnglishConversationProbe=29}
$total=0
foreach($name in $checks.Keys){
 $exe=Join-Path $directory ($name+'.exe');$result=Join-Path $directory ($name+'.txt')
 & $compiler /nologo /target:exe /platform:anycpu /langversion:5 "/out:$exe" "/r:$product" @references (Join-Path $PSScriptRoot ($name+'.cs'))
 if($LASTEXITCODE -ne 0){throw ($name+' build failed')}
 [string[]]$arguments=if($name -eq 'FloatingViewsProbe'){@($result)}elseif($name -in @('UiStateProbe','FloatingStateProbe')){@($product,$result)}else{@($product)}
 $lines=@(& $exe @arguments)
 $code=$LASTEXITCODE
 $lines | Set-Content -LiteralPath $result -Encoding UTF8
 $count=@($lines | Where-Object {$_ -match '^PASS[:\s]'}).Count
 if($code -ne 0 -or $count -ne $checks[$name] -or @($lines | Where-Object {$_ -match '^FAIL[:\s]'}).Count){$lines | Write-Output;throw ($name+' failed')}
 $total+=$count;Write-Output ($name+': '+$count+' passed')
}
if((Get-FileHash -LiteralPath $product -Algorithm SHA256).Hash -ne $hash){throw 'Product changed during verification'}
@("Product SHA256: $hash","Result: $total passed; 0 failed") | Set-Content -LiteralPath (Join-Path $directory 'summary.txt') -Encoding UTF8
Write-Output ("Result: $total passed; 0 failed")
$general=(Resolve-Path -LiteralPath $GeneralPath).Path
$generalHash=(Get-FileHash -LiteralPath $general -Algorithm SHA256).Hash
Copy-Item -LiteralPath $general -Destination (Join-Path $directory ([IO.Path]::GetFileName($general))) -Force
$generalTest=Join-Path $directory 'GeneralEditionProbe.exe'
& $compiler /nologo /target:exe /platform:anycpu /langversion:5 "/out:$generalTest" "/r:$general" @references (Join-Path $PSScriptRoot 'GeneralEditionProbe.cs')
if($LASTEXITCODE -ne 0){throw 'General tests did not compile'}
$generalLines=@(& $generalTest $general)
$generalCode=$LASTEXITCODE
$generalLines | Set-Content -LiteralPath (Join-Path $directory 'GeneralEditionProbe.txt') -Encoding UTF8
if($generalCode -ne 0){$generalLines | Write-Output;throw 'General edition failed'}
$generalCount=@($generalLines | Where-Object {$_ -match '^PASS[:\s]'}).Count
if($generalCount -ne 21){throw "Expected 21 general checks, got $generalCount"}
if((Get-FileHash -LiteralPath $general -Algorithm SHA256).Hash -ne $generalHash){throw 'General product changed during verification'}
@("General SHA256: $generalHash","General: $generalCount passed; 0 failed","Total: $($total+$generalCount) passed; 0 failed") | Add-Content -LiteralPath (Join-Path $directory 'summary.txt') -Encoding UTF8
Write-Output ("General: $generalCount passed; total: $($total+$generalCount)")
