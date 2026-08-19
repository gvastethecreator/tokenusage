#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [Parameter()]
    [ValidateSet('Release')]
    [string] $Configuration = 'Release',

    [Parameter()]
    [switch] $SkipTests,

    [Parameter()]
    [string] $PackageCertificateKeyFile = $env:TOKENUSAGE_STORE_CERTIFICATE_PATH,

    [Parameter()]
    [string] $PackageCertificatePassword = $env:TOKENUSAGE_STORE_CERTIFICATE_PASSWORD,

    [Parameter()]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$manifestPath = Join-Path $repoRoot 'src\TokenUsage.Package\Package.appxmanifest'
$packageProject = Join-Path $repoRoot 'src\TokenUsage.Package\TokenUsage.Package.wapproj'
$buildPropertiesPath = Join-Path $repoRoot 'Directory.Build.props'
$readinessScript = Join-Path $PSScriptRoot 'Test-StoreReadiness.ps1'
$checkScript = Join-Path $repoRoot 'scripts\check.ps1'

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\store\$($Platform.ToLowerInvariant())"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [scriptblock] $Command
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $Command
    if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Resolve-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw 'Visual Studio Installer vswhere.exe is required.'
    }

    $visualStudio = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($visualStudio)) {
        throw 'A Visual Studio installation with MSBuild is required.'
    }

    $candidate = Join-Path $visualStudio 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "MSBuild was not found at $candidate."
    }

    return $candidate
}

