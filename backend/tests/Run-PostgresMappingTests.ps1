[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$backendRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repoRoot = (Resolve-Path (Join-Path $backendRoot '..')).Path
$containerName = 'metro-ef-test-' + [Guid]::NewGuid().ToString('N')
$databaseName = 'metro_be02a_test_' + [Guid]::NewGuid().ToString('N')
$previousConnection = $env:METRO_TEST_CONNECTION
$previousCoreConnection = $env:METRO_TEST_POSTGRES_CONNECTION_STRING
$previousCoreDatabase = $env:METRO_TEST_POSTGRES_DATABASE
$previousCoreRequired = $env:METRO_TEST_POSTGRES_REQUIRED
$containerCreated = $false

try {
    # This container has no persistent volumes and is removed at the end.
    docker run --detach --name $containerName --publish '127.0.0.1::5432' --env POSTGRES_USER=metro_test --env POSTGRES_PASSWORD=metro_test --env "POSTGRES_DB=$databaseName" postgres:17-alpine
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the disposable PostgreSQL container.' }
    $containerCreated = $true

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        docker exec $containerName pg_isready -U metro_test -d $databaseName *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'PostgreSQL did not become ready within 60 seconds.' }

    $deployment = Join-Path $repoRoot 'infra/database/deploy.sql'
    docker cp $deployment "${containerName}:/tmp/metro-deploy.sql"
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy the SQL deployment.' }
    docker exec $containerName psql -v ON_ERROR_STOP=1 -U metro_test -d $databaseName -f /tmp/metro-deploy.sql
    if ($LASTEXITCODE -ne 0) { throw 'Test database deployment failed.' }

    $binding = docker port $containerName 5432/tcp
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Could not determine the temporary PostgreSQL port.'
    }
    $testPort = $Matches[1]
    $env:METRO_TEST_CONNECTION = "Host=127.0.0.1;Port=$testPort;Database=$databaseName;Username=metro_test;Password=metro_test"
    $env:METRO_TEST_POSTGRES_CONNECTION_STRING = $env:METRO_TEST_CONNECTION
    $env:METRO_TEST_POSTGRES_DATABASE = $databaseName
    $env:METRO_TEST_POSTGRES_REQUIRED = '1'
    Push-Location -LiteralPath $backendRoot
    try {
        dotnet test Metro.sln --configuration Release
        if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
    }
    finally { Pop-Location }
}
finally {
    $env:METRO_TEST_CONNECTION = $previousConnection
    $env:METRO_TEST_POSTGRES_CONNECTION_STRING = $previousCoreConnection
    $env:METRO_TEST_POSTGRES_DATABASE = $previousCoreDatabase
    $env:METRO_TEST_POSTGRES_REQUIRED = $previousCoreRequired
    if ($containerCreated) { docker rm --force $containerName | Out-Null }
}
