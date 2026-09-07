#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [Parameter()]
    [string] $Version,

    [Parameter()]
    [string] $ReleaseTag,

    [Parameter()]
    [switch] $SkipTests,

    [Parameter()]
    [switch] $UnsignedPreview,

    [Parameter()]
    [string] $PackageCertificateKeyFile = $env:TOKENUSAGE_CERTIFICATE_PATH,

    [Parameter()]
    [string] $PackageCertificatePassword = $env:TOKENUSAGE_CERTIFICATE_PASSWORD
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildPropertiesPath = Join-Path $repoRoot 'Directory.Build.props'
$appProject = Join-Path $repoRoot 'src\TokenUsage.App\TokenUsage.App.csproj'
$cliProject = Join-Path $repoRoot 'src\TokenUsage.Cli\TokenUsage.Cli.csproj'
$packageProject = Join-Path $repoRoot 'src\TokenUsage.Package\TokenUsage.Package.wapproj'
$packageManifestPath = Join-Path $repoRoot 'src\TokenUsage.Package\Package.appxmanifest'
$packageOutput = Join-Path $repoRoot 'src\TokenUsage.Package\AppPackages'
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$stagingRoot = Join-Path $repoRoot 'artifacts\release-staging'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Command
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Resolve-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        throw 'Visual Studio MSBuild discovery tool is missing.'
    }

    $visualStudio = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    $candidate = Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw 'Visual Studio MSBuild is required to build the package.'
    }

    return $candidate
}

function Reset-Directory {
    param([Parameter(Mandatory)][string] $Path)

    if (Test-Path -LiteralPath $Path) {
        $resolved = (Resolve-Path -LiteralPath $Path).Path
        $artifactsRoot = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'artifacts')).Path
        if (-not $resolved.StartsWith($artifactsRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "The cleanup path is outside artifacts: $resolved"
        }

        [System.IO.Directory]::Delete($resolved, $true)
    }

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Copy-PublishOutput {
    param(
        [Parameter(Mandatory)][string] $Source,
        [Parameter(Mandatory)][string] $Destination
    )

    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        if ($file.Extension -eq '.pdb') {
            continue
        }

        $relativePath = [System.IO.Path]::GetRelativePath($Source, $file.FullName)
        $destinationPath = Join-Path $Destination $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

        if (Test-Path -LiteralPath $destinationPath) {
            $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $destinationHash = (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash
            if ($sourceHash -ne $destinationHash) {
                throw "Publish outputs conflict: $relativePath"
            }

            continue
        }

        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath
    }
}

function Assert-PublishedVersion {
    param([Parameter(Mandatory)][string] $Path)

    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($Path).Version.ToString()
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ($assemblyVersion -ne $windowsVersion -or
        $fileVersion.FileVersion -ne $windowsVersion -or
        $fileVersion.ProductVersion -ne $Version -or
        $fileVersion.ProductName -ne 'TokenUsage') {
        throw "Published assembly metadata does not match release $Version`: $Path"
    }
}

function Copy-PortableRuntimeResources {
    param([Parameter(Mandatory)][string] $Destination)

    # The component publish omits the aggregate runtime resource needed by
    # AppNotificationManager.Register. Use the matching restored runtime, never
    # a DLL from the machine's installed Windows App Runtime.
    $restore = Get-Content -LiteralPath (Join-Path $repoRoot 'src\TokenUsage.App\obj\project.assets.json') -Raw | ConvertFrom-Json
    $runtime = @($restore.libraries.PSObject.Properties | Where-Object Name -Like 'Microsoft.WindowsAppSDK.Runtime/*')
    if ($runtime.Count -ne 1) { throw 'Expected exactly one restored Windows App SDK runtime.' }
    $packageRoots = @($restore.packageFolders.PSObject.Properties.Name | ForEach-Object {
        Join-Path $_ $runtime[0].Value.path
    } | Where-Object { Test-Path -LiteralPath (Join-Path $_ "tools\MSIX\win10-$architecture\Microsoft.WindowsAppRuntime.2.msix") })
    if ($packageRoots.Count -ne 1) { throw 'The restored runtime resource package could not be resolved uniquely.' }

    $resourceName = 'Microsoft.WindowsAppRuntime.Insights.Resource.dll'
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $packageRoots[0] "tools\MSIX\win10-$architecture\Microsoft.WindowsAppRuntime.2.msix"))
    try {
        $entry = $archive.GetEntry($resourceName)
        if ($null -eq $entry) { throw 'The runtime package is missing its Insights resource.' }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $Destination $resourceName), $true)
    }
    finally { $archive.Dispose() }
    Copy-Item -LiteralPath (Join-Path $packageRoots[0] 'license.txt') -Destination (Join-Path $Destination 'LICENSE-WINDOWS-APP-SDK.txt')
}

