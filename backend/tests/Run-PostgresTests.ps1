[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repoRoot = (Resolve-Path (Join-Path $backendRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($env:METRO_TEST_POSTGRES_CONNECTION_STRING)) {
    throw 'METRO_TEST_POSTGRES_CONNECTION_STRING must target a dedicated PostgreSQL test database.'
}
if ([string]::IsNullOrWhiteSpace($env:METRO_TEST_POSTGRES_DATABASE) -or
    -not $env:METRO_TEST_POSTGRES_DATABASE.StartsWith('metro_test_', [StringComparison]::Ordinal)) {
    throw 'METRO_TEST_POSTGRES_DATABASE must start with metro_test_.'
}
if ($env:METRO_TEST_POSTGRES_REQUIRED -ne '1') {
    throw 'METRO_TEST_POSTGRES_REQUIRED=1 is required for an acceptance run.'
}
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('metro-test-results-' + [Guid]::NewGuid().ToString('N'))
}

& (Join-Path $PSScriptRoot 'Test-CiCredentialPolicy.ps1') -RepositoryRoot $repoRoot

Push-Location -LiteralPath $backendRoot
try {
    $arguments = @(
        'test', 'Metro.sln',
        '--configuration', 'Release',
        '--logger', 'trx;LogFilePrefix=backend',
        '--results-directory', $ResultsDirectory
    )
    if ($NoBuild) { $arguments += '--no-build' }
    if ($NoRestore) { $arguments += '--no-restore' }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }

    $resultFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File)
    if ($resultFiles.Count -eq 0) { throw 'No test results were produced.' }

    $totalPassed = 0
    $postgresPassed = 0
    foreach ($resultFile in $resultFiles) {
        [xml]$result = [IO.File]::ReadAllText($resultFile.FullName)
        foreach ($counter in $result.GetElementsByTagName('Counters')) {
            if ([int]$counter.failed -ne 0 -or [int]$counter.notExecuted -ne 0) {
                throw 'Acceptance requires zero failed tests and zero skipped tests.'
            }
            $totalPassed += [int]$counter.passed
        }
        foreach ($testResult in $result.GetElementsByTagName('UnitTestResult')) {
            if ($testResult.testName -match '\.(PostgresLiveIntegrationTests|TicketingFinancePostgresTests|AuthEndpointsTests|CatalogEndpointsTests|ProfileEndpointsTests|BookingEndpointsTests|OperationsEndpointsTests|TicketEndpointsTests|GateEndpointsTests|PaymentEndpointsTests|PaymentOwnershipTests|Be09PaymentIntegrationTests|FareMappingPostgresTests|FareQuoteEndpointsTests|AdminProductEndpointsTests|AdminFareEndpointsTests|AdminEligibilityPolicyEndpointsTests|AdminNetworkEndpointsTests|AdminScheduleEndpointsTests|AdminTripEndpointsTests|AdminTripLoadEndpointsTests|AdminNoticeEndpointsTests)\.' -and
                $testResult.outcome -eq 'Passed') {
                $postgresPassed++
            }
        }
    }

    if ($postgresPassed -eq 0) { throw 'No PostgreSQL acceptance tests were executed.' }
    Write-Output "Acceptance: $totalPassed passed, $postgresPassed PostgreSQL tests passed, zero skipped."
    Write-Output "Test results: $ResultsDirectory"
}
finally {
    Pop-Location
}
