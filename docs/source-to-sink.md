# Власна карта source-to-sink

Рівень: «Добре».

Початковий уразливий commit: `3f07849`.

## 1. Incident.Description

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Client/browser-lab.html`, форма `#edit` | Поле `description`, введене користувачем | Недовірене введення |
| Request | `Client/browser-lab.js`, обробник `submit` | `POST /api/incidents`, JSON `description` | Дані передаються серверу |
| Validation/mapping | `Scaffolding/Lab02Endpoints.cs`, `CreateIncidentRequest` | Перевірка обов'язковості, довжини, `Trim()` | Валідація не усуває XSS |
| Storage | `Incident.Description`, `SecureLabDbContext` | Значення зберігається в PostgreSQL | Збережений текст залишається недовіреним |
| Response | `IncidentQueries.GetDetailsAsync`, `IncidentDetailsResponse` | `GET /api/incidents/{id}`, JSON `description` | Текст повертається браузеру |
| Client transform | `Client/browser-lab.js`, `load()` | `response.json()` → `current.description` | Перетворення JSON не очищує HTML |
| Sink/context | `Client/browser-lab.js`, `load()` | `#description.innerHTML = current.description` | Небезпечний HTML-контекст |

## 2. IncidentComment.Text

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Data/DbSeeder.cs`, `SeedAsync` | `IncidentComment.Text` | Навчальне значення; у реальному застосунку текст автора коментаря є недовіреним |
| Request | Читання існуючого інциденту | `GET /api/incidents/{id}` | Guid визначає запис |
| Validation/mapping | `IncidentQueries.GetDetailsAsync` | `comment.Text` → `IncidentCommentResponse.Text` | Немає HTML-очищення |
| Storage | `IncidentComment.Text`, PostgreSQL | Збережений текст коментаря | Зберігання не забезпечує безпечного відображення |
| Response | `IncidentDetailsResponse.Comments` | JSON `comments[].text` | Дані надходять із API |
| Client transform | `Client/browser-lab.js`, `load()` | Цикл `for ... of current.comments` | Кожен коментар передається в DOM |
| Sink/context | `Client/browser-lab.js`, `load()` | `paragraph.innerHTML = comment.text` | Небезпечний HTML-контекст |

## 3. Інвентаризація DOM sinks

| Місце | Дані | Контекст | Рішення |
|---|---|---|---|
| `#title.textContent` | Назва інциденту | Текст | Залишити безпечний спосіб |
| `#description.innerHTML` | Опис інциденту | HTML | Замінити safe DOM rendering |
| `paragraph.innerHTML` | Текст коментаря | HTML | Замінити safe DOM rendering |

## 4. Висновок аналізу

Виявлено два небезпечні способи відображення текстових даних через `innerHTML`. Значення `Description` і `IncidentComment.Text` можуть містити HTML-подібний текст, який браузер інтерпретує як розмітку. Збереження в PostgreSQL, серверна валідація та JSON-серіалізація не усувають цієї загрози.

Для усунення вразливості було використано безпечний DOM API `textContent` та реалізовано спільну функцію відображення тексту.

## 5. Результати виправлення

Початковий уразливий стан зафіксовано в коміті `3f07849`. Виправлення XSS реалізовано в коміті `8b1dc1b`.

У файлі `browser-lab.js` створено спільну функцію:

```javascript
function renderSafeText(element, text) {
  element.textContent = text ?? "";
}
```

Вона використовується для відображення обох полів:
- `Incident.Description`;
- `IncidentComment.Text`.

Після виправлення дані відображаються як текстові вузли DOM без інтерпретації HTML.

Повторні перевірки підтвердили:
- XSS-marker в описі не виконується (`null`).
- XSS-marker у коментарі не виконується (`null`).
- HTML-подібні рядки, лапки та Unicode відображаються правильно.
- Текст довжиною 3900 символів зберігається та відображається без втрат.
- Усі 33 автоматичні тести завершилися успішно після виправлення XSS.

Тестовий публічний коментар створено в `DbSeeder.cs` з ідентифікатором `30000000-0000-0000-0000-000000000005`.

Залишковий ризик: безпечність відображення залежить від правильного використання DOM API. Поява нових небезпечних операцій `innerHTML` з недовіреними даними може повторно створити XSS-вразливість.
