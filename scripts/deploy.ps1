# Розгортання всіх трьох БД з нуля.
# Запуск з кореня репозиторію:  powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1
$ErrorActionPreference = "Stop"
$sa = "Cinema_Dev_2026!"

function Invoke-Sql([string]$file) {
    Write-Host ">> sqlcmd $file" -ForegroundColor Cyan
    docker exec cinema-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $sa -C -I -b -W -f 65001 -i $file
    if ($LASTEXITCODE -ne 0) { throw "Помилка у $file" }
}

function Invoke-Mongo([string]$file) {
    Write-Host ">> mongosh $file" -ForegroundColor Green
    docker exec cinema-mongo mongosh --quiet $file
    if ($LASTEXITCODE -ne 0) { throw "Помилка у $file" }
}

docker compose up -d --wait
if ($LASTEXITCODE -ne 0) { throw "Не вдалося запустити контейнери (чи запущений Docker Desktop?)" }

# Проєкт №1 — Бронювання (SQL-скрипти)
foreach ($f in "001_create_database", "002_schema", "003_procedures", "004_seed", "005_security") {
    Invoke-Sql "/scripts/booking/$f.sql"
}
Write-Host "Повторний запуск seed — кількість записів має лишитися тією самою:" -ForegroundColor Yellow
Invoke-Sql "/scripts/booking/004_seed.sql"

# Проєкт №2 — Каталог (EF Core міграції + логін застосунку)
dotnet tool restore
$env:CINEMA_CATALOG_DB = "Server=localhost,1433;Database=CinemaCatalog;User Id=sa;Password=$sa;TrustServerCertificate=True"
dotnet ef database update --project src/Catalog/Catalog.Data
if ($LASTEXITCODE -ne 0) { throw "Помилка міграцій EF Core" }
Invoke-Sql "/scripts/catalog/catalog_security.sql"

# Проєкт №3 — Відгуки (MongoDB)
foreach ($f in "01_collections", "02_indexes", "03_seed", "03_seed") {   # seed двічі — перевірка ідемпотентності
    Invoke-Mongo "/scripts/$f.js"
}

Write-Host "`nГотово. Перевірки — див. README, розділ «Перевірка»." -ForegroundColor Yellow
