# Access Control Matrix - Lab 04

## Контекст та межі

- Навчальний baseline: **2-A**. Головний ресурс: `Incident`; дочірній: `IncidentComment`.
- Головна захищена операція: `GET /api/incidents/{id}`.
- Політика для автентифікованого користувача, який не має доступу до **наявного** ресурсу: **403 Forbidden**. Для анонімного запиту до захищеного маршруту: **401 Unauthorized**. Для неіснуючого ресурсу: **404 Not Found**.
- Ідентичність і ролі надходять лише з перевіреного сервером `HttpContext.User` (`ClaimsPrincipal`), а не з `body`, query string чи JavaScript.
- Для Reporter застосовується перевірка власника; Analyst має право читати інциденти для аналізу і виконувати тільки дозволений перехід `New -> Triaged`. Administrator не отримує загального bypass.

## Матриця дозволів

| Дія / маршрут | Anonymous | Reporter (власний) | Reporter (чужий) | Analyst | Administrator без спеціального дозволу |
|---|---|---|---|---|---|
| `GET /api/incidents/{id}` - details | 401 | 200 | 403 | 200 (для аналізу) | 403 для чужого |
| `GET /api/incidents` - list | 401 | 200, лише власні | чужі не повертаються у списку | 200, доступні всі | лише власні за загальним scoping, без обходу |
| `PUT /api/incidents/{id}` - title/description | 401 | 204 | 403 | 403 (навіть для чужого) | 403 без Reporter ownership-правила |
| `DELETE /api/incidents/{id}` | 401 | 204, **лише status=New** | 403 | 403 | 403 без окремого дозволу |
| `POST /api/incidents/{id}/triage` | 401 | 403 | 403 | 204, **лише status=New**, далі `Triaged` | 403 без окремого дозволу |
| `GET /api/incident-comments/{id}` - public | 401 | 200, якщо доступний батьківський `Incident` | 403, якщо parent чужий | 200, якщо parent доступний | залежить від права на parent; жодного bypass |
| `GET /api/incident-comments/{id}` - internal | 401 | 403, навіть для свого `Incident` | 403 | 200 після перевірки права на parent | 403 без окремого дозволу |
| `GET /api/incidents/{id}` - internal comment в `Comments` | 401 | не включається у відповідь | 403 для чужого incident | включається після дозволеного read | не включається без дозволеної ролі |
| `POST /api/incidents` - create | успадкований захищений маршрут | дозволений, owner/status/id задає сервер | не застосовується | відповідно до успадкованого create contract | без автоматичного bypass |

**Неіснуючий `Incident` або `IncidentComment`** - `404` (коли запит дійшов до resource lookup). Політику нерозрізнюваного `404` для чужого й неіснуючого ресурсу не обрано, отже A-10 не застосовується.

**Примітка до Administrator:** для списку реалізована умова «усі записи лише для Analyst»; для деталей чужого інциденту Admin не має автоматичного доступу. Матриця не стверджує, що роль Admin означає право Reporter на зміну власного запису.

## Застосування правил у коді

| Об'єкт перевірки | Реалізація | Захищений принцип |
|---|---|---|
| Principal і роль | `HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)`, `IsInRole` | Довіряємо лише server-issued claim |
| Details | `IncidentEndpoints.GetDetailsAsync`, `IncidentQueries.GetOwnerIdAsync` | Перевірка owner або Analyst перед поверненням DTO |
| List | `IncidentEndpoints.GetListAsync`, `IncidentQueries.GetListAsync` | `OwnerUserId == userId`, якщо не Analyst |
| Content update | `UpdateIncidentRequest`, `IncidentAccess.CanUpdate`, `UpdateIncidentAsync` | Тільки `Title`, `Description`; без перепризначення owner/status/role |
| Delete | `IncidentAccess.CanDelete`, `DeleteIncidentAsync` | Reporter власник і статус `New` |
| Triage | `IncidentAccess.CanTriage`, `TriageIncidentAsync` | Лише Analyst і `New -> Triaged`; запис history |
| Child comment | `GetCommentAsync`, `IncidentAccess.CanRead`, `IncidentAccess.CanReadAll` | Право на parent + internal visibility |
| Comments у details | `IncidentQueries.GetDetailsAsync(includeInternal: ...)` | Internal повертаються лише для Analyst після авторизації parent |
| Audit deny | `TriageIncidentAsync`, logger `SecureLab.Authorization` | Подія `authorization.denied` з subject/action/resource/result/UTC/trace, без credential |

