# Карта архітектури

Це початкова карта. Під час ЛР 1 доповніть її власним трасуванням запиту,
конкретними файлами та спостереженнями з DevTools і журналу PostgreSQL.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку або деталей інциденту |
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

1. Натискання кнопки зведення викликає `loadSeveritySummary()` у `Client/app.js`.
2. Клієнт надсилає `GET /api/incidents/severity-summary`, додаючи `status`, якщо фільтр задано.
3. `GetSeveritySummaryAsync()` в `IncidentEndpoints.cs` перевіряє статус і викликає відповідний метод `IncidentQueries`.
4. У `IncidentQueries.cs` EF Core групує інциденти за критичністю та підраховує кількість записів у кожній групі.
5. API повертає список `IncidentSeveritySummaryResponse` у форматі JSON, а клієнт відображає значення через `textContent`.

### Спостереження з коду

- Для читання даних використовується `AsNoTracking()`.
- Параметр `status` перевіряється на стороні API; некоректне значення призводить до помилки валідації.
- Внутрішні коментарі виключаються з відповіді з деталями.
- Для відображення тексту клієнт використовує `textContent` і `document.createTextNode()`.

## Межі довіри

Доповніть таблицю щонайменше трьома конкретними спостереженнями.

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Користувач → Browser client | Користувач може вводити довільні значення у поля форми. | Клієнтська частина отримує введені значення; перед використанням їх потрібно обробляти відповідно до призначення. |
| Browser client → API | HTTP-запит можна сформувати або змінити поза інтерфейсом браузера, тому не можна покладатися лише на перевірки UI. | `Presentation/Endpoints/IncidentEndpoints.cs`: параметр `status` перевіряється через `Enum.TryParse` та `Enum.IsDefined` або список дозволених статусів. Некоректне значення призводить до `Results.ValidationProblem`. |
| PostgreSQL → API → DOM | У базі даних може зберігатися текст, який раніше надійшов від користувача, тому його не слід автоматично вважати безпечним для HTML. | `Application/Incidents/IncidentQueries.cs` проєктує дані у DTO з `Presentation/Contracts/`. У клієнті текст відображається через безпечні DOM API, зокрема `textContent` і `createTextNode`. |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.
