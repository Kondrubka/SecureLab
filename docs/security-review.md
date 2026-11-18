# Security review SecureLab — ЛР №6

**Рівень виконання:** «Добре».
**Період перевірок:** 17–18.11.2026.
**Початковий commit:** 526cd1178759f55f32de2f4e6d7f5ffe94a3a790.
**Гілка:** lab/6-hardening-review.
**Git-реліз:** merge-коміт e510c1b; тег v1.0.0 опубліковано.

## 1. Executive summary

Проведено огляд безпеки навчального застосунку SecureLab:
ASP.NET Core API, браузерного клієнта, PostgreSQL,
механізмів автентифікації, авторизації, журналювання,
конфігурації та NuGet-залежностей.

Виявлено й виправлено дві основні знахідки:
F-01 — відсутність явної CSRF-перевірки для захищених операцій;
F-02 — відсутність окремого rate limiting для пошуку інцидентів.

Додатково виправлено некоректну відповідь невідомих
API-маршрутів, прибрано жорстко записані облікові дані,
ротовано локальний пароль PostgreSQL та додано audit logging.

Фінальна підтверджена регресія: 44/44 PASS,
0 failed, 0 skipped, exit code 0.

Основні залишкові ризики: стара конфігурація в Git-історії,
локальний in-memory rate limiter, відсутність гарантованого
довготривалого сховища аудиту та неперевірене production-розгортання.

## 2. Scope та exclusions

У межах огляду:
- ASP.NET Core API та браузерні HTML/JavaScript-клієнти;
- cookie authentication, authorization та CSRF;
- перевірки введення і доступу до інцидентів;
- rate limiting, CORS, CSP та HTTP security headers;
- обробка помилок API;
- NuGet-залежності та конфігураційні секрети;
- структуроване журналювання операцій;
- поточні відстежувані й нові файли проєкту.

Git-історію переглянуто вибірково для двох файлів
із раніше жорстко записаними рядками підключення.
Повний історичний secret scan не проводився.

Поза межами огляду: зовнішня інфраструктура,
реальне production-розгортання, навантажувальні DoS-тести
та перевірка розподіленого rate limiting.

## 3. System overview і trust boundaries

Компоненти: браузерний клієнт, ASP.NET Core Minimal API,
ASP.NET Core Identity, Entity Framework Core, Npgsql,
PostgreSQL у Docker, Git та конфігурація середовища.

| Межа довіри | Ризик | Контроль і перевірка |
|---|---|---|
| Browser → API | CSRF, XSS | Antiforgery, CSP, безпечні DOM-операції; REG-CSRF |
| Authenticated user → API | IDOR, зміна чужих даних | Authorization, IncidentAccess, наявні A03/A03R |
| API → PostgreSQL | SQL injection, цілісність | EF Core та параметризовані запити; REG-SQLI-01 PASS для перевіреного набору запитів |
| Client → Search API | Виснаження ресурсів | search-limit; REG_RATE_SEARCH_01/02 |
| Repository → runtime | Витік секретів | .gitignore, змінні середовища, ротація пароля |
| API → logs | Відсутність аудиту | SecureLab.Audit; REG-AUDIT-02/03/04 |

Детальна модель загроз: docs/threat-model.md.

## 4. Methodology та test environment

Початкова контрольна точка: 33/33 PASS.
Платформа: .NET SDK 10.0.401, net10.0.
Основний профіль перевірок: Development.
Тестовий клієнт: xUnit, HttpClient, WebApplicationFactory.
База даних: PostgreSQL у Docker, 127.0.0.1:55432.

Методика: огляд коду, baseline-запити, визначення ризиків,
виправлення, негативні й позитивні повторні перевірки,
інтеграційні тести та аналіз журналів.

Перевірено окремий сценарій Production-обробки помилки 500.
Повноцінне production-розгортання не тестувалося.

Фінальний прогін:
dotnet test SecureLab.sln -c Release --logger "console;verbosity=minimal"

Результат: 44/44 PASS, exit code 0.

## 5. Findings summary

