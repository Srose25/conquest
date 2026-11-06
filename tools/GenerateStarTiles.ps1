Add-Type -AssemblyName System.Drawing

$outputDirectory = Join-Path $PSScriptRoot "..\Sprites\Backgrounds"
$canvasSize = 1024
$safeMargin = 72

$palettes = @(
	@([System.Drawing.Color]::FromArgb(125, 158, 207), [System.Drawing.Color]::FromArgb(182, 166, 140, 217), [System.Drawing.Color]::FromArgb(190, 224, 232, 255)),
	@([System.Drawing.Color]::FromArgb(210, 102, 163, 255), [System.Drawing.Color]::FromArgb(225, 190, 145, 255), [System.Drawing.Color]::FromArgb(235, 236, 232, 255)),
	@([System.Drawing.Color]::FromArgb(255, 104, 180, 255), [System.Drawing.Color]::FromArgb(255, 213, 164, 255), [System.Drawing.Color]::FromArgb(255, 255, 248, 255))
)

function New-Tile([int]$seed, [int]$dotCount, [int]$glareCount, [int]$layerIndex, [string]$name) {
	$bitmap = [System.Drawing.Bitmap]::new($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
	$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
	$graphics.Clear([System.Drawing.Color]::Black)
	$random = [System.Random]::new($seed)
	$palette = $palettes[$layerIndex]

	for ($index = 0; $index -lt $dotCount; $index++) {
		$size = if ($layerIndex -eq 0) { $random.Next(1, 3) } else { $random.Next(1, 4) }
		$x = $random.Next($safeMargin, $canvasSize - $safeMargin - $size)
		$y = $random.Next($safeMargin, $canvasSize - $safeMargin - $size)
		$base = $palette[$random.Next($palette.Count)]
		$alpha = if ($layerIndex -eq 0) { $random.Next(105, 185) } else { $random.Next(135, 225) }
		$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($alpha, $base.R, $base.G, $base.B))
		$graphics.FillRectangle($brush, $x, $y, $size, $size)
		$brush.Dispose()
	}

	for ($index = 0; $index -lt $glareCount; $index++) {
		$coreSize = if ($layerIndex -eq 1) { $random.Next(4, 7) } else { $random.Next(3, 6) }
		$reach = $coreSize * 5
		$x = $random.Next($safeMargin + $reach, $canvasSize - $safeMargin - $reach)
		$y = $random.Next($safeMargin + $reach, $canvasSize - $safeMargin - $reach)
		$base = $palette[$random.Next($palette.Count)]

		# Pixelated circular halo: row widths follow a circle equation, cutting corners.
		$haloCell = [Math]::Max(1, [int]($coreSize / 2))
		$haloRadius = $coreSize * 2
		$radiusCells = [int][Math]::Ceiling($haloRadius / $haloCell)
		for ($cellY = -$radiusCells; $cellY -le $radiusCells; $cellY++) {
			$halfRow = [int][Math]::Floor([Math]::Sqrt($radiusCells * $radiusCells - $cellY * $cellY))
			for ($cellX = -$halfRow; $cellX -le $halfRow; $cellX++) {
				$distance = [Math]::Sqrt(($cellX * $haloCell) * ($cellX * $haloCell) + ($cellY * $haloCell) * ($cellY * $haloCell))
				if ($distance -gt $haloRadius -or $distance -lt ($coreSize / 2)) { continue }
				$fade = 1.0 - ($distance / $haloRadius)
				$alpha = [int](125 * $fade)
				$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($alpha, $base.R, $base.G, $base.B))
				$graphics.FillRectangle($brush, $x + $cellX * $haloCell - ($haloCell / 2), $y + $cellY * $haloCell - ($haloCell / 2), $haloCell, $haloCell)
				$brush.Dispose()
			}
		}

		# Broad, smoothly fading cross that only tapers in its final third.
		$segmentLength = $coreSize / 4.0
		for ($segment = 1; $segment -le 20; $segment++) {
			$fade = 1.0 - ($segment / 21.0)
			$taper = [Math]::Max(0.0, ($segment - 13) / 7.0)
			$thickness = [Math]::Max(1, [int]($coreSize - ($coreSize - 1) * $taper))
			$offset = $coreSize / 2.0 + $segmentLength * ($segment - 0.5)
			$alpha = [int](235 * $fade)
			$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($alpha, $base.R, $base.G, $base.B))
			$graphics.FillRectangle($brush, [int]($x - $thickness / 2), [int]($y - $offset - $segmentLength / 2), $thickness, [int][Math]::Ceiling($segmentLength))
			$graphics.FillRectangle($brush, [int]($x - $thickness / 2), [int]($y + $offset - $segmentLength / 2), $thickness, [int][Math]::Ceiling($segmentLength))
			$graphics.FillRectangle($brush, [int]($x - $offset - $segmentLength / 2), [int]($y - $thickness / 2), [int][Math]::Ceiling($segmentLength), $thickness)
			$graphics.FillRectangle($brush, [int]($x + $offset - $segmentLength / 2), [int]($y - $thickness / 2), [int][Math]::Ceiling($segmentLength), $thickness)
			$brush.Dispose()
		}

		# White-hot cores keep the brightest point of every glare readable above its colored halo.
		$coreBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
		$graphics.FillRectangle($coreBrush, $x - [int]($coreSize / 2), $y - [int]($coreSize / 2), $coreSize, $coreSize)
		$coreBrush.Dispose()
	}

	$path = Join-Path $outputDirectory $name
	$bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
	$graphics.Dispose()
	$bitmap.Dispose()
}

New-Tile 19471 38 0 0 "Stars_Far.png"
New-Tile 59117 44 5 1 "Stars_Mid.png"
New-Tile 92333 46 18 2 "Stars_Near.png"
