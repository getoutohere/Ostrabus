[CmdletBinding()]
param(
    [string]$GameDirectory=$env:OSTRANAUTS_DIR,
    [string]$SettingsPath=(Join-Path $env:USERPROFILE 'AppData/LocalLow/Blue Bottle Games/Ostranauts/settings.json'),
    [switch]$Uninstall
)
$ErrorActionPreference='Stop'
$support=Join-Path $PSScriptRoot 'Support'
if(-not (Test-Path -LiteralPath $support)){ $support=Join-Path $PSScriptRoot '../../Build' }
. (Join-Path $support 'Find-Game.ps1')
$gamePath=Find-Ostranauts -GameDirectory $GameDirectory
if(Get-Process -Name Ostranauts -ErrorAction SilentlyContinue){throw 'Close Ostranauts before changing this mod.'}
if(-not $Uninstall){
    if(-not (Test-Path -LiteralPath (Join-Path $gamePath 'BepInEx/core/BepInEx.dll'))){throw 'Install BepInEx 5 first.'}
}
$pluginRoot=Join-Path $gamePath 'BepInEx/plugins/OstrabusBottleRefiller'
$dll=Join-Path $pluginRoot 'Ostrabus.BottleRefiller.dll'
$source=Join-Path $PSScriptRoot 'Plugin/Ostrabus.BottleRefiller.dll'
if(-not (Test-Path -LiteralPath $source)){$source=Join-Path $PSScriptRoot 'bin/Release/netstandard2.1/Ostrabus.BottleRefiller.dll'}
if(-not $Uninstall -and -not (Test-Path -LiteralPath $source)){throw 'Build the Release DLL or use the packaged installer.'}
$orderPath=Join-Path $gamePath 'Ostranauts_Data/Mods/loading_order.json'
if(Test-Path -LiteralPath $SettingsPath){
    $settings=Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    $user=$settings | Where-Object strName -eq UserSettings | Select-Object -First 1
    if(-not [string]::IsNullOrWhiteSpace($user.strPathMods)){$orderPath=Join-Path ([IO.Path]::GetDirectoryName($user.strPathMods)) 'loading_order.json'}
}
$nativeRoot=Join-Path (Split-Path -Parent $orderPath) 'OstrabusBottleRefiller'
if($Uninstall -and -not (Test-Path -LiteralPath (Join-Path $nativeRoot 'mod_info.json'))){
    if(Test-Path -LiteralPath $dll){Move-Item -LiteralPath $dll -Destination ($dll+'.disabled-'+[Guid]::NewGuid().ToString('N'))}
    Write-Host 'Bottle Refiller plugin removed; no native installation was found.';return
}
$assets=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'Assets')).ProviderPath
$targets=@($dll,$orderPath,(Join-Path $nativeRoot 'data/loot/supply_stock.json'))
if(-not $Uninstall){$targets+=@(Get-ChildItem -LiteralPath $assets -Recurse -File | ForEach-Object {Join-Path $nativeRoot $_.FullName.Substring($assets.Length).TrimStart('\','/')})}
$backupRoot=Join-Path $pluginRoot ('Backups/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$snapshots=@(foreach($target in @($targets | Select-Object -Unique)){
    $exists=Test-Path -LiteralPath $target -PathType Leaf
    $backup=Join-Path $backupRoot ([Guid]::NewGuid().ToString('N')+'.bak')
    if($exists){Copy-Item -LiteralPath $target -Destination $backup}
    [pscustomobject]@{path=$target;existed=$exists;backup=$backup}
})
try{
    if($Uninstall){
        if(Test-Path -LiteralPath $dll){Remove-Item -LiteralPath $dll}
    }else{
        & (Join-Path $support 'Install-NativeMod.ps1') -ModName OstrabusBottleRefiller -AssetSource $assets -GameDirectory $gamePath -SettingsPath $SettingsPath -Enable
        Copy-Item -LiteralPath $source -Destination $dll -Force
        if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $dll).Hash){throw 'Plugin copy verification failed.'}
    }
    & (Join-Path $PSScriptRoot 'Update-Stock.ps1') -GameDirectory $gamePath -OrderPath $orderPath -RemoveStock:$Uninstall
}catch{
    $failure=$_
    foreach($snapshot in $snapshots){
        if($snapshot.existed){Copy-Item -LiteralPath $snapshot.backup -Destination $snapshot.path -Force}
        elseif(Test-Path -LiteralPath $snapshot.path -PathType Leaf){Remove-Item -LiteralPath $snapshot.path}
    }
    throw $failure
}
if($Uninstall){Write-Host 'Refilling disabled and future stock removed. Native definitions remain enabled for saved equipment and bottles.'}
else{Write-Host 'Bottle Refiller installed. Supply kiosks receive one loose unit on their next native restock. New units start OFF.'}