| ID | Знахідка | Ризик | Статус | Перевірка |
|---|---|---|---|---|
| F-01 | Відсутня явна antiforgery-перевірка cookie-authenticated mutations | Небажана зміна стану | Fixed | REG-CSRF-01/02/03/04 |
| F-02 | Відсутній окремий пошуковий rate limiter | Надмірне навантаження на API і БД | Fixed | REG_RATE_SEARCH_01/02 |

Для F-01 початковий logout без CSRF-токена повертав HTTP 204.
Після виправлення відсутній або неправильний токен
дає HTTP 400 без виконання захищеної операції.

Сам початковий результат не доводить можливість
експлуатації з довільного стороннього origin:
браузерні обмеження cookie також впливають на ризик.

Для F-02 початково 15 пошукових запитів дали
15 відповідей HTTP 200 і 0 відповідей HTTP 429.
Після виправлення: 5 відповідей HTTP 200,
10 відповідей HTTP 429; після 11 секунд доступ відновився.

## 6. Detailed findings

F-01: docs/findings/F-01-short-name.md.
F-02: docs/findings/F-02-search-rate-limit.md.

Для обох знахідок задокументовано початковий стан,
заходи виправлення та повторні перевірки.

## 7. Regression results

| Сценарій | Перевірка | Результат |
|---|---|---|
| Baseline | Початковий стан проєкту | 33/33 PASS |
| REG-CSRF | Відсутній, неправильний і правильний токен | PASS |
| REG_RATE_SEARCH_01 | Квота, HTTP 429, відновлення | PASS |
| REG_RATE_SEARCH_02 | Незалежні лічильники Alice/Bob | PASS |
| REG-CORS-01 | Дозволений origin, сторонній origin, preflight | PASS |
| REG-API-404-01 | Невідомий API → 404 ProblemDetails | PASS |
| REG-ERROR-500-01 | Безпечна відповідь на помилку БД | PASS |
| REG-AUDIT-02 | Успішний і відхилений logout | PASS |
| REG-AUDIT-03 | Create, update, delete | PASS |
| REG-AUDIT-04 | Triage та історія статусу | PASS |
| Full regression | Після всіх змін і ротації пароля | 44/44 PASS |

Наявні тести також охоплюють авторизацію,
відмову в доступі до чужих інцидентів
та перевірки небезпечних DOM-операцій.

Цільовий REG-SQLI-01 виконано успішно для обраного набору запитів.

## 8. Hardening controls

Реалізовано:
- Antiforgery через CsrfEndpointFilter і X-SecureLab-CSRF;
- search-limit: Fixed Window, 5 запитів за 10 секунд;
- partition за UserId або IP;
- наявний окремий login-limit;
- CORS для дозволеного Development origin;
- X-Content-Type-Options: nosniff;
- Referrer-Policy: no-referrer;
- CSP для перевіреної HTML-сторінки;
- ProblemDetails для невідомого API-маршруту;
- структурований SecureLab.Audit.

Матриця змін стану:

| Method + route | Cookie auth для доступу | CSRF | Перевірка |
|---|---|---|---|
| POST /api/auth/register | Ні | Поза захищеним cookie-периметром | Окремий аналіз ризику |
| POST /api/auth/login | Ні | Поза захищеним cookie-периметром | Login tests, rate limiting |
| POST /api/auth/logout | Так | Explicit | REG-CSRF-01 |
| POST /api/incidents | Так | Explicit | REG-CSRF-02 |
| PUT /api/incidents/{id} | Так | Explicit | REG-CSRF-03 |
| DELETE /api/incidents/{id} | Так | Explicit | REG-CSRF-03 |
| POST /api/incidents/{id}/triage | Так | Explicit | REG-CSRF-04 |

Усі п'ять захищених операцій мають
SecurityAuditEndpointFilter.

## 9. Dependency and secret audit

DEP-AUDIT-01:
Перевірено прямі й транзитивні NuGet-залежності.
Для SecureLab.Api та SecureLab.Api.Tests
відомих вразливих пакетів не виявлено.
Результат залежить від джерел і дати перевірки.

SEC-SECRET-01:
Виявлено два жорстко записані рядки підключення.
Їх прибрано з поточної конфігурації та коду.
Підключення перенесено до змінної середовища.

SEC-SECRET-02:
В історії Git виявлено старі облікові дані.
Пароль локального користувача PostgreSQL ротовано.
Регресія після ротації: 44/44 PASS.

