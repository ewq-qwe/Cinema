# Розгортання всіх трьох БД з нуля.
# Запуск з кореня репозиторію:  powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1
$ErrorActionPreference = "Stop"
$sa = "Cinema_Dev_2026!"

function Invoke-Sql([string]$file) {
    Write-Host ">> sqlcmd $file" -ForegroundColor Cyan
    docker exec cinema-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $sa -C -I -b -W -f 65001 -i "/scripts/$file"
    if ($LASTEXITCODE -ne 0) { throw "Помилка у $file" }
}

function Invoke-Mongo([string]$file) {
    Write-Host ">> mongosh $file" -ForegroundColor Green
    docker exec cinema-mongo mongosh --quiet "/scripts/$file"
    if ($LASTEXITCODE -ne 0) { throw "Помилка у $file" }
}

docker compose up -d --wait
if ($LASTEXITCODE -ne 0) { throw "Не вдалося запустити контейнери (чи запущений Docker Desktop?)" }

# Проєкт №1 — Бронювання (CinemaBooking)
foreach ($f in "schema", "procedures", "seed", "security") { Invoke-Sql "p1/$f.sql" }

# Проєкт №2 — Каталог (CinemaCatalog)
foreach ($f in "schema", "seed", "security") { Invoke-Sql "p2/$f.sql" }

# Проєкт №3 — Відгуки (cinema_reviews)
foreach ($f in "collections", "seed") { Invoke-Mongo "p3/$f.js" }

# Перевірка ідемпотентності: seed-скрипти вдруге — кількість записів не змінюється
Write-Host "`nПовторний запуск seed-скриптів (дублікатів бути не повинно):" -ForegroundColor Yellow
Invoke-Sql "p1/seed.sql"
Invoke-Sql "p2/seed.sql"
Invoke-Mongo "p3/seed.js"

Write-Host "`nГотово. Перевірки — див. README, розділ «Перевірка»." -ForegroundColor Yellow
