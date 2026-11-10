# Spousti kontroly projektu. Soubor je jen v ASCII, Windows PowerShell 5.1 cte UTF-8 bez BOM spatne.
#   Fast: preklad a testy bez site a bez prohlizece, spousti je i Stop hook.
#   Full: navic testy v prohlizeci, zkusebni publikace a detektor duplicit (jen varovani).
#   Smoke: jen testy proti produkcnimu API (klient a vykreslena stranka), cteni s tokenem z user-secrets. Pri uzavreni milniku.
param(
    [ValidateSet('Fast', 'Full', 'Smoke')]
    [string]$Mode = 'Fast'
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "== $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        Write-Host "CHYBA: $Name (kod $LASTEXITCODE)"
        exit 1
    }
}

Invoke-Step 'Preklad' { dotnet build AvailabilityView.slnx --verbosity quiet }
Invoke-Step 'Test Stop hooku' { node --test .claude/hooks/locked-build.test.mjs }

if ($Mode -eq 'Smoke') {
    Invoke-Step 'Testy proti API' { dotnet test --project tests/Reservanto.ApiClient.Tests/Reservanto.ApiClient.Tests.csproj --no-build -- --filter-trait 'Category=Smoke' }
    Invoke-Step 'Stranka nad produkcnim API' { dotnet test --project tests/AvailabilityView.Web.Tests/AvailabilityView.Web.Tests.csproj --no-build -- --filter-trait 'Category=Smoke' }
    Write-Host 'OK: kontroly Smoke prosly'
    exit 0
}

# Exit 8 = v projektu po filtru nezbyl zadny test, to je u testu v prohlizeci spravne.
Invoke-Step 'Testy' { dotnet test --solution AvailabilityView.slnx --no-build -- --filter-not-trait 'Category=E2E' --filter-not-trait 'Category=Smoke' --ignore-exit-code 8 }

if ($Mode -eq 'Full') {
    Invoke-Step 'Testy v prohlizeci' { dotnet test --project tests/AvailabilityView.Web.E2E.Tests/AvailabilityView.Web.E2E.Tests.csproj --no-build }
    Invoke-Step 'Publikace' { dotnet publish src/AvailabilityView.Web/AvailabilityView.Web.csproj --configuration Release --output artifacts/publish --verbosity quiet }

    Write-Host '== Duplicity (jen varovani)'
    npx --yes jscpd@5.4.2 --config .jscpd.json
    if ($LASTEXITCODE -ne 0) {
        Write-Host "VAROVANI: detektor duplicit skoncil kodem $LASTEXITCODE"
    }
}

Write-Host "OK: kontroly $Mode prosly"
exit 0
