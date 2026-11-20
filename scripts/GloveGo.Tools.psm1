Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:Project = Join-Path $script:Root 'src/GloveGo.App/GloveGo.App.csproj'
$script:Sdk = Join-Path $env:LOCALAPPDATA 'Android/Sdk'
if ($env:ANDROID_HOME) { $script:Sdk = $env:ANDROID_HOME }
$script:Adb = Join-Path $script:Root 'tools/platform-tools/adb.exe'

function Fail([string]$Message) { throw $Message }
function Require-File([string]$Path, [string]$Hint) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { Fail "$Hint`nMissing: $Path" }
}
function Find-Tool([string]$Name, [string[]]$Candidates = @()) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    foreach ($candidate in $Candidates) { if (Test-Path -LiteralPath $candidate -PathType Leaf -ErrorAction SilentlyContinue) { return $candidate } }
    return $null
}
function Find-Vs {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -version '[18.0,19.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($LASTEXITCODE -eq 0 -and $path) { return ($path | Select-Object -First 1) }
    }
    $base = Join-Path $env:ProgramFiles 'Microsoft Visual Studio'
    $versions = Get-ChildItem $base -Directory -ErrorAction SilentlyContinue
    foreach ($version in $versions | Sort-Object Name -Descending) {
        $edition = Get-ChildItem $version.FullName -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($edition -and (Test-Path (Join-Path $edition.FullName 'VC/Tools/MSVC'))) { return $edition.FullName }
    }
    return $null
}
function Find-Cmake {
    $vs = Find-Vs
    $candidates = @()
    if ($vs) { $candidates += (Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe') }
    $base = Join-Path $env:ProgramFiles 'Microsoft Visual Studio'
    $candidates += Get-ChildItem $base -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem $_.FullName -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            Join-Path $_.FullName 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
        }
    }
    return Find-Tool 'cmake.exe' $candidates
}
function Find-Ninja {
    $vs = Find-Vs
    $candidates = @()
    if ($vs) { $candidates += (Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe') }
    $base = Join-Path $env:ProgramFiles 'Microsoft Visual Studio'
    $candidates += Get-ChildItem $base -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem $_.FullName -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            Join-Path $_.FullName 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe'
        }
    }
    return Find-Tool 'ninja.exe' $candidates
}
function Find-Ndk {
    $parent = Join-Path $script:Sdk 'ndk'
    $ndk = Get-ChildItem $parent -Directory -ErrorAction SilentlyContinue | Where-Object Name -Like '27.*' | Sort-Object Name -Descending | Select-Object -First 1
    if ($ndk) { return $ndk.FullName }
    return $null
}
function Find-JavaHome {
    if ($env:JAVA_HOME -and (Test-Path (Join-Path $env:JAVA_HOME 'bin/java.exe'))) { return $env:JAVA_HOME }
    $local = Join-Path $script:Root 'tools/jdk'
    $bundled = Get-ChildItem $local -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName 'bin/java.exe') } | Sort-Object Name -Descending | Select-Object -First 1
    if ($bundled) { return $bundled.FullName }
    return $null
}
function Assert-Prerequisites([string]$Platform) {
    if (-not (Find-Tool 'dotnet.exe')) { Fail '.NET 10 SDK is missing.' }
    if (-not (Find-Cmake)) { Fail 'CMake is missing.' }
    if (-not (Test-Path (Join-Path $script:Root 'vendor/opencv/CMakeLists.txt'))) { Fail 'OpenCV 4.12 source is missing from vendor/opencv.' }
    if ($Platform -eq 'windows' -and -not (Find-Vs)) { Fail 'Visual Studio 2026 C++ build tools are missing.' }
    if ($Platform -eq 'android') {
        if (-not (Find-JavaHome)) { Fail 'Java 17 is missing. Set JAVA_HOME to a JDK 17 installation.' }
        if (-not (Find-Ndk)) { Fail 'Android NDK r27 is missing.' }
        if (-not (Find-Ninja)) { Fail 'Ninja is missing.' }
        if (-not (Test-Path (Join-Path $script:Sdk 'platforms/android-36'))) { Fail 'Android SDK API 36 is missing.' }
    }
}
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "$Program failed with exit code $LASTEXITCODE" }
}
function Build-Native([string]$Platform) {
    $cmake = Find-Cmake
    $opencvSource = Join-Path $script:Root 'vendor/opencv'
    if ($Platform -eq 'windows') {
        $vs = Find-Vs
        $opencvBuild = Join-Path $script:Root 'build/opencv-win'
        $nativeBuild = Join-Path $script:Root 'build/native-win'
        $vsVersion = Split-Path (Split-Path $vs -Parent) -Leaf
        $generator = if ($vsVersion -eq '18') { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
        if (-not (Test-Path (Join-Path $opencvBuild 'CMakeCache.txt'))) {
            Invoke-Checked $cmake @('-S', $opencvSource, '-B', $opencvBuild, '-G', $generator, '-A', 'x64', '-DBUILD_LIST=core,imgproc,calib3d', '-DBUILD_SHARED_LIBS=OFF', '-DBUILD_TESTS=OFF', '-DBUILD_PERF_TESTS=OFF', '-DBUILD_EXAMPLES=OFF', '-DBUILD_JAVA=OFF')
        }
        Invoke-Checked $cmake @('--build', $opencvBuild, '--config', 'Release', '--target', 'opencv_core', 'opencv_imgproc', 'opencv_calib3d')
        if (-not (Test-Path (Join-Path $nativeBuild 'CMakeCache.txt'))) {
            Invoke-Checked $cmake @('-S', (Join-Path $script:Root 'native'), '-B', $nativeBuild, '-G', $generator, '-A', 'x64', "-DOpenCV_DIR=$opencvBuild")
        }
        Invoke-Checked $cmake @('--build', $nativeBuild, '--config', 'Release')
        Require-File (Join-Path $nativeBuild 'Release/glovego_vision.dll') 'Windows native build did not produce its DLL.'
    } else {
        $ndk = Find-Ndk
        $opencvBuild = Join-Path $script:Root 'build/opencv-android-arm64'
        $nativeBuild = Join-Path $script:Root 'build/native-android'
        $toolchain = Join-Path $ndk 'build/cmake/android.toolchain.cmake'
        $ninja = Find-Ninja
        if (-not (Test-Path (Join-Path $opencvBuild 'CMakeCache.txt'))) {
            Invoke-Checked $cmake @('-S', $opencvSource, '-B', $opencvBuild, '-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$ninja", "-DCMAKE_TOOLCHAIN_FILE=$toolchain", '-DANDROID_ABI=arm64-v8a', '-DANDROID_PLATFORM=android-23', '-DCMAKE_BUILD_TYPE=Release', '-DBUILD_LIST=core,imgproc,calib3d', '-DBUILD_SHARED_LIBS=OFF', '-DBUILD_TESTS=OFF', '-DBUILD_PERF_TESTS=OFF', '-DBUILD_ANDROID_EXAMPLES=OFF', '-DBUILD_JAVA=OFF')
        }
        Invoke-Checked $cmake @('--build', $opencvBuild, '--target', 'opencv_core', 'opencv_imgproc', 'opencv_calib3d')
        if (-not (Test-Path (Join-Path $nativeBuild 'CMakeCache.txt'))) {
            Invoke-Checked $cmake @('-S', (Join-Path $script:Root 'native'), '-B', $nativeBuild, '-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$ninja", "-DCMAKE_TOOLCHAIN_FILE=$toolchain", '-DANDROID_ABI=arm64-v8a', '-DANDROID_PLATFORM=android-23', '-DCMAKE_BUILD_TYPE=Release', "-DOpenCV_DIR=$opencvBuild")
        }
        Invoke-Checked $cmake @('--build', $nativeBuild)
        Require-File (Join-Path $nativeBuild 'libglovego_vision.so') 'Android native build did not produce its library.'
    }
}
function Invoke-GloveBuild([string]$Platform = 'all') {
    $targets = if ($Platform -eq 'all') { @('windows', 'android') } else { @($Platform) }
    # A normal restore uses NuGet; cached packages remain usable when a source is temporarily offline.
    Invoke-Checked 'dotnet' @('restore', $script:Project, '--ignore-failed-sources', '-p:NuGetAudit=false')
    foreach ($target in $targets) {
        Assert-Prerequisites $target
        Build-Native $target
        $framework = if ($target -eq 'windows') { 'net10.0-windows10.0.19041.0' } else { 'net10.0-android' }
        $args = @('build', $script:Project, '-f', $framework, '-c', 'Debug', '--no-restore')
        if ($target -eq 'android') { $args += '-p:RuntimeIdentifier=android-arm64'; $args += "-p:AndroidSdkDirectory=$script:Sdk"; $args += "-p:JavaSdkDirectory=$(Find-JavaHome)" }
        Invoke-Checked 'dotnet' $args
        $extension = if ($target -eq 'windows') { '*.exe' } else { '*-Signed.apk' }
        $bin = Join-Path $script:Root "src/GloveGo.App/bin/Debug/$framework"
        $artifact = Get-ChildItem $bin -Filter $extension -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -Like 'GloveGo*' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $artifact -and $target -eq 'android') { $artifact = Get-ChildItem $bin -Filter '*.apk' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 }
        if (-not $artifact) { Fail "$target build succeeded but its app artifact could not be found under $bin" }
        Write-Host "$target build: $($artifact.FullName)"
    }
}
Export-ModuleMember -Function Find-Tool,Find-Vs,Find-Cmake,Find-Ninja,Find-Ndk,Find-JavaHome,Assert-Prerequisites,Invoke-Checked,Invoke-GloveBuild,Require-File,Fail
