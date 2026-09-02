#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter()]
    [string] $EvidencePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$manifestPath = Join-Path $repoRoot 'src\TokenUsage.Package\Package.appxmanifest'
$identityPath = Join-Path $repoRoot 'docs\store\store-identity.json'
$privacyPath = Join-Path $repoRoot 'PRIVACY.md'
$runbookPath = Join-Path $repoRoot 'docs\store\README.md'
$certificationNotesPath = Join-Path $repoRoot 'docs\store\CERTIFICATION-NOTES.md'
$listingPath = Join-Path $repoRoot 'docs\store\LISTING.md'
$evidenceTemplatePath = Join-Path $repoRoot 'docs\store\RELEASE-EVIDENCE-TEMPLATE.md'
$appProjectRoot = Join-Path $repoRoot 'src\TokenUsage.App'

$errors = [System.Collections.Generic.List[string]]::new()
$checks = [System.Collections.Generic.List[object]]::new()

function Get-RepoRelativePath {
    param([Parameter(Mandatory)] [string] $Path)

    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($root.Length)
    }

    return $fullPath
}

function Add-Check {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [bool] $Passed,
        [Parameter(Mandatory)] [string] $Details
    )

    $checks.Add([ordered]@{
        name = $Name
        passed = $Passed
        details = $Details
    })

    if (-not $Passed) {
        $errors.Add("$Name`: $Details")
    }
}

function Test-ExactText {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [AllowNull()] [string] $Actual,
        [AllowNull()] [string] $Expected
    )

    $passed = [string]::Equals($Actual, $Expected, [StringComparison]::Ordinal)
    Add-Check -Name $Name -Passed $passed -Details "expected '$Expected', actual '$Actual'"

    if ($null -ne $Actual) {
        Add-Check `
            -Name "$Name has no surrounding whitespace" `
            -Passed ([string]::Equals($Actual, $Actual.Trim(), [StringComparison]::Ordinal)) `
            -Details 'value must not contain leading, trailing, or non-breaking whitespace'
    }
}

foreach ($requiredFile in @(
    $manifestPath,
    $identityPath,
    $privacyPath,
    $runbookPath,
    $certificationNotesPath,
    $listingPath,
    $evidenceTemplatePath
)) {
    Add-Check `
        -Name "Required file: $(Get-RepoRelativePath -Path $requiredFile)" `
        -Passed (Test-Path -LiteralPath $requiredFile -PathType Leaf) `
        -Details 'file must exist'
}

