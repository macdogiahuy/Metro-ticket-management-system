[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
)

$ErrorActionPreference = 'Stop'
$paths = @(
    '.github/workflows/backend-ci.yml',
    'backend/tests/Run-PostgresMappingTests.ps1'
)
# Check only the workflow and disposable-database runner. Do not exclude either from secret scanning.
$literalCredentialPatterns = @(
    '(?i)^\s*(POSTGRES_PASSWORD|PGPASSWORD):\s*(?!\$\{\{)\S+',
    '(?i)\bPassword=(?!\$)[^;\s''"]+',
    '(?i)--env\s+[''"]?POSTGRES_PASSWORD=(?!\$)[^\s''"]+'
)
foreach ($relativePath in $paths) {
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $RepositoryRoot $relativePath))) {
        $lineNumber++
        foreach ($pattern in $literalCredentialPatterns) {
            if ($line -match $pattern) {
                # Report location only; never echo a possible credential.
                $location = $relativePath + ':' + $lineNumber
                throw "Hardcoded database credential at $location. Generate it at runtime or use a secret reference."
            }
        }
    }
}
Write-Output 'CI credential policy passed.'
