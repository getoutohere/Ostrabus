function Find-Ostranauts {
    param([string]$GameDirectory = $env:OSTRANAUTS_DIR)
    if ([string]::IsNullOrWhiteSpace($GameDirectory)) {
        $GameDirectory = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Steam\steamapps\common\Ostranauts'
    }
    $gamePath = [IO.Path]::GetFullPath($GameDirectory)
    if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'Ostranauts_Data\Managed\Assembly-CSharp.dll') -PathType Leaf)) {
        throw 'Ostranauts installation not found. Set OSTRANAUTS_DIR or pass -GameDirectory.'
    }
    return $gamePath
}
