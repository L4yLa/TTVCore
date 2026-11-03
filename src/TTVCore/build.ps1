param(
    [string]$Configuration = "Release",
    # ビルドするゲームの版。それぞれ Platform "BS<版>" になる。
    [string[]]$Targets = @("1.40.8", "1.44.1"),
    # -Debug だけで Debug 構成にする（-Configuration Debug と同じ）
    [switch]$Debug
)

if ($Debug) { $Configuration = "Debug" }

$ErrorActionPreference = "Continue"
Set-Location -Path $PSScriptRoot

# MSBuild / dotnet の出力は UTF-8。日本語のメッセージが化けないよう UTF-8 として読む。
$prevOutputEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$proj     = "TTVCore.csproj"
$assembly = "TTVCore"
$log      = Join-Path $PSScriptRoot "build.log"
$release  = Join-Path $PSScriptRoot "release"

# 配布用のアーカイブ（release\<id>_v<version>_bs<gameVersion>.7z と .zip）は Release のときだけ作る。
$doPackage = ($Configuration -eq "Release")

Write-Host "============================================================"
Write-Host " TTVCore - Build ($Configuration)"
Write-Host " Targets: $($Targets -join ', ')"
Write-Host "============================================================"

# .NET SDK を含まない MSBuild は Microsoft.NET.Sdk を解決できない。
# 該当するものがなければ dotnet build を使う。
$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $found = & $vswhere -latest -prerelease -products * `
                        -requires Microsoft.Component.MSBuild Microsoft.NetCore.Component.SDK `
                        -find "MSBuild\**\Bin\MSBuild.exe" 2>$null
    if ($found) { $msbuild = @($found)[0] }
}

if (-not $msbuild -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    [Console]::OutputEncoding = $prevOutputEncoding
    Write-Host "[ERROR] Neither MSBuild nor dotnet was found."
    Write-Host "        Install Visual Studio Build Tools or the .NET SDK."
    exit 1
}

if ($msbuild) {
    Write-Host "[INFO] MSBuild: $msbuild"
}
else {
    Write-Host "[INFO] MSBuild not found. Falling back to 'dotnet build'."
}

# 7-Zip は PATH と Program Files の下から探す。zip も 7-Zip で作る。
$sevenZip = $null
if ($doPackage) {
    $cmd = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        $sevenZip = $cmd.Source
    }
    else {
        foreach ($dir in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
            if (-not $dir) { continue }
            $candidate = Join-Path $dir "7-Zip\7z.exe"
            if (Test-Path $candidate) { $sevenZip = $candidate; break }
        }
    }

    if ($sevenZip) {
        Write-Host "[INFO] 7-Zip: $sevenZip"
    }
    else {
        Write-Host "[WARN] 7z.exe was not found (PATH, Program Files\7-Zip). Release packages will not be created." -ForegroundColor Yellow
    }
}

# release\<id>_v<version>_bs<gameVersion>.7z と .zip を作る。中身は Plugins\<assembly>.dll。
# 戻り値は @{ Ok; Archives; Message; Log }。
function New-ReleasePackage([string]$Target) {
    $result = @{ Ok = $false; Archives = @(); Message = $null; Log = @() }

    if (-not $sevenZip) {
        $result.Message = "7z.exe not found"
        return $result
    }

    $manifestPath = Join-Path $PSScriptRoot "Manifests\$Target\manifest.json"
    try {
        $manifest = Get-Content -Path $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        $result.Message = "Cannot read $manifestPath : $($_.Exception.Message)"
        return $result
    }

    if (-not $manifest.id -or -not $manifest.version -or -not $manifest.gameVersion) {
        $result.Message = "id / version / gameVersion is missing in $manifestPath"
        return $result
    }
    if ($manifest.gameVersion -ne $Target) {
        $result.Message = "gameVersion '$($manifest.gameVersion)' in $manifestPath does not match target $Target"
        return $result
    }

    $dll = Join-Path $PSScriptRoot "bin\$Target\$Configuration\$assembly.dll"
    if (-not (Test-Path $dll)) {
        $result.Message = "Build output not found: $dll"
        return $result
    }

    # アーカイブの中の配置: <stage>\Plugins\<assembly>.dll
    $stage   = Join-Path $PSScriptRoot "obj\$Target\package"
    $plugins = Join-Path $stage "Plugins"
    if (Test-Path $stage) { Remove-Item -Path $stage -Recurse -Force }
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    Copy-Item -Path $dll -Destination $plugins

    New-Item -ItemType Directory -Path $release -Force | Out-Null
    $baseName = "{0}_v{1}_bs{2}" -f $manifest.id, $manifest.version, $manifest.gameVersion

    foreach ($format in @(@{ Ext = "7z"; Args = @("-t7z", "-mx=9") }, @{ Ext = "zip"; Args = @("-tzip", "-mx=9") })) {
        $archive = Join-Path $release ("{0}.{1}" -f $baseName, $format.Ext)

        # 7z a は既存のアーカイブに追加するため、先に消す
        if (Test-Path $archive) { Remove-Item -Path $archive -Force }

        Push-Location $stage
        try {
            $result.Log += & $sevenZip a @($format.Args) $archive "Plugins" 2>&1
            $code = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }

        if ($code -ne 0) {
            $result.Message = "7z exited with code $code ($($format.Ext))"
            return $result
        }
        $result.Archives += $archive
    }

    $result.Ok = $true
    return $result
}