function Reset-OutputDirectory {
    param([Parameter(Mandatory)] [string] $Path)

    $artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
    if (-not $Path.StartsWith($artifactsRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a Store output path outside artifacts: $Path"
    }

    if (Test-Path -LiteralPath $Path) {
        [System.IO.Directory]::Delete($Path, $true)
    }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function New-RandomPassword {
    $bytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return [Convert]::ToBase64String($bytes)
}

function New-TemporaryStoreCertificate {
    param([Parameter(Mandatory)] [string] $Publisher)

    $plainPassword = New-RandomPassword
    $securePassword = ConvertTo-SecureString -String $plainPassword -AsPlainText -Force
    $temporaryRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
        [System.IO.Path]::GetTempPath()
    }
    else {
        $env:RUNNER_TEMP
    }
    $pfxPath = Join-Path $temporaryRoot "TokenUsage-store-build-$([Guid]::NewGuid().ToString('N')).pfx"

    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $Publisher `
        -FriendlyName 'TokenUsage Store build certificate (temporary)' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy Exportable `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddDays(7) `
        -TextExtension '2.5.29.37={text}1.3.6.1.5.5.7.3.3'

    Export-PfxCertificate `
        -Cert $certificate `
        -FilePath $pfxPath `
        -Password $securePassword `
        -Force | Out-Null

    return [ordered]@{
        path = $pfxPath
        password = $plainPassword
        thumbprint = $certificate.Thumbprint
        generated = $true
    }
}

& $readinessScript
if ($LASTEXITCODE -ne 0) {
    throw 'Store readiness validation failed.'
}

[xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw
$publisher = [string] $manifest.Package.Identity.Publisher
$packageVersion = [string] $manifest.Package.Identity.Version

[xml] $buildProperties = Get-Content -LiteralPath $buildPropertiesPath -Raw
$productVersion = [string] $buildProperties.Project.PropertyGroup.Version
$expectedPackageVersion = "$productVersion.0"
if ($packageVersion -ne $expectedPackageVersion) {
    throw "Package version $packageVersion does not match product version $productVersion (expected $expectedPackageVersion)."
}

$certificateState = $null
$temporaryCertificate = $false
try {
    if ($PackageCertificateKeyFile) {
        $resolvedCertificatePath = (Resolve-Path -LiteralPath $PackageCertificateKeyFile).Path
        if (-not $PackageCertificatePassword) {
            throw 'TOKENUSAGE_STORE_CERTIFICATE_PASSWORD or -PackageCertificatePassword is required with a supplied PFX.'
        }

        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $resolvedCertificatePath,
            $PackageCertificatePassword
        )
        try {
            if (-not [string]::Equals($certificate.Subject, $publisher, [StringComparison]::Ordinal)) {
                throw "Certificate subject '$($certificate.Subject)' does not match manifest publisher '$publisher'."
            }

            $certificateState = [ordered]@{
                path = $resolvedCertificatePath
                password = $PackageCertificatePassword
                thumbprint = $certificate.Thumbprint
                generated = $false
            }
        }
        finally {
            $certificate.Dispose()
        }
    }
    else {
        Write-Host 'No Store build certificate was supplied. Generating a short-lived self-signed certificate for package construction.' -ForegroundColor Yellow
        Write-Host 'This certificate is not a public distribution credential. Microsoft Store replaces the package signature after certification.' -ForegroundColor Yellow
        $certificateState = New-TemporaryStoreCertificate -Publisher $publisher
        $temporaryCertificate = $true
    }

    if (-not $SkipTests) {
        $checkArguments = @{
            Platform = $Platform
            Configuration = $Configuration
            PackageCertificateKeyFile = $certificateState.path
            PackageCertificatePassword = $certificateState.password
        }
        & $checkScript @checkArguments
        if ($LASTEXITCODE -ne 0) {
            throw "The complete release check failed with exit code $LASTEXITCODE."
        }
    }

    Reset-OutputDirectory -Path $OutputDirectory
    $msbuild = Resolve-MSBuild
    $packageDirectory = $OutputDirectory.TrimEnd('\') + '\'
    $arguments = @(
        $packageProject,
        '/restore',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform",
        '/p:GenerateAppxPackageOnBuild=true',
        "/p:AppxPackageDir=$packageDirectory",
        '/p:UapAppxPackageBuildMode=StoreUpload',
        '/p:AppxBundle=Always',
        "/p:AppxBundlePlatforms=$Platform",
        '/p:AppxPackageSigningEnabled=true',
        '/p:PackageCertificateThumbprint=',
        "/p:PackageCertificateKeyFile=$($certificateState.path)",
        "/p:PackageCertificatePassword=$($certificateState.password)",
        '/verbosity:minimal',
        '/nologo'
    )

    Invoke-CheckedCommand 'Build Store upload package' { & $msbuild @arguments }

    $uploadFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File -Filter '*.msixupload')
    if ($uploadFiles.Count -ne 1) {
        $found = @($uploadFiles | Select-Object -ExpandProperty FullName)
        throw "Expected exactly one .msixupload under $OutputDirectory; found $($uploadFiles.Count): $($found -join ', ')"
    }

    $forbiddenPrivateFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File | Where-Object {
        $_.Extension -in @('.pfx', '.pvk', '.key')
    })
    if ($forbiddenPrivateFiles.Count -gt 0) {
        throw "Private signing material was written into the Store artifact directory: $($forbiddenPrivateFiles.FullName -join ', ')"
    }

    $upload = $uploadFiles[0]
    $commit = (& git -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to resolve the source commit.'
    }

    $buildManifest = [ordered]@{
        schema = 'tokenusage.store-build.v1'
        generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
        sourceCommit = $commit
        productVersion = $productVersion
        packageVersion = $packageVersion
        platform = $Platform
        configuration = $Configuration
        packageIdentityName = [string] $manifest.Package.Identity.Name
        publisher = $publisher
        storeId = '9NWX6M53B36K'
        artifact = [ordered]@{
            path = $upload.FullName
            name = $upload.Name
            bytes = $upload.Length
            sha256 = (Get-FileHash -LiteralPath $upload.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        buildCertificate = [ordered]@{
            thumbprint = $certificateState.thumbprint
            temporary = $certificateState.generated
            note = 'Build/test signature only. Microsoft Store replaces MSIX/AppX signatures after certification.'
        }
    }

    $buildManifestPath = Join-Path $OutputDirectory 'store-build-manifest.json'
    $buildManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $buildManifestPath -Encoding utf8

    Write-Host ''
    Write-Host 'STORE UPLOAD READY FOR REVIEW' -ForegroundColor Green
    Write-Host "Upload: $($upload.FullName)"
    Write-Host "SHA-256: $($buildManifest.artifact.sha256)"
    Write-Host "Evidence: $buildManifestPath"
    Write-Host 'Do not submit until clean install, launch, alias, upgrade, uninstall, privacy, listing, and Partner Center checks are complete.' -ForegroundColor Yellow
}
finally {
    if ($temporaryCertificate -and $certificateState) {
        if (Test-Path -LiteralPath $certificateState.path) {
            Remove-Item -LiteralPath $certificateState.path -Force
        }
        if ($certificateState.thumbprint) {
            $certificateStorePath = "Cert:\CurrentUser\My\$($certificateState.thumbprint)"
            if (Test-Path -LiteralPath $certificateStorePath) {
                Remove-Item -LiteralPath $certificateStorePath -Force
            }
        }
    }
}
