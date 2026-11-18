# F-01. Відсутня CSRF-перевірка авторизованих змінювальних запитів

**Статус:** Fixed — retest PASS, 17.11.2026
**Актив:** автентифікована сесія користувача та дані інцидентів
**Компонент:** ASP.NET Core Minimal API / cookie authentication
**Попередній пріоритет:** P2

## Передумови і межі огляду

- Початковий commit: 526cd1178759f55f32de2f4e6d7f5ffe94a3a790.
- Середовище: Development, http://localhost:5080.
- Браузер: Chrome, DevTools Console.
- Користувач: авторизований навчальний обліковий запис.
- Перевірений маршрут: POST /api/auth/logout.
- Межа довіри: браузер -> API.

## Відтворення

1. Виконати вхід через /auth-flow.html.
2. Викликати GET /api/me: HTTP 200.
3. Виконати POST /api/auth/logout без CSRF-токена: HTTP 204.
4. Повторити GET /api/me: HTTP 401.

Фактичний результат: сервер виконав logout без CSRF-токена,
а стан автентифікованої сесії змінився.

## Вплив і ймовірність

Статус перевірки: спостережено локально.

Відсутня додаткова серверна перевірка наміру користувача
для cookie-authenticated mutation. SameSite=Lax частково
обмежує міжсайтовий ризик, але не замінює antiforgery.
Успішну атаку з іншого сайту окремо не перевірено.

## Першопричина

У поточному стані API немає явної antiforgery validation
перед виконанням авторизованих змінювальних операцій.

## Виправлення

Зареєстровано ASP.NET Core Antiforgery.
Додано GET /api/auth/csrf-token із Cache-Control: no-store.
Створено CsrfEndpointFilter із перевіркою токена перед мутацією.
Для відсутнього або неправильного токена повертається
HTTP 400 у форматі ProblemDetails.

Захищені маршрути:
- POST /api/incidents
- PUT /api/incidents/{id}
- DELETE /api/incidents/{id}
- POST /api/incidents/{id}/triage
- POST /api/auth/logout

Оновлено браузерні клієнти та тестові HTTP-клієнти.

## Retest

Дата: 17.11.2026.
Результат: PASS.

Logout без токена: HTTP 400, сесія збережена.
Logout з неправильним токеном: HTTP 400, сесія збережена.
Logout з правильним токеном: HTTP 204, сесію завершено.

Створення інциденту без токена: HTTP 400.
Створення інциденту з правильним токеном: HTTP 201.

Інтеграційні тести додатково підтвердили,
що відхилені create, update, delete і triage
не змінюють відповідні дані чи історію статусів.

## Positive regression

Збережено 33 попередні тести.
Додано 5 CSRF-регресій.

Команда:
dotnet test SecureLab.sln -c Release --logger "console;verbosity=minimal"

Результат:
Total: 38; Passed: 38; Failed: 0; Skipped: 0.
Release build: succeeded.

## Залишковий ризик

Antiforgery не замінює авторизацію або захист від XSS.
Компрометація JavaScript у довіреному origin може
поставити під загрозу токен.

Реальне HTTPS production-розгортання окремо не тестувалося.

## Докази

Стан до виправлення:
05_csrf_f01_before_fix_200_204_401.png

Після виправлення:
09_csrf_logout_after_fix_200_400_200_200_204_401.png
10_csrf_invalid_token_200_200_400_200.png
12_csrf_incident_creation_200_400_201.png
13_csrf_logout_browser_button_204_401.png

Повна регресія:
14_csrf_full_regression_38_of_38_console.txt

Висновок: F-01 виправлено, повторні перевірки успішні.