function Assert-PackageIdentity {
    param([Parameter(Mandatory)][string] $Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if ($null -eq $entry) {
            $entry = $archive.GetEntry('AppxMetadata/AppxBundleManifest.xml')
        }
        if ($null -eq $entry) {
            throw 'The package does not contain an identity manifest.'
        }

        $reader = [System.IO.StreamReader]::new($entry.Open())
        try {
            [xml] $builtManifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        $identity = $builtManifest.DocumentElement.Identity
        if ($identity.Name -ne $packageIdentity.Name -or
            $identity.Publisher -ne $packageIdentity.Publisher -or
            $identity.Version -ne $windowsVersion) {
            throw 'The built package identity does not match the release source.'
        }
    }
    finally {
        $archive.Dispose()
    }
}

[xml] $buildProperties = Get-Content -LiteralPath $buildPropertiesPath -Raw
$sourceVersion = [string] $buildProperties.Project.PropertyGroup.Version
if (-not $Version) {
    $Version = $sourceVersion
}

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
    @($Version.Split('.') | Where-Object { [long] $_ -gt 65535 }).Count -gt 0) {
    throw "Version must use the major.minor.patch format: $Version"
}

if ($Version -ne $sourceVersion) {
    throw "Version $Version does not match Directory.Build.props version $sourceVersion."
}

$windowsVersion = "$Version.0"
[xml] $appManifest = Get-Content -LiteralPath (Join-Path $repoRoot 'src\TokenUsage.App\app.manifest') -Raw
[xml] $packageManifest = Get-Content -LiteralPath $packageManifestPath -Raw
$packageIdentity = $packageManifest.Package.Identity
if ($buildProperties.Project.PropertyGroup.AssemblyVersion -ne $windowsVersion -or
    $buildProperties.Project.PropertyGroup.FileVersion -ne $windowsVersion -or
    $buildProperties.Project.PropertyGroup.InformationalVersion -ne $Version -or
    $appManifest.assembly.assemblyIdentity.version -ne $windowsVersion -or
    $packageIdentity.Version -ne $windowsVersion) {
    throw "Assembly, file, app manifest, and package versions must match release $Version."
}

if ($ReleaseTag) {
    $expectedTag = if ($UnsignedPreview) { '^v' + [regex]::Escape($Version) + '-preview\.[1-9]\d*$' } else { '^v' + [regex]::Escape($Version) + '$' }
    if ($ReleaseTag -cnotmatch $expectedTag) {
        throw 'The release tag must match the source version and selected stable or unsigned-preview channel.'
    }
    $tagCommit = & git -C $repoRoot rev-parse --verify "$ReleaseTag^{commit}"
    if ($LASTEXITCODE -ne 0) {
        throw "The release tag does not exist locally: $ReleaseTag"
    }
    $headCommit = & git -C $repoRoot rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $headCommit) {
        throw 'The release tag must point to the checked-out commit.'
    }
}

if ($PackageCertificatePassword -and -not $PackageCertificateKeyFile) {
    throw 'PackageCertificateKeyFile is required when PackageCertificatePassword is set.'
}
if ($UnsignedPreview -and ($PackageCertificateKeyFile -or $PackageCertificatePassword)) {
    throw 'Unsigned previews must not use a package signing certificate.'
}

