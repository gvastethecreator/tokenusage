#Requires -Version 5.1

param(
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [string] $PackageCertificateKeyFile,
    [string] $PackageCertificatePassword,
    [string] $OutputDirectory,
    [switch] $StoreUpload,
    [switch] $EnableUiTestFixtures
)

$ErrorActionPreference = 'Stop'
$Platform = if ($Platform -ieq 'ARM64') { 'ARM64' } else { 'x64' }
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$packageRoot = Join-Path $repoRoot 'src\TokenUsage.Package'
$appProject = Join-Path $repoRoot 'src\TokenUsage.App\TokenUsage.App.csproj'
$cliProject = Join-Path $repoRoot 'src\TokenUsage.Cli\TokenUsage.Cli.csproj'
$architecture = $Platform.ToLowerInvariant()
$layout = Join-Path $packageRoot "bin\$Platform\$Configuration"
$workDirectory = Join-Path $packageRoot "obj\native-package\$Platform\$Configuration"
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Assert-NoReparsePoint {
    param([string] $Path, [switch] $Recurse)

    $ancestor = [IO.Path]::GetFullPath($Path)
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Packaging paths cannot contain reparse points: $ancestor"
            }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    if ($Recurse -and (Test-Path -LiteralPath $Path -PathType Container)) {
        foreach ($child in Get-ChildItem -LiteralPath $Path -Force) {
            if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Packaging paths cannot contain reparse points: $($child.FullName)"
            }
            if ($child.PSIsContainer) { Assert-NoReparsePoint -Path $child.FullName -Recurse }
        }
    }
}

