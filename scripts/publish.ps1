# Builds a self-contained release into ./publish (no .NET install needed on the target PC).
$root = Split-Path $PSScriptRoot -Parent
dotnet publish "$root\src\Swivora.App" -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "$root\publish"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# the desktop helper must sit next to the exe
Copy-Item "$root\third_party\VirtualDesktopAccessor\VirtualDesktopAccessor.dll" "$root\publish" -Force
