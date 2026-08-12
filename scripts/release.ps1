#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [Parameter()]
    [string] $Version,

    [Parameter()]
    [switch] $SkipTests,

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

[xml] $buildProperties = Get-Content -LiteralPath $buildPropertiesPath -Raw
$sourceVersion = [string] $buildProperties.Project.PropertyGroup.Version
if (-not $Version) {
    $Version = $sourceVersion
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use the major.minor.patch format: $Version"
}

if ($Version -ne $sourceVersion) {
    throw "Version $Version does not match Directory.Build.props version $sourceVersion."
}

if ($PackageCertificatePassword -and -not $PackageCertificateKeyFile) {
    throw 'PackageCertificateKeyFile is required when PackageCertificatePassword is set.'
}

$architecture = $Platform.ToLowerInvariant()
$runtimeIdentifier = "win-$architecture"
$portableName = "TokenUsage-$Version-win-$architecture-portable"
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
        'Run TokenUsage.App.exe to start the tray app.'
        'Run cli\tokenusage.exe from PowerShell to use the CLI.'
        'Keep TokenUsage.portable beside the executable files.'
        'The app and CLI use the Data folder in this directory.'
        'Move the complete directory when you move the app.'
    )
    Set-Content -LiteralPath (Join-Path $portableDirectory 'tokenusage.cmd') -Encoding ascii -Value @(
        '@echo off'
        '"%~dp0cli\tokenusage.exe" %*'
    )

    Compress-Archive -LiteralPath $portableDirectory -DestinationPath $portableZip -CompressionLevel Optimal

    $packageAsset = Get-ChildItem -LiteralPath $packageOutput -Recurse -File -ErrorAction Stop |
        Where-Object { $_.Extension -in @('.msixbundle', '.msix', '.appxbundle', '.appx') } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $packageAsset) {
        throw "The package build did not create an MSIX asset under $packageOutput."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $packageAsset.FullName
    $packageIsSigned = $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid
    if ($PackageCertificateKeyFile -and -not $packageIsSigned) {
        throw "The package signature is not valid: $($signature.StatusMessage)"
    }

    $packageSuffix = if ($packageIsSigned) { '' } else { '-unsigned' }
    $packageName = "TokenUsage-$Version-win-$architecture$packageSuffix$($packageAsset.Extension)"
    $packageDestination = Join-Path $releaseRoot $packageName
    Copy-Item -LiteralPath $packageAsset.FullName -Destination $packageDestination

    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Git did not return the release commit.'
    }

    $payloadAssets = @($portableZip, $packageDestination)
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