## IDOR/BOLA finding - до / після

**До виправлення.** Alice автентифікувалась (`login_status=204`) і виконала `GET /api/incidents/20000000-0000-0000-0000-000000000002` (інцидент Bob). Сервер повертав `read_status=200` із чужими деталями. Першопричина: пошук ресурсу лише за id, без ownership/role decision.

**Після виправлення.** Та сама дія повертає `403` і не розкриває чужий DTO. Alice -> власний інцидент `200`; Morgan -> Alice `200`; Anonymous -> details `401`. Для mutating endpoints перевірка прав стоїть **перед** `SaveChangesAsync`; A-04 доводить відсутність змін чужого інциденту.

**Історія Git:** baseline `v0.3.0` (`d5dc45d`); vulnerable commit `0fb0cdd`; первинний fixed commit `b6071c5` (details IDOR і tests). Остаточний commit рівня «Добре» та тег `v0.4.0` - **дописати після фінальної фіксації**.

## Результати перевірок

| ID | Сценарій | Очікування | Фактично / доказ |
|---|---|---|---|
| A-01 | Anonymous protected request | 401 | `401` для списку (інтеграційний тест); details `401` вручну |
| A-02 | Alice -> свій details | 200 | `200`, test `A02_OwnerCanReadOwnIncident` |
| A-03 | Alice -> Bob details | 403 | `403`, test `A03_NonOwnerCannotReadForeignIncident` |
| A-03R | Morgan -> Alice details | 200 | `200`, test `A03R_AnalystCanReadForeignIncident` |
| A-04 | Alice -> Bob PUT/DELETE | 403; DB без змін | 2 сценарії `A04_NonOwnerCannotMutateForeignIncident`, before=after |
| A-05 | Alice list | лише власні | тест і браузер: Alice 3 записи; Morgan 5 |
| A-06 | Reporter triage | 403, статус без змін | `A06_ReporterCannotPerformTriage`, no history |
| A-07 | Analyst triage | 204, `New -> Triaged` | `A07_AnalystCanTriageNewIncident`, перевірено history |
| A-08a | Overposting `ownerUserId`/`role`/`status`/`severity` | змінюються лише content fields | `A08a_OverpostingCannotChangeProtectedFields`, protected state незмінний |
| A-08b | Альтернативний варіант | - | не виконувався, обрано A-08a |
| A-09 | Audit при deny triage | безпечна `authorization.denied` | лог `SecureLab.Authorization`, поля SubjectId/Action/ResourceId/Result/OccurredAtUtc/TraceId; без cookie/password/body |
| A-10 | Лише для політики 404 | - | не застосовується, обрано 403 |
| A-11 | Search / severity-summary | - | не виконувався, належить до «Відмінно» |
| A-12 | Чужий child id і internal comment | 403/401/200 згідно з роллю; Reporter не бачить internal | `A12_ChildCommentAndInternalVisibilityAreProtected`, direct endpoint і details projection |

**Повний регресійний запуск:** `dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release`: **33 із 33 успішних**, 0 failed, 0 skipped (27.10.2026).

## Залишкові ризики й обмеження

- `GET /api/incidents/search` і `GET /api/incidents/severity-summary` не входять до обов'язкового A-11 для рівня «Добре» і ще потребують окремого scoping, аби не віддавати чужі дані/агрегати.
- Для cookie-authenticated змінювальних операцій захист від CSRF має бути опрацьований у наступній лабораторній роботі; authorization не замінює CSRF-захист.
- Наявні тести переважно перевіряють локальний однопотоковий сценарій, не доводять безпечність усіх можливих суміжних API.
