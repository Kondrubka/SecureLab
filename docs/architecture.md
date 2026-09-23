# Карта архітектури

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарії отримання списку, деталей інциденту та зведення за критичністю |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері |

## Наскрізний маршрут запиту

### Отримання списку інцидентів

1. У `Client/app.js` функція `loadIncidents()` зчитує фільтр `status` і формує URL запиту.
2. `apiFetch()` надсилає запит через `fetch()` та повертає JSON. Якщо відповідь неуспішна, клієнт обробляє помилку.
3. Endpoint у `Presentation/Endpoints/IncidentEndpoints.cs` перевіряє параметр `status` і викликає `IncidentQueries.GetListAsync()`.
4. У `Application/Incidents/IncidentQueries.cs` EF Core формує запит до БД, за потреби фільтрує записи за статусом, сортує їх і проєктує у `IncidentListItemResponse`.
5. API повертає JSON, а `renderIncidentList()` у клієнті створює елементи списку для відображення.

### Отримання деталей інциденту

1. Після натискання на інцидент клієнт викликає `loadIncidentDetails(id)`.
2. Клієнт надсилає `GET /api/incidents/{id}` через `apiFetch()`.
3. `GetDetailsAsync()` в `IncidentEndpoints.cs` викликає `IncidentQueries.GetDetailsAsync()`.
4. Запит у `IncidentQueries.cs` отримує дані інциденту, власника та лише публічні коментарі (`!comment.IsInternal`), після чого формує `IncidentDetailsResponse`.
5. Якщо запису немає, API повертає 404; інакше повертає JSON із деталями.
6. `renderIncidentDetails()` відображає дані за допомогою `textContent` і `document.createTextNode()`.

### Отримання зведення за критичністю

1. Натискання кнопки викликає `loadSeveritySummary()` у `Client/app.js`.
   Клієнт блокує кнопку, очищає попередній результат і показує
   «Завантаження зведення…».
2. Через `apiFetch()` надсилається `GET /api/incidents/severity-summary`.
   Якщо вибрано статус, додається параметр `?status=...`.
3. `GetSeveritySummaryAsync()` у `Presentation/Endpoints/IncidentEndpoints.cs`
   перевіряє статус через `Enum.TryParse` і явний список дозволених значень:
   New, Triaged, InProgress, Resolved, Closed.
   Некоректне значення спричиняє відповідь 400 Validation Problem Details.
4. `IncidentQueries.GetSeveritySummaryAsync()` звертається до
   `SecureLabDbContext.Incidents`, що відповідає таблиці `incidents`.
   Запит використовує `AsNoTracking()` та за потреби фільтрує записи за статусом.
5. `GroupBy()` і `Count()` виконують агрегування в PostgreSQL.
   Результат отримується через `ToListAsync(cancellationToken)`.
6. Після отримання даних групи сортуються в пам'яті за порядком
   Low → Medium → High → Critical та перетворюються на
   `IncidentSeveritySummaryResponse` з полями `string Severity` і `int Count`.
   Групи без інцидентів не додаються; за відсутності відповідних записів
   повертається порожній масив `[]`.
7. Структурований журнал містить кількість груп `{GroupCount}`
   та застосований статус `{Status}`.
8. API повертає 200 OK із JSON. Клієнт відображає результат через `textContent`.
   Для порожнього масиву показує «Даних для зведення немає.»,
   а в разі помилки — фіксоване повідомлення
   «Не вдалося завантажити зведення. Спробуйте ще раз.».
   У блоці `finally` кнопка знову стає доступною.

Для початкових даних зведення без фільтра містить Low: 1, Medium: 1, High: 1.
Фільтр New повертає Low: 1; Resolved — порожній масив.
Схема БД для реалізації зведення не змінювалася.

### Спостереження з коду

- Для читання даних використовується `AsNoTracking()`.
- Параметр `status` перевіряється на стороні API; некоректне значення призводить до помилки валідації.
- Внутрішні коментарі виключаються з відповіді з деталями.
- Для відображення тексту клієнт використовує `textContent` і `document.createTextNode()`.

## Межі довіри

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Користувач → Browser client | Користувач може вводити довільні значення у поля форми. | Клієнтська частина отримує введені значення; перед використанням їх потрібно обробляти відповідно до призначення. |
| Browser client → API | HTTP-запит можна сформувати або змінити поза інтерфейсом браузера, тому не можна покладатися лише на перевірки UI. | Для зведення `IncidentEndpoints.cs` перевіряє `status` через `Enum.TryParse` і `allowedStatuses.Contains(candidate)`. Недопустиме значення спричиняє `Results.ValidationProblem` зі статусом 400.
| PostgreSQL → API → DOM | У базі даних може зберігатися текст, який раніше надійшов від користувача, тому його не слід автоматично вважати безпечним для HTML. | `Application/Incidents/IncidentQueries.cs` проєктує дані у DTO з `Presentation/Contracts/`. У клієнті текст відображається через безпечні DOM API, зокрема `textContent` і `createTextNode`. |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.

## Відновлення початкових даних

Команди виконуються з кореня репозиторію при запущеному PostgreSQL.
Перед скиданням потрібно зупинити API через Ctrl+C.

У середовищі Development виконати:

```powershell
dotnet run --project src/SecureLab.Api -- --reset-database
```

Команда застосовує наявні міграції, очищає відомі навчальні таблиці
та відновлює початкові дані. Поточні дані цих таблиць буде втрачено;
сама БД та її схема не видаляються.

Після завершення знову запустити API:

```powershell
dotnet run --project src/SecureLab.Api
```

Для перевірки відновлення виконати запити з `tests/http/incidents.http`:
отримати список інцидентів і зведення без фільтра.
Очікуване зведення початкових даних: Low: 1, Medium: 1, High: 1.