## 10. Residual risks

| Ризик | Стан | Наступна дія |
|---|---|---|
| Старий пароль у Git-історії | Локальний пароль ротовано, історія збережена | Не використовувати старі credentials; перевірити інші місця повторного використання |
| In-memory rate limiter | Прийнято для одного локального API | Для кількох інстансів потрібен спільний стан |
| Консольний audit logging | Події підтверджено | Для production додати захищене довготривале сховище |
| Реальна production-інфраструктура | Не перевірена | Окремий deployment security review |
| Повний historical secret scan | Не виконано | Виконати окремо за потреби |
| Цільова SQLi-регресія | REG-SQLI-01 PASS | Повторювати після змін пошуку |

## 11. Conclusion

Для рівня «Добре» реалізовано й повторно перевірено
дві основні знахідки F-01 та F-02.

Додатково перевірено CORS, захисні заголовки,
обробку помилок, NuGet-залежності, секрети
та журналювання захищених операцій.

Підсумкова автоматизована регресія: 44/44 PASS.

Основний реліз інтегровано в main і позначено тегом v1.0.0. Завершальні уточнення аудиту оформлюються окремим комітом.

## 12. Evidence appendix

Нижче наведено фактичні записи перевірок:
REG-CORS-01, DEP-AUDIT-01, REG-AUDIT-01/02/03/04,
SEC-SECRET-01/02 та відповідні результати.

### REG-CORS-01 — CORS policy

Дата: 17.11.2026.
Середовище: Development.
Результат: PASS.

GET /health з Origin http://localhost:4173:
HTTP 200.
Access-Control-Allow-Origin: http://localhost:4173.
Access-Control-Allow-Credentials: true.

GET /health з Origin http://evil.example:
HTTP 200.
Access-Control-Allow-Origin відсутній.

OPTIONS /api/incidents з дозволеного Origin:
HTTP 204.
Access-Control-Allow-Methods: POST.
Access-Control-Allow-Headers:
content-type,x-securelab-csrf.
Access-Control-Allow-Credentials: true.

Висновок: CORS дозволяє налаштований
Development Origin та не надає дозволу
сторонньому Origin.

Примітка: CORS не є заміною автентифікації,
авторизації або CSRF-захисту.

### DEP-AUDIT-01 — NuGet dependency vulnerability audit

Дата: 18.11.2026.
Результат: PASS.

Команда:
dotnet package list --project SecureLab.sln --vulnerable --include-transitive

Перевірені проєкти:
- SecureLab.Api
- SecureLab.Api.Tests

Прямі та транзитивні NuGet-залежності перевірено.
За використаними джерелами відомих вразливих пакетів
не виявлено.

Джерела:
- https://api.nuget.org/v3/index.json
- Локальне джерело Microsoft SDK NuGetPackages

Exit code: 0.

Обмеження: результат актуальний на дату аудиту
та залежить від доступних джерел інформації
про вразливості.

### REG-AUDIT-01 — Security audit logging

Дата: 18.11.2026.
Результат: PASS для auth.logout.

Реалізовано SecurityAuditEndpointFilter.
Фільтр підключено до п'яти операцій:
- POST /api/auth/logout
- POST /api/incidents
- PUT /api/incidents/{id}
- DELETE /api/incidents/{id}
- POST /api/incidents/{id}/triage

У журналі SecureLab.Audit підтверджено подію:
Action=auth.logout
Result=success

Запис містить SubjectId, Action, ResourceId,
Result, OccurredAtUtc та TraceId.

Паролі, cookies та CSRF-токени не входять
до шаблону повідомлення журналювання.

Обмеження:
- Реальну роботу журналювання решти чотирьох
  операцій необхідно підтвердити тестами.
- Консольне журналювання саме по собі
  не забезпечує довготривалого зберігання
  та захисту audit trail.

### SEC-SECRET-01 — Configuration and secrets review

Дата: 18.11.2026.
Результат: Mitigated; historical review pending.

Перевірено:
- Налаштування .gitignore для .env
  та appsettings.Local.json.
- Відстежувані конфігураційні файли.
- Згадки потенційних секретів у коді.
- Нові файли, які ще не додані до Git.

