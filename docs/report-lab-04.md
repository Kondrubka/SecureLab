# Звіт ЛР04 - Авторизація, ролі, володіння ресурсом та IDOR/BOLA

## Контекст і межа виконання

**Рівень:** «Добре» (усі обов'язкові результати «Задовільно» + A-01, A-04-A-09, A-12). A-08a обрано замість A-08b. A-10 не застосовується, оскільки обрано `403` для чужого наявного ресурсу. A-11 («Відмінно») не виконувався.

**Проєкт:** SecureLab, baseline 2-A, Windows / PowerShell / .NET 10 / PostgreSQL (Docker). Робоча гілка: `lab/4-authorization`. Відправна точка: тег `v0.3.0`, commit `d5dc45d`. Уразливий стан: commit `0fb0cdd`. Початкове виправлення details: commit `b6071c5`.

**Головна дія:** `GET /api/incidents/{id}`. Додатково реалізовано авторизований список, редагування і видалення, `triage`, пряме читання дочірнього `IncidentComment` та внутрішніх коментарів з урахуванням ролі.

**Політика:** anonymous -> `401`, authenticated без права на наявний ресурс -> `403`, ресурс не знайдений -> `404`.

**Остаточний hash fixed commit / tag v0.4.0:** очікує завершального commit і створення тегу. Не заповнювати довільним значенням.

## Мета та завдання

Мета - дослідити IDOR/BOLA та реалізувати server-side авторизацію, що враховує роль, власника ресурсу, поточний стан і взаємозв'язок батьківських та дочірніх даних. Перевірити відсутність змін БД після заборонених операцій, безпечний аудит та обмеження видимості службових коментарів.

## Підготовка середовища

- Від створеного стану `v0.3.0` створено гілку `lab/4-authorization`; незавершені зміни ЛР 3 попередньо збережено через `git stash`.
- Перевірено .NET SDK `10.0.401`, Python `3.9.13`, Docker Engine `29.6.1`.
- PostgreSQL контейнер `secure-lab-postgres-1` мав статус Healthy, локальний порт `127.0.0.1:54329 -> 5432`.
- Перевірено навчальний скрипт `login-and-read.py --check`, JSON валідний; пароль наявний у змінній середовища, у звіт не виноситься.
- Після відновлення навчальної бази вихідний набір інтеграційних тестів: 22/22 успішно.

## Матриця доступу і серверні правила

Повна матриця: `docs/access-matrix.md`. Основні умови:

1. `Reporter` читає й редагує лише власний `Incident`. Видалення власного дозволене лише якщо статус `New`.
2. `Analyst` читає записи для аналізу, може виконати тільки `New -> Triaged`, але не отримує права довільного `PUT` чи `DELETE`.
3. Для `internal comment` Reporter отримує заборону навіть для свого батьківського інциденту; Analyst бачить його після авторизації parent.
4. `Administrator` не отримує права обходу перевірок автоматично.
5. `OwnerUserId`, ролі, зміна статусу та системні поля не приймаються як довірені параметри клієнтського запиту. Авторитетним джерелом identity і role є `HttpContext.User`.

Точки реалізації: `IncidentEndpoints.cs`, `IncidentQueries.cs`, `IncidentAccess.cs`, `UpdateIncidentRequest.cs`. Для `GetDetailsAsync` перевірка власника / Analyst стоїть перед поверненням `IncidentDetailsResponse`; для mutation доступ перевіряється до збереження змін. Список фільтрується сервером по `OwnerUserId`, якщо користувач не Analyst.

## Уразливий стан і PoC

Перед виправленням скрипт `py -3 ..\lr04-resources\commands\login-and-read.py --user alice --incident bob` повернув:

```text
login_status=204
read_status=200
```

Ресурс: `20000000-0000-0000-0000-000000000002`, власник Боб Мельник. За матрицею Alice не має права його читати, очікувався `403`. Відповідь `200` із деталями чужого інциденту підтвердила **IDOR/BOLA**.

**Причина:** ресурс знаходився за GUID без перевірки ownership або дозволеної ролі current principal. Лише приховування чужих пунктів у UI не усунуло б дефект прямого HTTP-запиту.

## Виправлення головної дії та повторна перевірка

У `GetDetailsAsync` додано читання `ClaimTypes.NameIdentifier`, перевірку owner ID і server-issued ролі Analyst. Для анонімного клієнта діє `RequireAuthorization`. Результат повторної ручної перевірки:

| Сценарій | До | Після |
|---|---:|---:|
| Alice -> Alice | 200 | 200 |
| Alice -> Bob | 200 | **403** |
| Morgan -> Alice | 200 | 200 |
| Anonymous -> Alice | 200 | **401** |

У браузері власні деталі Alice відображаються, а спроба відкрити інцидент Bob повертає `Помилка: Forbidden`. Виправлений стан головної дії зафіксовано commit `b6071c5`, попередній уразливий стан - `0fb0cdd`.

## Додаткове посилення рівня «Добре»

### Фільтрація списку (A-05)

`GET /api/incidents` вимагає автентифікації. Query для Reporter обмежено `OwnerUserId == currentUserId`; Analyst бачить усі доступні записи. Підтверджено у браузері: **Alice - 3 інциденти**, **Morgan - 5**. Чужий запис Bob відсутній у списку Alice.

### Оновлення та видалення (A-04)

Додано `PUT /api/incidents/{id}` із DTO `UpdateIncidentRequest(Title, Description)` та `DELETE /api/incidents/{id}`. Звичайний Reporter може змінювати title/description лише власного ресурсу; delete для власника - тільки в статусі `New`. Перед `SaveChangesAsync` перевіряється `IncidentAccess.CanUpdate` / `CanDelete`. Два HTTP-сценарії A-04 (PUT і DELETE чужого Bob) повернули **403**; порівняння `Title`, `Description`, `OwnerUserId`, `Severity`, `Status`, `UpdatedAtUtc` до/після підтвердило, що PostgreSQL не змінено.

### Overposting (A-08a)

Навмисно подано зайві поля `ownerUserId`, `role`, `status`, `severity` разом із дозволеними `title` і `description`. Результат **204**: вміст оновився, але protected fields зберегли свої значення; нових прав не виникло. Після тесту початковий title/description/time відновлено. Контракт ігнорує невідомі поля за обраною моделлю A-08a.

### Привілейований triage і статус (A-06, A-07)

Окремий `POST /api/incidents/{id}/triage`: Reporter отримує **403** та не змінює статус або історію; Analyst може змінити **лише `New -> Triaged`**, результат **204**. Сервер сам встановлює новий статус і записує `IncidentStatusHistory` із тим, хто виконав зміну. Перевірено автоматичними HTTP-тестами A-06/A-07 на окремому тимчасовому інциденті.

### Аудит (A-09)

При відмові для triage зафіксовано подію `authorization.denied` у категорії `SecureLab.Authorization`: поля `SubjectId`, `Action=incident.triage`, `ResourceId`, `Result=denied`, `OccurredAtUtc`, `TraceId`. У рядку події відсутні пароль, cookie, token та тіло запиту. Лог зафіксовано у виводі інтеграційного тестування; окремий збіг trace з HTTP-відповіддю у наявних доказах не перевірено.

### Дочірній ресурс і internal visibility (A-12)

Додано `GET /api/incident-comments/{id}`. Спочатку визначається батьківський incident і `IsInternal`, після чого перевіряється право current principal на parent і режим показу internal. Також `IncidentQueries.GetDetailsAsync` отримав прапорець `includeInternal`, який сервер визначає за роллю Analyst.

Інтеграційний тест A-12 довів: Anonymous -> **401**; Alice -> internal власного інциденту **403**; Alice -> public comment Bob **403**; Alice -> public власного **200**; Morgan -> internal **200**. Internal відсутній у `details` Alice, але відображається в `details` Morgan. Тест створює і видаляє тимчасовий internal comment; у seed також додано окремий зразок internal comment. Для перевірки seed після скидання БД потрібен окремий запуск із відновленням seed.

## Доказова таблиця

| ID | Результат | Доказ |
|---|---|---|
| A-01 | 401 Anonymous protected list/details | integration test і ручний HTTP |
| A-02 | 200 Alice -> Alice | `A02_OwnerCanReadOwnIncident` |
| A-03 | 403 Alice -> Bob | `A03_NonOwnerCannotReadForeignIncident` |
| A-03R | 200 Morgan -> Alice | `A03R_AnalystCanReadForeignIncident` |
| A-04 | 403 PUT/DELETE Bob; DB unchanged | `A04_NonOwnerCannotMutateForeignIncident` (2 рядки) |
| A-05 | Alice list лише власні | `A05_ReporterSeesOnlyOwnIncidents`; браузер 3 vs 5 |
| A-06 | 403 Reporter triage, status New і history без змін | `A06_ReporterCannotPerformTriage` |
| A-07 | 204 Analyst triage, New -> Triaged та history | `A07_AnalystCanTriageNewIncident` |
| A-08a | 204 permitted content, protected fields не змінені | `A08a_OverpostingCannotChangeProtectedFields` |
| A-08b | не виконувався | альтернатива A-08a |
| A-09 | є подія authorization.denied | надісланий вивід тестів; журнал без секретів |
| A-10 | не застосовується | політика чужого ресурсу 403, не 404 |
| A-11 | не виконувався | рівень «Відмінно» |
| A-12 | child / internal rules працюють | `A12_ChildCommentAndInternalVisibilityAreProtected` |

## Регресія та технічні помилки під час роботи

Після первинного виправлення details успішно завершено 25/25 тестів; після scoping - 27/27; після A-04 - 29/29; після A-08a - 30/30. Перший запуск із A-06/A-07 збоїв через rate limiting login у спільному тестовому середовищі. Для інтеграційних тестів додано override `Testing:LoginPermitLimit=500`, а окремий тест перевірки login rate limit використовує власне налаштування 12. Звичайний API залишається з лімітом 12. Повторна регресія з A-06/A-07: **32/32**; після A-12: **33/33**, failed=0, skipped=0, Build succeeded (27.10.2026).

Команда контрольного запуску:

```powershell
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
```

Під час роботи виправлено розташування методів у `IncidentEndpoints.cs` і проблему `ILogger<IncidentEndpoints>` для static class через `ILoggerFactory`; після коригувань збірка проходить. Ці проміжні помилки не включаються до підсумкових 33 тестів.

## Результати у браузері

1. Авторизована Alice читає власний інцидент; чужий details повертає `Forbidden`.
2. Після server-side scoping Alice бачить три власні записи, Morgan як Analyst - усі п'ять.
3. Відповіді й тести не містять навчального пароля, cookie або `Set-Cookie`.

**Для Word-звіту:** розмістити скриншоти послідовно по вертикалі з короткими підписами й поясненнями: baseline; вразливий Alice -> Bob `200`; виправлений Alice -> Bob `403`; реєстрація/перевірка C#; 25/25; браузер details; 27/27; списки Alice/Morgan; 29/29; 30/30; 32/32; audit `authorization.denied` (за виводом); 33/33; commits і тег.

## Основний finding та залишковий ризик

**Finding:** BOLA на прямому читанні `Incident` за GUID. Уразливий commit дозволяв Reporter отримувати чужі details. Після серверної перевірки ownership/role запит за тим самим id отримує `403`, дозволені запити залишаються робочими. На рівні «Добре» аналогічні обмеження поширено на list, update/delete, privileged triage і child/internal comments.

**Залишковий ризик:** маршрути `GET /api/incidents/search` та `GET /api/incidents/severity-summary` ще не перевірялись за A-11 (рівень «Відмінно»), тож можливе розкриття чужих даних через ці маршрути. Cookie-authenticated mutation потребує CSRF-захисту в наступних роботах. Також до цього звіту не входить аналіз паралельних state transitions, race conditions або додаткових export/batch потоків.

## Висновок

У ході лабораторної роботи було встановлено причину IDOR/BOLA, пов'язану з відсутністю серверної перевірки прав доступу під час звернення до ресурсу за ідентифікатором. Опановано застосування моделі owner/role/context, серверної фільтрації множини доступних інцидентів та розділення звичайного редагування й привілейованих переходів статусу. Було з'ясовано, чому клієнтські поля owner, role та status не можуть бути підставою для авторизації, а також перевірено незмінність даних після заборонених дій.

Окремо набуто навичок захисту дочірніх ресурсів через перевірку доступу до батьківського інциденту, контрольованого показу внутрішніх коментарів і ведення аудиту відмов без розкриття секретів. Фінальний інтеграційний запуск підтвердив проходження **33 із 33** тестів для реалізованого обсягу рівня «Добре».

## Git, відтворення, подання

- `v0.3.0`: `d5dc45d` (відправний стан).
- Vulnerable state: `0fb0cdd` (`Document vulnerable IDOR baseline for Lab 4`).
- First fix: `b6071c5` (`Fix incident IDOR and add authorization tests`).
- **Фінальний fixed commit / tag `v0.4.0`:** `f8dfe14` (`f8dfe14ce15693b12539256cca9ef27220518ace`). Команди `git rev-parse HEAD` і `git rev-list -n 1 v0.4.0` повернули однаковий hash; після створення тегу `git status --short` був порожнім.
- Локально перевірено `git diff --check`, фінальні 33/33 тести, commit `f8dfe14`, анотований тег `v0.4.0` та чисте робоче дерево. Відправлення на GitHub виконується окремо після звірки документації.

## Використання допоміжних інструментів

Для пояснення методичних вимог, прикладів змін коду, розбору помилок компіляції/тестування та структурування звіту використовувався ChatGPT. Фактичну реалізацію, запуск локальних команд, HTTP-запити, результати БД, браузерні перевірки та Git-історію підтверджено в локальному середовищі. Дані автентифікації до матеріалів звіту не включено.
