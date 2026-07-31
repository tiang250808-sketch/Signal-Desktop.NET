# Build official libsignal FFI (signal_ffi) for SignalCpf.
# Requires: Rust toolchain + cargo, Visual Studio C++ build tools (Windows).
# Usage:
#   .\scripts\build-libsignal-ffi.ps1
#   .\scripts\build-libsignal-ffi.ps1 -LibSignalDir D:\src\libsignal

param(
    [string]$LibSignalDir = "",
    [switch]$SkipClone
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$NativeOut = Join-Path $Root "native"
$Tools = Join-Path $Root ".deps\tools"
New-Item -ItemType Directory -Force -Path $NativeOut | Out-Null

# Prefer user cargo; refresh PATH for tools installed after shell start.
$env:PATH = "$(Join-Path $env:USERPROFILE '.cargo\bin');C:\Program Files\CMake\bin;C:\Program Files\LLVM\bin;C:\Program Files\Git\usr\bin;$Tools\nasm;$env:LOCALAPPDATA\Microsoft\WinGet\Links;$env:PATH"

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw "cargo not found. Install Rust: winget install Rustlang.Rustup  (then reopen the shell)."
}
if (-not (Get-Command protoc -ErrorAction SilentlyContinue)) {
    throw "protoc not found. Install: winget install Google.Protobuf"
}
if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    throw "cmake not found. Install: winget install Kitware.CMake"
}
if (-not (Test-Path "C:\Program Files\LLVM\bin\libclang.dll")) {
    throw "libclang not found. Install: winget install LLVM.LLVM"
}
$env:LIBCLANG_PATH = "C:\Program Files\LLVM\bin"
$env:PROTOC = (Get-Command protoc).Source
$env:CMAKE = (Get-Command cmake).Source

if ([string]::IsNullOrWhiteSpace($LibSignalDir)) {
    $LibSignalDir = Join-Path $Root ".deps\libsignal"
}

if (-not (Test-Path (Join-Path $LibSignalDir "Cargo.toml"))) {
    if ($SkipClone) {
        throw "libsignal not found at $LibSignalDir"
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $LibSignalDir) | Out-Null
    Write-Host "Cloning signalapp/libsignal into $LibSignalDir ..."
    git clone --depth 1 https://github.com/signalapp/libsignal.git $LibSignalDir
}

# Official crate is staticlib-only; add cdylib for C# P/Invoke.
$ffiToml = Join-Path $LibSignalDir "rust\bridge\ffi\Cargo.toml"
$toml = Get-Content $ffiToml -Raw
if ($toml -notmatch 'cdylib') {
    $patched = $toml -replace 'crate-type\s*=\s*\["staticlib"\]', 'crate-type = ["staticlib", "cdylib"]'
    if ($patched -eq $toml) {
        throw "Could not patch $ffiToml to enable cdylib"
    }
    Set-Content -Path $ffiToml -Value $patched -NoNewline
    Write-Host "Patched libsignal-ffi crate-type to include cdylib"
}

$env:CARGO_TARGET_DIR = Join-Path $LibSignalDir "target"

# Import MSVC environment when available.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvars64.bat"
    if (Test-Path $vcvars) {
        cmd /c "`"$vcvars`" && set" | ForEach-Object {
            if ($_ -match '^([^=]+)=(.*)$') {
                Set-Item -Path "Env:$($matches[1])" -Value $matches[2]
            }
        }
    }
}

Push-Location $LibSignalDir
try {
    Write-Host "Building libsignal-ffi (release, cdylib)..."
    cargo build -p libsignal-ffi --release

    $dllCandidates = @(
        (Join-Path $env:CARGO_TARGET_DIR "release\signal_ffi.dll"),
        (Join-Path $env:CARGO_TARGET_DIR "release\libsignal_ffi.dll"),
        (Join-Path $env:CARGO_TARGET_DIR "release\signal_ffi.so"),
        (Join-Path $env:CARGO_TARGET_DIR "release\libsignal_ffi.so"),
        (Join-Path $env:CARGO_TARGET_DIR "release\libsignal_ffi.dylib")
    )
    $built = $dllCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $built) {
        throw "Built shared library not found under target/release (expected signal_ffi.dll). Is cdylib enabled?"
    }

    $destName = if ($built -match '\.dll$') { "signal_ffi.dll" }
        elseif ($built -match '\.dylib$') { "libsignal_ffi.dylib" }
        else { "libsignal_ffi.so" }
    Copy-Item $built (Join-Path $NativeOut $destName) -Force
    Write-Host "Copied $built -> native\$destName"

    $headerSrc = Join-Path $LibSignalDir "swift\Sources\SignalFfi\signal_ffi.h"
    if (Test-Path $headerSrc) {
        Copy-Item $headerSrc (Join-Path $NativeOut "signal_ffi.h") -Force
    }
    Write-Host "Header ready: native\signal_ffi.h"
    Write-Host "Done. Rebuild SignalCpf.UI to copy the native library next to the app."
}
finally {
    Pop-Location
}