if ($errors.Count -gt 0) {
    throw "Store readiness prerequisites are missing:`n - $($errors -join "`n - ")"
}

$storeIdentity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
[xml] $manifest = Get-Content -LiteralPath $manifestPath -Raw

$ns = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$ns.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$ns.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
$ns.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')
$ns.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')

$identityNode = $manifest.SelectSingleNode('/f:Package/f:Identity', $ns)
$publisherDisplayNameNode = $manifest.SelectSingleNode('/f:Package/f:Properties/f:PublisherDisplayName', $ns)
$applicationNode = $manifest.SelectSingleNode('/f:Package/f:Applications/f:Application[@Id="App"]', $ns)
$aliasNode = $manifest.SelectSingleNode('//uap5:ExecutionAlias[@Alias="tokenusage.exe"]', $ns)
$fullTrustNode = $manifest.SelectSingleNode('/f:Package/f:Capabilities/rescap:Capability[@Name="runFullTrust"]', $ns)

Add-Check -Name 'Manifest Identity node' -Passed ($null -ne $identityNode) -Details 'Package.appxmanifest must contain Package/Identity'
Add-Check -Name 'Manifest application node' -Passed ($null -ne $applicationNode) -Details 'Package.appxmanifest must contain Applications/Application Id=App'

if ($null -ne $identityNode) {
    Test-ExactText -Name 'Package identity name' -Actual $identityNode.GetAttribute('Name') -Expected $storeIdentity.packageIdentity.name
    Test-ExactText -Name 'Package publisher' -Actual $identityNode.GetAttribute('Publisher') -Expected $storeIdentity.packageIdentity.publisher

    $versionText = $identityNode.GetAttribute('Version')
    $versionParts = $versionText.Split('.')
    $validVersion = $versionText -match '^\d+\.\d+\.\d+\.\d+$' -and $versionParts.Count -eq 4
    if ($validVersion) {
        foreach ($part in $versionParts) {
            $number = 0
            if (-not [int]::TryParse($part, [ref] $number) -or $number -lt 0 -or $number -gt 65535) {
                $validVersion = $false
                break
            }
        }
    }
    Add-Check -Name 'MSIX version' -Passed $validVersion -Details "'$versionText' must contain four numeric parts from 0 through 65535"
}

Test-ExactText `
    -Name 'Publisher display name' `
    -Actual $(if ($publisherDisplayNameNode) { $publisherDisplayNameNode.InnerText } else { $null }) `
    -Expected $storeIdentity.packageIdentity.publisherDisplayName

$targetFamilies = @($manifest.SelectNodes('/f:Package/f:Dependencies/f:TargetDeviceFamily', $ns))
Add-Check -Name 'One target device family' -Passed ($targetFamilies.Count -eq 1) -Details "expected one Windows.Desktop target; found $($targetFamilies.Count)"
if ($targetFamilies.Count -gt 0) {
    $targetNames = @($targetFamilies | ForEach-Object { $_.GetAttribute('Name') })
    Add-Check -Name 'Desktop-only package' -Passed ($targetNames.Count -eq 1 -and $targetNames[0] -eq 'Windows.Desktop') -Details "target families: $($targetNames -join ', ')"
    Add-Check -Name 'Minimum Windows version' -Passed ($targetFamilies[0].GetAttribute('MinVersion') -eq '10.0.17763.0') -Details "expected 10.0.17763.0; actual $($targetFamilies[0].GetAttribute('MinVersion'))"
}

$languages = @($manifest.SelectNodes('/f:Package/f:Resources/f:Resource', $ns) | ForEach-Object { $_.GetAttribute('Language') })
$supportedLanguages = @('en-US')
$languageDifferences = @(Compare-Object -ReferenceObject $supportedLanguages -DifferenceObject $languages)
Add-Check `
    -Name 'Package resource languages' `
    -Passed ($languageDifferences.Count -eq 0 -and $languages.Count -eq $supportedLanguages.Count) `
    -Details "expected: $($supportedLanguages -join ', '); declared: $($languages -join ', ')"

Add-Check -Name 'runFullTrust declaration' -Passed ($null -ne $fullTrustNode) -Details 'desktop app requires a restricted-capability declaration and Partner Center explanation'
Add-Check -Name 'tokenusage.exe execution alias' -Passed ($null -ne $aliasNode) -Details 'the packaged CLI alias must remain declared'

$requiredAssets = @(
    'Assets\StoreLogo.png',
    'Assets\Square150x150Logo.png',
    'Assets\Square44x44Logo.png',
    'Assets\Wide310x150Logo.png',
    'Assets\SplashScreen.png'
)
foreach ($relativeAsset in $requiredAssets) {
    $assetPath = Join-Path $appProjectRoot $relativeAsset
    $assetDirectory = Split-Path -Parent $assetPath
    $assetName = [System.IO.Path]::GetFileNameWithoutExtension($assetPath)
    $assetExtension = [System.IO.Path]::GetExtension($assetPath)
    $qualifiedAssets = @()
    if (Test-Path -LiteralPath $assetDirectory -PathType Container) {
        $qualifiedAssets = @(Get-ChildItem -LiteralPath $assetDirectory -File | Where-Object {
            $_.Name.StartsWith("$assetName.", [StringComparison]::OrdinalIgnoreCase) `
                -and $_.Extension.Equals($assetExtension, [StringComparison]::OrdinalIgnoreCase)
        })
    }
    $assetExists = (Test-Path -LiteralPath $assetPath -PathType Leaf) -or $qualifiedAssets.Count -gt 0
    $assetSources = if (Test-Path -LiteralPath $assetPath -PathType Leaf) {
        Get-RepoRelativePath -Path $assetPath
    }
    elseif ($qualifiedAssets.Count -gt 0) {
        ($qualifiedAssets | ForEach-Object { Get-RepoRelativePath -Path $_.FullName }) -join ', '
    }
    else {
        'none'
    }
    Add-Check `
        -Name "Package asset $relativeAsset" `
        -Passed $assetExists `
        -Details "source asset: $assetSources"
}

Test-ExactText -Name 'Documented PFN' -Actual $storeIdentity.packageIdentity.packageFamilyName -Expected 'GVASTETHECREATOR.TokenUsage_h2dcbfhqhrgv8'
Test-ExactText -Name 'Documented Package SID' -Actual $storeIdentity.packageIdentity.packageSid -Expected 'S-1-15-2-1053362801-2461558768-657845133-3595218920-1529770649-1636304934-4044864160'
Test-ExactText -Name 'Documented Store ID' -Actual $storeIdentity.store.productId -Expected '9NWX6M53B36K'

$manifestXmlText = $manifest.OuterXml
Add-Check -Name 'PFN not written into manifest' -Passed ($manifestXmlText.IndexOf($storeIdentity.packageIdentity.packageFamilyName, [StringComparison]::Ordinal) -lt 0) -Details 'PFN is derived and must remain documentation-only'
Add-Check -Name 'Package SID not written into manifest' -Passed ($manifestXmlText.IndexOf($storeIdentity.packageIdentity.packageSid, [StringComparison]::Ordinal) -lt 0) -Details 'Package SID is derived and must remain documentation-only'
Add-Check -Name 'No legacy publisher placeholder' -Passed ($manifestXmlText.IndexOf('CN=AppPublisher', [StringComparison]::Ordinal) -lt 0) -Details 'legacy placeholder must not return'

$result = [ordered]@{
    schema = 'tokenusage.store-readiness.v1'
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    repository = 'gvastethecreator/tokenusage'
    storeId = $storeIdentity.store.productId
    manifest = Get-RepoRelativePath -Path $manifestPath
    passed = $errors.Count -eq 0
    checks = $checks
}

if ($EvidencePath) {
    $resolvedEvidencePath = if ([System.IO.Path]::IsPathRooted($EvidencePath)) {
        $EvidencePath
    }
    else {
        Join-Path $repoRoot $EvidencePath
    }
    $evidenceDirectory = Split-Path -Parent $resolvedEvidencePath
    if ($evidenceDirectory) {
        New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedEvidencePath -Encoding utf8
    Write-Host "Evidence: $resolvedEvidencePath" -ForegroundColor DarkGray
}

if ($errors.Count -gt 0) {
    Write-Host ''
    Write-Host 'STORE READINESS FAILED' -ForegroundColor Red
    foreach ($errorMessage in $errors) {
        Write-Host " - $errorMessage" -ForegroundColor Red
    }
    exit 1
}

Write-Host ''
Write-Host "STORE READINESS PASSED ($($checks.Count) checks)" -ForegroundColor Green
Write-Host "Identity: $($storeIdentity.packageIdentity.name)"
Write-Host "Publisher: $($storeIdentity.packageIdentity.publisher)"
Write-Host "Store ID: $($storeIdentity.store.productId)"