$architecture = $Platform.ToLowerInvariant()
$runtimeIdentifier = "win-$architecture"
$portableName = "TokenUsage-$Version-win-$architecture-portable"
if ($UnsignedPreview) { $portableName += '-unsigned-preview' }
$portableDirectory = Join-Path $stagingRoot $portableName
$appPublish = Join-Path $stagingRoot 'app-publish'
$cliPublish = Join-Path $stagingRoot 'cli-publish'
$portableZip = Join-Path $releaseRoot "$portableName.zip"

New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot 'artifacts') | Out-Null
Reset-Directory $releaseRoot
Reset-Directory $stagingRoot
New-Item -ItemType Directory -Force -Path $portableDirectory | Out-Null

if (Test-Path -LiteralPath $packageOutput) {
    $resolvedPackageOutput = (Resolve-Path -LiteralPath $packageOutput).Path
    $expectedPackageOutput = [System.IO.Path]::GetFullPath($packageOutput)
    if (-not [string]::Equals(
        $resolvedPackageOutput,
        $expectedPackageOutput,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The package output path is not valid: $resolvedPackageOutput"
    }

    [System.IO.Directory]::Delete($resolvedPackageOutput, $true)
}

Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        $checkParameters = @{
            Platform = $Platform
            Configuration = 'Release'
        }
        if ($PackageCertificateKeyFile) {
            $checkParameters.PackageCertificateKeyFile = $PackageCertificateKeyFile
            if ($PackageCertificatePassword) {
                $checkParameters.PackageCertificatePassword = $PackageCertificatePassword
            }
        }

        & (Join-Path $repoRoot 'scripts\check.ps1') @checkParameters
        if ($LASTEXITCODE -ne 0) {
            throw "The release check failed with exit code $LASTEXITCODE."
        }
    }
    else {
        $msbuild = Resolve-MSBuild
        $packageArguments = @(
            $packageProject,
            '/restore',
            '/p:Configuration=Release',
            "/p:Platform=$Platform",
            '/p:GenerateAppxPackageOnBuild=true',
            '/verbosity:minimal',
            '/nologo'
        )
        if ($PackageCertificateKeyFile) {
            $resolvedCertificate = (Resolve-Path -LiteralPath $PackageCertificateKeyFile).Path
            $packageArguments += '/p:AppxPackageSigningEnabled=true'
            $packageArguments += "/p:PackageCertificateKeyFile=$resolvedCertificate"
            if ($PackageCertificatePassword) {
                $packageArguments += "/p:PackageCertificatePassword=$PackageCertificatePassword"
            }
        }

        Invoke-CheckedCommand 'Package build' { & $msbuild @packageArguments }
    }

    Invoke-CheckedCommand 'Portable app publish' {
        & dotnet publish $appProject `
            --configuration Release `
            --runtime $runtimeIdentifier `
            --self-contained true `
            --output $appPublish `
            "-p:Platform=$Platform" `
            "-p:PublishProfile=portable-$architecture"
    }

    Invoke-CheckedCommand 'Portable CLI publish' {
        & dotnet publish $cliProject `
            --configuration Release `
            --runtime $runtimeIdentifier `
            --self-contained true `
            --output $cliPublish `
            "-p:Platform=$Platform" `
            '-p:PublishTrimmed=false' `
            '-p:PublishSingleFile=false'
    }

    Assert-PublishedVersion (Join-Path $appPublish 'TokenUsage.App.dll')
    Assert-PublishedVersion (Join-Path $cliPublish 'tokenusage.dll')
    Copy-PortableRuntimeResources $appPublish
    Copy-PublishOutput $appPublish $portableDirectory
    Copy-PublishOutput $cliPublish (Join-Path $portableDirectory 'cli')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $portableDirectory
    Set-Content -LiteralPath (Join-Path $portableDirectory 'TokenUsage.portable') -Encoding utf8 -Value @(
        'TokenUsage portable distribution'
        "Version: $Version"
        'The app and CLI store TokenUsage data in the Data folder.'
    )
    Set-Content -LiteralPath (Join-Path $portableDirectory 'README-PORTABLE.txt') -Encoding utf8 -Value @(
        "TokenUsage $Version portable"
        ''
        $(if ($UnsignedPreview) { 'UNSIGNED PREVIEW: Windows may show an unknown-publisher warning. This is not a signed or stable release.' })
        'Run TokenUsage.App.exe to start the tray app.'
        'Run cli\tokenusage.exe from PowerShell to use the CLI.'
        'Keep TokenUsage.portable beside the executable files.'
        'Keep TokenUsage.files.json so updates can identify files owned by this release.'
        'The app and CLI use the Data folder in this directory.'
        'Move the complete directory when you move the app.'
    )
    Set-Content -LiteralPath (Join-Path $portableDirectory 'tokenusage.cmd') -Encoding ascii -Value @(
        '@echo off'
        '"%~dp0cli\tokenusage.exe" %*'
    )

    if (Test-Path -LiteralPath (Join-Path $portableDirectory 'Data')) {
        throw 'A portable release must not contain user data.'
    }
    $inventoryName = 'TokenUsage.files.json'
    $managedFiles = @($inventoryName) + @(Get-ChildItem -LiteralPath $portableDirectory -Recurse -File |
        ForEach-Object { [System.IO.Path]::GetRelativePath($portableDirectory, $_.FullName).Replace('\', '/') })
    ConvertTo-Json -InputObject @($managedFiles | Sort-Object -CaseSensitive -Unique) |
        Set-Content -LiteralPath (Join-Path $portableDirectory $inventoryName) -Encoding utf8

    Compress-Archive -LiteralPath $portableDirectory -DestinationPath $portableZip -CompressionLevel Optimal

    $packageAsset = Get-ChildItem -LiteralPath $packageOutput -Recurse -File -ErrorAction Stop |
        Where-Object { $_.Extension -in @('.msixbundle', '.msix', '.appxbundle', '.appx') } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $packageAsset) {
        throw "The package build did not create an MSIX asset under $packageOutput."
    }

    Assert-PackageIdentity $packageAsset.FullName
    $signature = Get-AuthenticodeSignature -LiteralPath $packageAsset.FullName
    $packageIsSigned = $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid
    if ($PackageCertificateKeyFile -and -not $packageIsSigned) {
        throw "The package signature is not valid: $($signature.StatusMessage)"
    }
    if ($ReleaseTag -and -not $UnsignedPreview -and -not $packageIsSigned) {
        throw 'A tagged release requires a valid signed package.'
    }

    $packageSuffix = if ($packageIsSigned) { '' } else { '-unsigned' }
    $packageName = "TokenUsage-$Version-win-$architecture$packageSuffix$($packageAsset.Extension)"
    $packageDestination = Join-Path $releaseRoot $packageName
    if (-not $UnsignedPreview) {
        Copy-Item -LiteralPath $packageAsset.FullName -Destination $packageDestination
    }

    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Git did not return the release commit.'
    }

    $payloadAssets = @($portableZip)
    if (-not $UnsignedPreview) { $payloadAssets += $packageDestination }
    $manifestAssets = @($payloadAssets | ForEach-Object {
        $item = Get-Item -LiteralPath $_
        [ordered]@{
            name = $item.Name
            bytes = $item.Length
            sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    $manifest = [ordered]@{
        schema = 'tokenusage.release.v1'
        version = $Version
        channel = $(if ($UnsignedPreview) { 'unsigned-preview' } else { 'stable-candidate' })
        platform = $Platform
        runtimeIdentifier = $runtimeIdentifier
        commit = $commit
        generatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        portableDataDirectory = 'Data'
        packageSigned = $packageIsSigned
        assets = $manifestAssets
    }
    $manifestPath = Join-Path $releaseRoot 'release-manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    $checksumPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
    @($payloadAssets + $manifestPath) |
        ForEach-Object {
            $item = Get-Item -LiteralPath $_
            $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $($item.Name)"
        } |
        Set-Content -LiteralPath $checksumPath -Encoding ascii

    Write-Host "Release assets: $releaseRoot" -ForegroundColor Green
    Get-ChildItem -LiteralPath $releaseRoot -File |
        Select-Object Name, Length, LastWriteTime |
        Format-Table -AutoSize
}
finally {
    Pop-Location
}