Виявлено:
- Пароль у рядку підключення
  appsettings.Development.json.
- Резервний рядок підключення з паролем
  у SecureLabDesignTimeDbContextFactory.cs.

Виправлення:
- ConnectionStrings:SecureLab прибрано
  з відстежуваної Development-конфігурації.
- Жорстко записаний резервний рядок
  підключення видалено з design-time factory.
- Підключення передається через змінну
  середовища ConnectionStrings__SecureLab.

Регресія після виправлень: 41/41 PASS.

Обмеження:
- Попередні Git-коміти можуть містити
  старі значення підключення.
- Потрібні окрема перевірка історії Git
  та оцінка необхідності ротації пароля.
- Пошук за ключовими словами не є
  повним автоматизованим secret scanning.

### REG-AUDIT-02 — Automated logout audit regression

Дата: 18.11.2026.
Результат: PASS.

Автоматизований інтеграційний тест:
AuditTrailTests.REG_AUDIT_02_Logout_LogsOnlySuccessfulAction.

Перевірено:
- Logout без CSRF-токена повертає HTTP 400.
- Відхилена операція не створює запис
  про успішний logout.
- Logout із правильним токеном повертає HTTP 204.
- У категорії SecureLab.Audit виникає запис
  Action=auth.logout, Result=success.
- Запис містить SubjectId і TraceId.
- Після виходу GET /api/me повертає HTTP 401.

Окремий тест: 1/1 PASS.
Повна регресія: 42/42 PASS, 0 failed, 0 skipped.

Висновок: журналювання успішного logout
підтверджено автоматизованим тестом.

### REG-AUDIT-03 — Incident change audit logging

Дата: 18.11.2026.
Результат: PASS.

Інтеграційним тестом перевірено:
- incident.create;
- incident.update;
- incident.delete.

Успішні операції створюють записи SecureLab.Audit.
Для оновлення і видалення перевірено ResourceId.
Запити без CSRF-токена відхиляються HTTP 400
і не створюють записів про успішне виконання.

### REG-AUDIT-04 — Incident triage audit logging

Дата: 18.11.2026.
Результат: PASS.

Аналітик Morgan виконує triage інциденту.
Успішна операція повертає HTTP 204.
У журналі зафіксовано incident.triage,
ResourceId, Result=success і TraceId.
У базі даних створюється запис історії статусу.
Запит без CSRF-токена повертає HTTP 400
і не журналюється як успішний.

Окремий запуск AuditTrailTests: 3/3 PASS.

### SEC-SECRET-02 — Historical credential rotation

Дата: 18.11.2026.
Результат: Mitigated.

Під час перевірки Git-історії встановлено,
що початковий коміт 2119b0f містить
жорстко записаний рядок підключення
до навчальної PostgreSQL.

Виконані заходи:
- Прибрано рядок підключення з паролем
  із appsettings.Development.json.
- Видалено резервний рядок підключення
  з SecureLabDesignTimeDbContextFactory.cs.
- Виконано ротацію пароля користувача
  securelab у локальній PostgreSQL.
- Новий пароль передається застосунку
  через змінну середовища.
- Значення нового пароля не записувалося
  у файли репозиторію.

Перевірка після ротації:
dotnet test SecureLab.sln -c Release

Результат: 44/44 PASS.
Failed: 0. Skipped: 0. Exit code: 0.

Залишковий ризик:
Старий рядок підключення залишається
в історичних Git-комітах. Історію
репозиторію не переписували.
Для локальної навчальної БД пароль
ротовано; для будь-яких інших систем
повторно використані облікові дані
потребують окремої ротації.

### REG-SQLI-01 — SQL injection and LIKE escaping (PASS)

Дата: 18.11.2026.
Середовище: Development, PostgreSQL, localhost:5080.
Маршрут: GET /api/incidents/search.

| Сценарій | HTTP | Кількість записів |
|---|---|---|
| USB | 200 | 1 |
| Контроль без збігів | 200 | 0 |
| SQLi: OR 1=1 із коментарем | 200 | 0 |
| LIKE: % | 200 | 0 |
| LIKE: _ | 200 | 0 |

