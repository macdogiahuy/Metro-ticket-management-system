[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repoRoot = (Resolve-Path (Join-Path $backendRoot '..')).Path
$containerName = 'metro-ef-test-' + [Guid]::NewGuid().ToString('N')
$databaseName = 'metro_be02a_test_' + [Guid]::NewGuid().ToString('N')
$previousConnection = $env:METRO_TEST_CONNECTION
$previousCoreConnection = $env:METRO_TEST_POSTGRES_CONNECTION_STRING
$previousCoreDatabase = $env:METRO_TEST_POSTGRES_DATABASE
$previousCoreRequired = $env:METRO_TEST_POSTGRES_REQUIRED
$previousDockerPassword = $env:POSTGRES_PASSWORD
$containerCreated = $false
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('metro-test-results-' + [Guid]::NewGuid().ToString('N'))
}

& (Join-Path $PSScriptRoot 'Test-CiCredentialPolicy.ps1') -RepositoryRoot $repoRoot

try {
    # Generate a new credential for every run; never store it in the repository or command arguments.
    $randomBytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) }
    finally { $generator.Dispose() }
    $testPassword = [BitConverter]::ToString($randomBytes).Replace('-', '').ToLowerInvariant()
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Output "::add-mask::$testPassword"
    }
    $env:POSTGRES_PASSWORD = $testPassword
    # This container has no persistent volumes and is removed at the end.
    docker run --detach --name $containerName --publish '127.0.0.1::5432' --env POSTGRES_USER=metro_test --env POSTGRES_PASSWORD --env "POSTGRES_DB=$databaseName" postgres:17-alpine
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the disposable PostgreSQL container.' }
    $containerCreated = $true

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        # The entrypoint's temporary initialization server accepts Unix sockets
        # before POSTGRES_DB exists. Only the final server accepts TCP connections.
        docker exec $containerName pg_isready -h 127.0.0.1 -U metro_test -d $databaseName *> $null
        if ($LASTEXITCODE -eq 0) {
            docker exec $containerName psql -U metro_test -d $databaseName -Atqc 'SELECT 1' *> $null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'PostgreSQL did not finish initialization with a usable test database within 60 seconds.' }

    $deployment = Join-Path $repoRoot 'infra/database/deploy.sql'
    docker cp $deployment "${containerName}:/tmp/metro-deploy.sql"
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy the SQL deployment.' }
    docker exec $containerName psql -v ON_ERROR_STOP=1 -U metro_test -d $databaseName -f /tmp/metro-deploy.sql
    if ($LASTEXITCODE -ne 0) { throw 'Test database deployment failed.' }

    $verification = Join-Path $repoRoot 'infra/database/scripts/09_verify.sql'
    docker cp $verification ($containerName + ':/tmp/metro-verify.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy the SQL schema verification.' }
    docker exec $containerName psql -v ON_ERROR_STOP=1 -U metro_test -d $databaseName -f /tmp/metro-verify.sql
    if ($LASTEXITCODE -ne 0) { throw 'Test database schema verification failed.' }

    $binding = docker port $containerName 5432/tcp
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Could not determine the temporary PostgreSQL port.'
    }
    $testPort = $Matches[1]
    $env:METRO_TEST_CONNECTION = "Host=127.0.0.1;Port=$testPort;Database=$databaseName;Username=metro_test;Password=$testPassword"
    $env:METRO_TEST_POSTGRES_CONNECTION_STRING = $env:METRO_TEST_CONNECTION
    $env:METRO_TEST_POSTGRES_DATABASE = $databaseName
    $env:METRO_TEST_POSTGRES_REQUIRED = '1'
    Push-Location -LiteralPath $backendRoot
    try {
        $testArguments = @('--logger', 'trx;LogFilePrefix=backend', '--results-directory', $ResultsDirectory)
        if ($NoBuild) { $testArguments += '--no-build' }
        dotnet test Metro.sln --configuration Release @testArguments
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
                if ($testResult.testName -match '\.(PostgresLiveIntegrationTests|TicketingFinancePostgresTests)\.' -and $testResult.outcome -eq 'Passed') {
                    $postgresPassed++
                }
            }
        }
        if ($postgresPassed -eq 0) { throw 'No PostgreSQL acceptance tests were executed.' }
        Write-Output "Acceptance: $totalPassed passed, $postgresPassed PostgreSQL tests passed, zero skipped."
        Write-Output "Test results: $ResultsDirectory"
    }
    finally { Pop-Location }
}
finally {
    $env:METRO_TEST_CONNECTION = $previousConnection
    $env:METRO_TEST_POSTGRES_CONNECTION_STRING = $previousCoreConnection
    $env:METRO_TEST_POSTGRES_DATABASE = $previousCoreDatabase
    $env:METRO_TEST_POSTGRES_REQUIRED = $previousCoreRequired
    $env:POSTGRES_PASSWORD = $previousDockerPassword
    if ($containerCreated) { docker rm --force $containerName | Out-Null }
}