function Reset-PackageDirectory {
    param([string] $Path, [string[]] $AllowedRoots)

    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $allowed = @($AllowedRoots | Where-Object {
        $resolved.StartsWith(([IO.Path]::GetFullPath($_).TrimEnd('\', '/') + '\'), [StringComparison]::OrdinalIgnoreCase)
    })
    if ($allowed.Count -eq 0) { throw "Refusing to clear a path outside packaging output folders: $resolved" }
    Assert-NoReparsePoint -Path $resolved -Recurse
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

function Invoke-PackageTool {
    param([string] $Name, [string] $Tool, [string[]] $Arguments)

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Tool @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

function Get-RestoredPackagePath {
    param([object] $Assets, [string] $PackageId)

    $entries = @($Assets.libraries.PSObject.Properties | Where-Object { $_.Name.StartsWith("$PackageId/", [StringComparison]::OrdinalIgnoreCase) })
    if ($entries.Count -ne 1) { throw "Expected one restored $PackageId package." }
    foreach ($folder in $Assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $entries[0].Value.path
        if (Test-Path -LiteralPath $candidate -PathType Container) { return $candidate }
    }
    throw "The restored $PackageId package could not be found."
}

function Get-PackageIdentity {
    param([string] $Path)

    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "Missing AppxManifest.xml in $Path" }
        $reader = New-Object IO.StreamReader($entry.Open())
        try { [xml] $xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        return $xml.Package.Identity
    }
    finally { $archive.Dispose() }
}

function Add-FrameworkDependency {
    param([xml] $Manifest, [string] $Name, [string] $Version, [string] $Publisher)

    $dependency = $Manifest.CreateElement('PackageDependency', $Manifest.DocumentElement.NamespaceURI)
    $dependency.SetAttribute('Name', $Name)
    $dependency.SetAttribute('MinVersion', $Version)
    $dependency.SetAttribute('Publisher', $Publisher)
    $Manifest.Package.Dependencies.AppendChild($dependency) | Out-Null
}

function Sign-Package {
    param([string] $Path)

    if (-not $PackageCertificateKeyFile) { return }
    $signArguments = @('sign', '/fd', 'SHA256', '/f', $PackageCertificateKeyFile)
    if ($PackageCertificatePassword) { $signArguments += @('/p', $PackageCertificatePassword) }
    $signArguments += $Path
    Invoke-PackageTool 'Sign package' $signTool $signArguments
}

if ($PackageCertificatePassword -and -not $PackageCertificateKeyFile) {
    throw 'PackageCertificateKeyFile is required when PackageCertificatePassword is set.'
}
if ($StoreUpload -and -not $PackageCertificateKeyFile) { throw 'StoreUpload requires a package signing certificate.' }
if ($PackageCertificateKeyFile) {
    $PackageCertificateKeyFile = (Resolve-Path -LiteralPath $PackageCertificateKeyFile).Path
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio with MSBuild and the Windows SDK is required.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $visualStudio) { throw 'Visual Studio MSBuild was not found.' }
$msbuild = Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild)) { throw "MSBuild was not found: $msbuild" }

[xml] $manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'Package.appxmanifest') -Raw
$manifest.Package.Identity.SetAttribute('ProcessorArchitecture', $architecture)
$version = $manifest.Package.Identity.Version
$artifactName = "TokenUsage.Package_${version}_${architecture}"
if ($Configuration -eq 'Debug') { $artifactName += '_Debug' }
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $packageRoot "AppPackages\${artifactName}_Test"
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$outputRoots = @((Join-Path $packageRoot 'AppPackages'), (Join-Path $repoRoot 'artifacts'))
foreach ($clearedDirectory in @($OutputDirectory, $layout, $workDirectory)) {
    if ($PackageCertificateKeyFile -and $PackageCertificateKeyFile.StartsWith($clearedDirectory.TrimEnd('\', '/') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The signing certificate must be outside the packaging output directories.'
    }
}

Reset-PackageDirectory $OutputDirectory $outputRoots
Reset-PackageDirectory $layout @((Join-Path $packageRoot 'bin'))
Reset-PackageDirectory $workDirectory @((Join-Path $packageRoot 'obj\native-package'))

Add-Type -AssemblyName System.IO.Compression.FileSystem
$commonArguments = @(
    '/nologo', '/verbosity:minimal', "/p:Platform=$Platform", "/p:Configuration=$Configuration",
    "/p:RuntimeIdentifier=win-$architecture", '/p:SelfContained=true',
    '/p:PublishSingleFile=false', '/p:PublishTrimmed=false'
)
$appPublish = Join-Path $layout 'TokenUsage.App'
$cliPublish = Join-Path $layout 'TokenUsage.Cli'
$appArguments = $commonArguments + @("/p:PublishProfile=win-$architecture", "/p:EnableUiTestFixtures=$($EnableUiTestFixtures.IsPresent.ToString().ToLowerInvariant())")

Push-Location $repoRoot
try {
    Invoke-PackageTool 'Publish packaged app' $msbuild (@($appProject, '/restore', '/t:Publish', "/p:PublishDir=$appPublish\") + $appArguments)
    Invoke-PackageTool 'Publish packaged CLI' $msbuild (@($cliProject, '/restore', '/t:Publish', "/p:PublishDir=$cliPublish\") + $commonArguments)

    # Query the same evaluated project that was published; package versions have one owner.
    $metadataText = & $msbuild $appProject @appArguments '-getProperty:TargetDir,ProjectAssetsFile,SDKReferenceDirectoryRoot,DefaultLanguage,TargetName' '-getItem:Content'
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the published app metadata.' }
    $metadata = ($metadataText -join [Environment]::NewLine) | ConvertFrom-Json
    $properties = $metadata.Properties
    $assets = Get-Content -LiteralPath $properties.ProjectAssetsFile -Raw | ConvertFrom-Json
    $buildTools = Get-RestoredPackagePath $assets 'Microsoft.Windows.SDK.BuildTools'
    $toolDirectories = @(Get-ChildItem -LiteralPath (Join-Path $buildTools 'bin') -Directory | Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe')
    })
    if ($toolDirectories.Count -ne 1) { throw 'Expected one native Windows SDK toolset in the restored BuildTools package.' }
    $nativeTools = Join-Path $toolDirectories[0].FullName 'x64'
    $makeAppx = Join-Path $nativeTools 'makeappx.exe'
    $makePri = Join-Path $nativeTools 'makepri.exe'
    $signTool = Join-Path $nativeTools 'signtool.exe'
    foreach ($tool in @($makeAppx, $makePri, $signTool)) {
        if (-not (Test-Path -LiteralPath $tool)) { throw "Missing restored Windows SDK tool: $tool" }
    }

    # The app PRI addresses content from the package root, including its embedded XAML.
    foreach ($content in $metadata.Items.Content) {
        $relative = $content.Identity
        $link = $content.PSObject.Properties['Link']
        if ($link -and $link.Value) { $relative = $link.Value }
        if ($relative.StartsWith('Assets\', [StringComparison]::OrdinalIgnoreCase) -or $relative -eq 'THIRD-PARTY-NOTICES.md') {
            $destination = Join-Path $layout $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $content.FullPath -Destination $destination -Force
        }
    }

    $dependencyDirectory = Join-Path $OutputDirectory "Dependencies\$architecture"
    New-Item -ItemType Directory -Path $dependencyDirectory -Force | Out-Null
    $runtimePackage = Get-RestoredPackagePath $assets 'Microsoft.WindowsAppSDK.Runtime'
    $runtimeMsix = Join-Path $runtimePackage "tools\MSIX\win10-$architecture\Microsoft.WindowsAppRuntime.2.msix"
    $runtimeIdentity = Get-PackageIdentity $runtimeMsix
    Add-FrameworkDependency $manifest $runtimeIdentity.Name $runtimeIdentity.Version $runtimeIdentity.Publisher
    Copy-Item -LiteralPath $runtimeMsix -Destination $dependencyDirectory

    $sdkConfiguration = 'Retail'
    if ($Configuration -eq 'Debug') { $sdkConfiguration = 'Debug' }
    foreach ($sdk in @('Microsoft.VCLibs.Desktop', 'Microsoft.VCLibs')) {
        $sdkManifestPath = $null
        foreach ($sdkRoot in $properties.SDKReferenceDirectoryRoot.Split(';')) {
            $candidate = Join-Path $sdkRoot "Windows Kits\10\ExtensionSDKs\$sdk\14.0\SDKManifest.xml"
            if (Test-Path -LiteralPath $candidate) { $sdkManifestPath = $candidate; break }
        }
        if (-not $sdkManifestPath) { throw "Install the Windows SDK $sdk extension before packaging." }
        [xml] $sdkManifest = Get-Content -LiteralPath $sdkManifestPath -Raw
        $framework = $sdkManifest.FileList.GetAttribute("FrameworkIdentity-$sdkConfiguration")
        if ($framework -notmatch "^Name\s*=\s*(?<name>[^,]+),\s*MinVersion\s*=\s*(?<version>[^,]+),\s*Publisher\s*=\s*'(?<publisher>[^']+)'\s*$") {
            throw "Invalid framework identity in $sdkManifestPath"
        }
        Add-FrameworkDependency $manifest $Matches.name.Trim() $Matches.version.Trim() $Matches.publisher
        $frameworkPackage = Join-Path (Split-Path -Parent $sdkManifestPath) $sdkManifest.FileList.GetAttribute("AppX-$sdkConfiguration-$Platform")
        if (-not (Test-Path -LiteralPath $frameworkPackage -PathType Leaf)) {
            throw "The Windows SDK is missing its $sdk $Platform framework package: $frameworkPackage. See CONTRIBUTING.md for the required Visual Studio components."
        }
        Copy-Item -LiteralPath $frameworkPackage -Destination $dependencyDirectory
    }
    $manifestPath = Join-Path $layout 'AppxManifest.xml'
    $manifest.Save($manifestPath)
    foreach ($executable in $manifest.SelectNodes('//@Executable')) {
        if (-not (Test-Path -LiteralPath (Join-Path $layout $executable.Value) -PathType Leaf)) {
            throw "The package manifest references a missing executable: $($executable.Value)"
        }
    }
    foreach ($publication in @($appPublish, $cliPublish)) {
        foreach ($runtimeFile in @('coreclr.dll', 'hostfxr.dll', 'e_sqlite3.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $publication $runtimeFile))) {
                throw "The self-contained publication is missing $runtimeFile in $publication"
            }
        }
    }

    $appPri = Join-Path $properties.TargetDir "$($properties.TargetName).pri"
    if (-not (Test-Path -LiteralPath $appPri)) { throw "The app build did not produce its XAML resource index: $appPri" }
    $priList = Join-Path $workDirectory 'pri.resfiles'
    [IO.File]::WriteAllText($priList, $appPri + [Environment]::NewLine, $utf8)
    $priConfig = Join-Path $workDirectory 'priconfig.xml'
    $escapedList = [Security.SecurityElement]::Escape($priList)
    $language = [Security.SecurityElement]::Escape($properties.DefaultLanguage)
    [IO.File]::WriteAllText($priConfig, @"
<resources targetOsVersion="10.0.0" majorVersion="1">
  <index root="\" startIndexAt="$escapedList">
    <default><qualifier name="Language" value="$language" /><qualifier name="Scale" value="200" /></default>
    <indexer-config type="PRI" /><indexer-config type="RESFILES" qualifierDelimiter="." />
  </index>
</resources>
"@, $utf8)
    Invoke-PackageTool 'Merge package XAML resources' $makePri @('new', '/pr', $packageRoot, '/cf', $priConfig, '/in', $manifest.Package.Identity.Name, '/of', (Join-Path $layout 'resources.pri'), '/o')

    # Symbols belong in the Store upload, not in the installed package.
    $symbolsDirectory = Join-Path $workDirectory 'Symbols'
    New-Item -ItemType Directory -Path $symbolsDirectory -Force | Out-Null
    foreach ($symbol in Get-ChildItem -LiteralPath $layout -Recurse -File -Filter '*.pdb') {
        $destination = Join-Path $symbolsDirectory $symbol.Name
        if (Test-Path -LiteralPath $destination) {
            if ((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $symbol.FullName).Hash) {
                throw "Conflicting publication symbols: $($symbol.Name)"
            }
            Remove-Item -LiteralPath $symbol.FullName -Force
        }
        else { Move-Item -LiteralPath $symbol.FullName -Destination $destination }
    }
    # A single root PRI owns the package resources; publishing may also copy the component PRI.
    Get-ChildItem -LiteralPath $appPublish -File -Filter '*.pri' | Remove-Item -Force
    $msix = Join-Path $OutputDirectory "$artifactName.msix"
    Invoke-PackageTool 'Validate and pack MSIX' $makeAppx @('pack', '/d', $layout, '/p', $msix, '/o')
    Sign-Package $msix

    if ($StoreUpload) {
        $bundleMap = Join-Path $workDirectory 'bundle.map.txt'
        [IO.File]::WriteAllText($bundleMap, "[Files]`r`n`"$msix`" `"$artifactName.msix`"`r`n", $utf8)
        $bundle = Join-Path $OutputDirectory "$artifactName.msixbundle"
        Invoke-PackageTool 'Build Store bundle' $makeAppx @('bundle', '/f', $bundleMap, '/p', $bundle, '/bv', $version, '/o')
        Sign-Package $bundle
        if (@(Get-ChildItem -LiteralPath $symbolsDirectory -Recurse -File -Filter '*.pdb').Count -eq 0) {
            throw 'StoreUpload requires the publication symbols.'
        }
        $uploadDirectory = Join-Path $workDirectory 'Upload'
        New-Item -ItemType Directory -Path $uploadDirectory -Force | Out-Null
        Copy-Item -LiteralPath $bundle -Destination $uploadDirectory
        $symbols = Join-Path $uploadDirectory "$artifactName.appxsym"
        [IO.Compression.ZipFile]::CreateFromDirectory($symbolsDirectory, $symbols, [IO.Compression.CompressionLevel]::Optimal, $false)
        $upload = Join-Path $OutputDirectory "$artifactName.msixupload"
        [IO.Compression.ZipFile]::CreateFromDirectory($uploadDirectory, $upload, [IO.Compression.CompressionLevel]::Optimal, $false)
        Write-Host "Store upload: $upload" -ForegroundColor Green
    }
    Write-Host "MSIX: $msix" -ForegroundColor Green
    Write-Host "Package layout: $layout" -ForegroundColor Green
    $global:LASTEXITCODE = 0
}
finally { Pop-Location }
