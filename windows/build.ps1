param(
    [ValidateSet('build', 'test', 'integration', 'preview', 'package', 'restore', 'models')]
    [string]$Action = 'package'
)
$ErrorActionPreference = 'Stop'
$compose = Join-Path $PSScriptRoot 'compose.yaml'
& docker compose -f $compose build tools
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Action -eq 'integration') {
    try {
        & docker compose -f $compose up --build --force-recreate -d fleet
        if ($LASTEXITCODE -ne 0) { throw 'Could not start the printer emulator.' }
        & docker compose -f $compose run --rm --use-aliases tools sh windows/scripts/build.sh integration
        $result = $LASTEXITCODE
    } finally {
        & docker compose -f $compose stop fleet
    }
    exit $result
}
& docker compose -f $compose run --rm tools sh windows/scripts/build.sh $Action
exit $LASTEXITCODE