Пошук реалізовано через EF.Functions.ILike.
Символи LIKE екрануються.
Єдиний виявлений ExecuteSqlRawAsync містить
статичний TRUNCATE без введення користувача.

Висновок: PASS для перевірених сценаріїв.
Повний аудит усіх можливих SQLi не проводився.

### DEP-AUDIT-02 — Dependency decisions

Дата перевірки: 18.11.2026.

Перевірено прямі та транзитивні NuGet-залежності
проєктів SecureLab.Api та SecureLab.Api.Tests.

| Пакет або група | Встановлена версія | Рішення |
|---|---|---|
| ASP.NET Core Identity EF Core | 10.0.10 | Залишити |
| ASP.NET Core OpenAPI | 10.0.10 | Залишити |
| Entity Framework Core / Design / Relational | 10.0.10 | Залишити |
| Microsoft.OpenApi | 2.7.5 | Залишити |
| Npgsql EF Core PostgreSQL | 10.0.3 | Залишити |
| Scalar.AspNetCore | 2.16.16 | Залишити |
| coverlet.collector | 6.0.4 | Залишити |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.10 | Залишити |
| Microsoft.NET.Test.Sdk | 17.14.1 | Залишити |
| xunit | 2.9.3 | Залишити |
| xunit.runner.visualstudio | 3.1.4 | Залишити |
| Транзитивні залежності | Згідно з NuGet inventory | Залишити |

Підстава рішення:
dotnet package list --project SecureLab.sln
--vulnerable --include-transitive

Обидва проєкти: відомих вразливих пакетів
за використаними джерелами не виявлено.

Exit code: 0.

Рішення: позапланове оновлення пакетів
лише за результатом цього аудиту не потрібне.

Обмеження: перевірка не гарантує відсутності
невідомих вразливостей. Повторний аудит
потрібний після змін залежностей.

### SEC-SECRET-03 — Tracked-file secret scan

Дата: 18.11.2026.
Обсяг: 115 tracked-файлів поточної версії.

Широкий пошук:
- 44 файли зі збігами;
- 229 рядків зі згадками password, secret,
  token, connectionstring та споріднених термінів.

Збіги не прирівнювалися автоматично до витоків.

Цільова класифікація:
- 14 присвоєнь Password у коді й тестах;
- 13 присвоєнь зі змінними або виразами;
- 1 літерал у LoginRateLimitTests.cs,
  використаний у тесті невдалих входів;
- 0 збігів приватних ключів за PEM-шаблоном;
- 0 збігів довгих Bearer-токенів за шаблоном;
- 0 присвоєнь Pwd за перевіреним шаблоном.

Перевірено appsettings.json,
appsettings.Development.json,
SecureLabDesignTimeDbContextFactory.cs
та конфігурацію Docker Compose.

Раніше виявлені паролі прибрано з поточних
файлів. Локальний пароль PostgreSQL ротовано.

У infra/compose.yaml значення POSTGRES_PASSWORD
замінено на обов'язкову змінну середовища
SECURELAB_POSTGRES_PASSWORD.

Перевірка:
docker compose -f infra/compose.yaml config --quiet

Результат: PASS, exit code 0.

Залишковий ризик:
Історичні коміти не переписувалися.
Перевірка за шаблонами не гарантує
виявлення всіх можливих форматів секретів.

### REG-LOG-01 — Logging source review

Дата: 18.11.2026.

Переглянуто 11 місць журналювання
в ASP.NET Core API за обраними шаблонами.

Автоматичний аналіз найближчих рядків:
- 10 місць без згадок чутливих полів;
- 1 місце потребувало ручної перевірки.

Окремо перевірено IncidentEndpoints.cs
поблизу LogWarning для відхиленого triage.

Передаються ідентифікатори користувача
та ресурсу, час і TraceId.
Передачу пароля, cookie або токена
в цьому виклику не встановлено.

SecurityAuditEndpointFilter журналює
успішні захищені операції.
Наявні автоматизовані перевірки:
REG-AUDIT-02, REG-AUDIT-03, REG-AUDIT-04.

Висновок:
Перевірені виклики журналювання
не показали явного запису облікових секретів.

Обмеження:
Статичний перегляд викликів не замінює
повної перевірки фактичних runtime-журналів.
