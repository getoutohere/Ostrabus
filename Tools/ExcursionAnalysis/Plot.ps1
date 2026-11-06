param([string]$Directory = 'EXCURSION-ANALYSIS')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms.DataVisualization
Add-Type -AssemblyName System.Drawing
$chart = New-Object System.Windows.Forms.DataVisualization.Charting.Chart
$chart.Width = 1500; $chart.Height = 1400; $chart.BackColor = [Drawing.Color]::White
$chart.Titles.Add('O-GXD excursion: Telic Glamour') | Out-Null
function Area($name, $title, $xTitle, $yTitle, $top) {
    $a = New-Object System.Windows.Forms.DataVisualization.Charting.ChartArea($name)
    $a.Position.X = 7; $a.Position.Y = $top; $a.Position.Width = 86; $a.Position.Height = 22
    $a.AxisX.Title = $xTitle; $a.AxisY.Title = $yTitle
    $a.AxisX.LabelStyle.Format = '0.##'; $a.AxisY.LabelStyle.Format = '0.##'
    $a.AxisX.MajorGrid.LineColor = [Drawing.Color]::Gainsboro; $a.AxisY.MajorGrid.LineColor = [Drawing.Color]::Gainsboro
    $a.AxisY.IsStartedFromZero = $false
    $chart.ChartAreas.Add($a)
    $t = $chart.Titles.Add($title); $t.DockedToChartArea = $name; $t.IsDockedInsideChartArea = $false
}
function Series($name, $area, $color, $type) {
    $s = New-Object System.Windows.Forms.DataVisualization.Charting.Series($name)
    $s.ChartArea = $area; $s.ChartType = $type; $s.Color = [Drawing.Color]::FromName($color); $s.BorderWidth = 2
    $chart.Series.Add($s)
}
try {
    Area 'flight' 'Displayed VREL over the flight' 'Elapsed game minutes' 'VREL (m/s)' 5
    Area 'docked' 'While still docked to VORB: same-epoch model gives zero relative velocity' 'Elapsed game seconds' 'Displayed VREL (m/s)' 28
    Area 'handoffs' 'Measured velocity changes on entering analytical coast (11 events)' 'Elapsed game minutes' 'Velocity change (m/s)' 51
    Area 'orbit' 'VORB moves inward and speeds up; captured orbit states match game ephemeris' 'Elapsed game minutes' 'Venus-centred radius (km)' 74
    Series 'flight' 'flight' 'RoyalBlue' 'Line'
    Series 'docked' 'docked' 'DarkOrange' 'Line'
    Series 'handoffs' 'handoffs' 'Firebrick' 'Point'
    $chart.Series['handoffs'].MarkerSize = 9
    Series 'radius' 'orbit' 'DarkGreen' 'Line'
    Series 'speed' 'orbit' 'DarkViolet' 'Line'
    $chart.Series['speed'].YAxisType = 'Secondary'; $chart.ChartAreas['orbit'].AxisY2.Title = 'Speed relative to Venus (km/s)'
    $chart.ChartAreas['orbit'].AxisY2.MajorGrid.Enabled = $false
    foreach ($row in (Import-Csv (Join-Path $Directory 'readouts.csv'))) {
        $time = [double]::Parse($row.gameSeconds, [Globalization.CultureInfo]::InvariantCulture)
        $value = [double]::Parse($row.vrelMPerSecond, [Globalization.CultureInfo]::InvariantCulture)
        $chart.Series['flight'].Points.AddXY($time / 60, $value) | Out-Null
        if ($row.bodyLocked -eq '1' -and $row.referenceBodyId -eq 'VORB') { $chart.Series['docked'].Points.AddXY($time, $value) | Out-Null }
    }
    foreach ($row in (Import-Csv (Join-Path $Directory 'handoffs.csv'))) {
        $chart.Series['handoffs'].Points.AddXY(([double]$row.gameSeconds / 60), [double]$row.deltaVMPerSecond) | Out-Null
    }
    foreach ($row in (Import-Csv (Join-Path $Directory 'vorb-orbit.csv'))) {
        $chart.Series['radius'].Points.AddXY(([double]$row.gameSeconds / 60), [double]$row.radiusKm) | Out-Null
        $chart.Series['speed'].Points.AddXY(([double]$row.gameSeconds / 60), [double]$row.speedKmPerSecond) | Out-Null
    }
    $path = [IO.Path]::GetFullPath((Join-Path $Directory 'excursion.png'))
    $chart.SaveImage($path, [System.Windows.Forms.DataVisualization.Charting.ChartImageFormat]::Png)
    Write-Output $path
} finally { $chart.Dispose() }