$output   = @()
$results  = [ordered]@{}
$packages = [ordered]@{}

try {
    foreach ($target in $Targets) {
        $platform = "BS$target"
        Write-Host ""
        Write-Host "------------------------------------------------------------"
        Write-Host " [$target] Platform=$platform"
        Write-Host "------------------------------------------------------------"
        $output += "==================== [$target] Platform=$platform ===================="

        $rc = 0
        if ($msbuild) {
            Write-Host "[INFO] [$target] Restoring NuGet packages..."
            $output += & $msbuild $proj /t:Restore /p:Configuration=$Configuration /p:Platform=$platform /v:minimal /nologo 2>&1
            $rc = $LASTEXITCODE

            if ($rc -eq 0) {
                Write-Host "[INFO] [$target] Building..."
                $output += & $msbuild $proj /t:Build /p:Configuration=$Configuration /p:Platform=$platform /v:normal /nologo 2>&1
                $rc = $LASTEXITCODE
            }
        }
        else {
            Write-Host "[INFO] [$target] Building with dotnet..."
            $output += & dotnet build $proj -c $Configuration -p:Platform=$platform -v normal 2>&1
            $rc = $LASTEXITCODE
        }

        $results[$target] = $rc
        if ($rc -eq 0) {
            Write-Host "[OK] [$target] -> bin\$target\$Configuration\" -ForegroundColor Green

            if ($doPackage) {
                Write-Host "[INFO] [$target] Packaging..."
                $pkg = New-ReleasePackage -Target $target
                $output += "---- [$target] Package ----"
                $output += $pkg.Log
                $packages[$target] = $pkg
                if ($pkg.Ok) {
                    foreach ($archive in $pkg.Archives) {
                        Write-Host "[OK] [$target] -> release\$(Split-Path $archive -Leaf)" -ForegroundColor Green
                    }
                }
                else {
                    $output += "[PACKAGE FAILED] $($pkg.Message)"
                    Write-Host "[PACKAGE FAILED] [$target] $($pkg.Message)" -ForegroundColor Red
                }
            }
        }
        else {
            Write-Host "[FAILED] [$target] (exit code $rc)" -ForegroundColor Red
        }
    }
}
finally {
    [Console]::OutputEncoding = $prevOutputEncoding
}

$output | Out-File -FilePath $log -Encoding utf8

$failed    = @($results.Keys | Where-Object { $results[$_] -ne 0 })
$pkgFailed = @($packages.Keys | Where-Object { -not $packages[$_].Ok })

Write-Host ""
Write-Host "============================================================"
Write-Host " Summary"
Write-Host "============================================================"
foreach ($target in $results.Keys) {
    if ($results[$target] -eq 0) {
        Write-Host "  [$target] OK      bin\$target\$Configuration\$assembly.dll" -ForegroundColor Green
        if ($packages.Contains($target)) {
            $pkg = $packages[$target]
            if ($pkg.Ok) {
                foreach ($archive in $pkg.Archives) {
                    Write-Host "  [$target] PACKAGE release\$(Split-Path $archive -Leaf)" -ForegroundColor Green
                }
            }
            else {
                Write-Host "  [$target] PACKAGE FAILED  ($($pkg.Message))" -ForegroundColor Red
            }
        }
    }
    else {
        Write-Host "  [$target] FAILED  (exit code $($results[$target]))" -ForegroundColor Red
    }
}

$warnings = $output | Select-String -Pattern "warning" -SimpleMatch
if ($warnings) {
    Write-Host ""
    Write-Host "---- Warnings ----"
    $warnings | ForEach-Object { Write-Host $_ }
}

if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "---- Errors ----"
    $output | Select-String -Pattern "error" -SimpleMatch | ForEach-Object { Write-Host $_ }
}

Write-Host ""
Write-Host "Full log: $log"

if ($failed.Count -gt 0 -or $pkgFailed.Count -gt 0) { exit 1 }
exit 0
