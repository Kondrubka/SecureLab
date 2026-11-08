# Докази безпеки браузера (DEL-01)

**Лабораторна робота № 5. Рівень «Добре».**
**Стан документа:** оновлено 08.11.2026 після перевірок кінцевої конфігурації коду в коміті `ddf9cfa`. Невиконані дії для подання позначені **[ДОПЕРЕВІРИТИ]**; їх не слід вважати завершеними. Паролі, значення cookie і токенів у цьому документі відсутні.

## 1. Стенд і Git-стан

| Параметр | Фактичне значення |
|---|---|
| ОС | Windows 11 |
| Браузер | Google Chrome (Chromium 156 за DevTools Client Hints; уточнити версію в About Chrome) |
| API origin | `http://localhost:5080` |
| Дозволений CORS probe | `http://localhost:4173` |
| Контрольний недозволений origin | `http://localhost:4174` |
| .NET SDK | `10.0.401` |
| Scaffold release | `lab-05-start-v1` |
| `base_commit`, `entry_parent` | `f8dfe14ce15693b12539256cca9ef27220518ace` |
| Початковий уразливий коміт | `3f07849` |
| Коміт safe DOM fix | `8b1dc1b` |
| Коміт CORS та CSP | `462af20` |
| Коміт обмеження CORS до Development | `ae28fdb` |
| Коміт узгодження назви CORS policy | `ddf9cfa` (`Lab05BrowserProbe`) |
| Перевірений кодовий коміт | `ddf9cfa`: `dotnet build` та 33/33 тестів успішні, CORS повторно перевірено |
| Підсумковий коміт / тег | **[ДОПЕРЕВІРИТИ]** `v0.5.0` ще не створено; спершу завершити документацію та перевірити кінцевий Git-стан |
| `main`, спосіб передавання | **[ДОПЕРЕВІРИТИ]** GitHub з доступом викладача або перевірений Git bundle |

Навчальне середовище: локальний PostgreSQL у Docker; облікові записи Alice (Reporter), Bob (Reporter), Morgan (Analyst). Скидання та повторне заповнення БД виконано з `--reset-database`; навчальний пароль передавався лише змінною середовища `SeedUsers__Password`, не через Git.

**Механізм сеансу:** cookie-автентифікація ASP.NET Core Identity. У DevTools → Application → Cookies перевірено: Name — `.AspNetCore.Identity.Application`, Domain — `localhost`, Path — `/`, HttpOnly — увімкнено, Secure — вимкнено (локальний HTTP-стенд), SameSite — `Lax`, Expires — `Session`. CORS probe використовує `credentials: "include"`. Значення cookie до доказів не включено.

## 2. REG-XSS-01: гіпотеза, відтворення та виправлення

**Гіпотеза:** недовірене `Incident.Description` (і окремо `IncidentComment.Text`) зберігається в PostgreSQL, потрапляє через `GET /api/incidents/{id}` у JSON та виконується браузером як HTML під час використання `innerHTML`.

**Маршрут `Incident.Description`:** форма `#edit` → `browser-lab.js` (`POST /api/incidents`, JSON `description`) → `CreateIncidentRequest`, `Lab02Endpoints.cs` → `Incident.Description` у PostgreSQL → `IncidentQueries.GetDetailsAsync()` / `IncidentDetailsResponse` → `response.json()` → `#description.innerHTML` (до) / `renderSafeText(...).textContent` (після). Детальніше: [`source-to-sink.md`](source-to-sink.md).

**Дані досліду:** harmless marker із `resources/lab05-marker.txt`:

```html
<img src=x onerror="document.querySelector('#lab05-marker-target').setAttribute('data-lab05-marker','executed')">
```

Тестовий інцидент до виправлення: `6eda6a5e-7683-43e9-9fba-fe27a10c2245`. Сеанс Alice: `GET: 200`, у DOM створено `<img>`, Console показала `executed`. У новому незалежному сеансі Morgan: спочатку `null`, після GET — `executed`. У сеансі Bob: `GET: 403`, маркер лишився `null`.

| Спостереження | До виправлення | Після виправлення | Докази |
|---|---|---|---|
| Збережене значення / API | Маркер повертався як JSON `description`, без виконання на сервері | JSON-значення залишається даними | [JSON базового інциденту](lab05-evidence/01-baseline-api-json.png), [Alice до](lab05-evidence/02-xss-description-alice-before.png) |
| DOM `Incident.Description` | `innerHTML` створював реальний елемент `<img>` | `textContent` залишає рядок текстовим вузлом | [До](lab05-evidence/02-xss-description-alice-before.png), [після](lab05-evidence/08-xss-description-after.png), [DOM після](lab05-evidence/09-xss-description-dom-after.png) |
| Дія marker | `executed` | `null` | [До](lab05-evidence/02-xss-description-alice-before.png), [після](lab05-evidence/08-xss-description-after.png) |
| Апостроф, лапки, `< >`, Unicode, HTML-like | Не перевірялися окремо | Коректно відображені `Київ — Ω`, `O'Brien`, `"терміново"`, `5 < 7`, `<b>жирний</b>` буквально | [Регресія](lab05-evidence/10-regression-text.png) |
| Довгий рядок | До fix окремо не перевірявся | 3900 символів передано через POST/GET та відображено: `.textContent.length === 3900` | [3900](lab05-evidence/11-regression-3900.png) |
| Morgan, Bob | Morgan: `GET 200` і `executed`; Bob: `GET 403`, `null` | Після fix окремий повтор у сеансах Morgan/Bob не зафіксований. Безпечний рендер опису та коментаря перевірено в інших сценаріях; результат за цих двох користувачів не заявляється як повторно перевірений | [Morgan](lab05-evidence/03-xss-description-morgan-before.png), [Bob](lab05-evidence/04-authorization-bob-403.png) |

**Другий потік `IncidentComment.Text` («Добре»):** у `DbSeeder.cs` додано публічний коментар `30000000-0000-0000-0000-000000000005`, автор Morgan, інцидент Alice `20000000-0000-0000-0000-000000000001`, `IsInternal = false`. До fix браузер створював `<img onerror=...>` всередині `#comments`; маркер змінювався з `null` на `executed`. Після fix цей самий рядок показано текстом, маркер залишився `null`. [Коментар до](lab05-evidence/05-xss-comment-before.png), [DOM до](lab05-evidence/06-xss-comment-dom-before.png), [після](lab05-evidence/07-xss-comment-after.png).

**Виправлення:** у `browser-lab.js` реалізовано спільний helper:

```javascript
function renderSafeText(element, text) {
  element.textContent = text ?? "";
}
```

Виклики застосовано для опису та кожного коментаря. Залишкові ризики: майбутні нові DOM sinks, вставлення неперевірених URL/HTML у нові компоненти, обмежений захист CSP від помилок у дозволених власних скриптах. Регресійний контроль: після кожної зміни перевіряти, що маркер відображається буквально, атрибут лишається `null`, безпечні символи та довгий текст зберігаються.

## 3. CORS та REG-CORS-01

**Модель очікувань (відтворена після виконання, не записана наперед):**

| Сценарій | HTTP-запит може дійти до API? | JavaScript читає відповідь? | Причина |
|---|---|---|---|
| Same-origin `5080 → API` | Так | Так, якщо відповідь дозволена автентифікацією | Same-origin policy не блокує |
| GET `4173 → API`, без CORS | Так | Ні | Відсутній ACAO |
| GET `4174 → API`, без CORS | Так | Ні | Відсутній ACAO |
| `curl` із довільним Origin | Так | Не застосовується | `curl` не виконує CORS-перевірку браузера |
| Preflight `4173`, із policy | Так, OPTIONS | Так для дозволених method/headers і належної відповіді; авторизація основного запиту окрема | Правильні CORS-заголовки |

Стенд: `py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe` та аналогічно порт `4174`. Запити з `probe.js`: `fetch(url, { credentials: "include" })`.

| Сценарій | До policy | Після policy | Докази |
|---|---|---|---|
| Same-origin 5080 | API за автентифікованого Alice повертав `200` | `GET 200` у browser-lab зі CSP | [Сторінка](lab05-evidence/23-csp-normal-render.png) |
| Allowed 4173 без cookie | CORS-помилка; HTTP 401 | JS прочитав HTTP-відмову `401`; ACAO `http://localhost:4173`, ACAC `true` | [Network headers](lab05-evidence/16-cors-headers-4173.png) |
| Disallowed 4174 без cookie | CORS-помилка; HTTP 401 | CORS-помилка; HTTP 401; без ACAO/ACAC | [Network headers](lab05-evidence/17-cors-headers-4174.png) |
| `curl` без Origin / із Origin 4174 | Не зафіксовано до policy | В обох випадках `401` та читабельний `application/problem+json` | [Два curl-запити](lab05-evidence/19-cors-curl-direct.png) |

**Preflight:** `OPTIONS /api/incidents`, заголовки `Origin`, `Access-Control-Request-Method: POST`, `Access-Control-Request-Headers: content-type`.

- `4173`: HTTP `204`; `Access-Control-Allow-Origin: http://localhost:4173`; `Access-Control-Allow-Credentials: true`; `Access-Control-Allow-Methods: POST`; `Access-Control-Allow-Headers: content-type`.
- `4174`: HTTP `204`, однак CORS allow-заголовки відсутні; сам статус `204` не означає дозволу.
- Доказ: [обидва OPTIONS у PowerShell](lab05-evidence/18-cors-preflight.png).

**Cookie-дослід:** після входу Alice у тому самому профілі браузера `4173` через `credentials: include` отримав `200` і JSON інциденту. На `4174` HTTP-запит також мав статус `200`, але JS отримав CORS-помилку через відсутній ACAO. Без автентифікації `4173` міг прочитати лише `401`. Докази авторизованого сценарію: [дозволений origin 4173: HTTP 200 і JSON](lab05-evidence/20-cors-cookie-4173.png), [недозволений origin 4174: блокування читання браузером](lab05-evidence/21-cors-cookie-4174.png). У нових двох знімках не показано значень cookie; окремі CORS headers перевірено командами `curl.exe` нижче.

**Контрольний дослід без `AllowCredentials()`:** тимчасово прибрано `.AllowCredentials()` із policy. Під Alice сервер відповів `200 OK`, але Chrome заблокував читання через `credentials: "include"`: заголовок `Access-Control-Allow-Credentials` був відсутній. Після цього `.AllowCredentials()` відновлено. [Консоль браузера](lab05-evidence/28-cors-no-allowcredentials.png).

**Кінцева policy — лише Development:** `AddCors()` і `UseCors("Lab05BrowserProbe")` обгорнуті в `IsDevelopment()`. Обмеження середовища зафіксовано в `ae28fdb`, назву узгоджено в `ddf9cfa`. Після цього виконано успішний `dotnet build`, 33/33 тестів і контроль двох origins. Окремий runtime-запуск у Production не проводився; відсутність реєстрації навчальної CORS policy у Production підтверджується умовним кодом. [Git-стан після обмеження Development](lab05-evidence/30-git-development-only-commit.png).

**Actual HTTP evidence кінцевої конфігурації (08.11.2026, після `ddf9cfa`):** два запити на однаковий `GET /health` із різними заголовками `Origin`. Збережені повні заголовки фактичних відповідей; значення cookie не надсилались і не фіксувалися.

```powershell
curl.exe -sS -D - -o NUL -H "Origin: http://localhost:4173" "http://localhost:5080/health"
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Sun, 08 Nov 2026 17:33:42 GMT
Server: Kestrel
Access-Control-Allow-Credentials: true
Access-Control-Allow-Origin: http://localhost:4173
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
```

```powershell
curl.exe -sS -D - -o NUL -H "Origin: http://localhost:4174" "http://localhost:5080/health"
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Sun, 08 Nov 2026 17:33:52 GMT
Server: Kestrel
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
```

**Висновок:** обидва запити дійшли до API (`200 OK`), але лише дозволений origin `4173` отримав ACAO/ACAC; `4174` їх не отримав. Це підтверджує REG-CORS-01 на фінальній конфігурації.

**REG-CORS-01:** перевіряти з однаковим URL: дозволений origin має ACAO/ACAC, недозволений не має; без cookie API повертає 401, з cookie Alice — 200, але JS на 4174 не може прочитати відповідь. `curl` не є доказом доступу браузерного JS і не обходить авторизацію.

## 4. CSP enforcement та інші headers

Розміщення: middleware захисних заголовків у `Program.cs` перед `UseStaticFiles()`; CORS middleware після `UseRouting()` і перед авторизацією, активний лише в `Development`. CSP додано для HTML-маршрутів `/`, `/index.html`, `/auth-lab.html`, `/auth-flow.html`, `/browser-lab.html`. Перевірено документ `browser-lab.html`: `200 OK` із заголовком `Content-Security-Policy` (не Report-Only), `nosniff` і `no-referrer`.

| Директива | Ресурси | Значення / мотивація |
|---|---|---|
| `default-src` | Базова політика | `'self'` — за замовчуванням власний origin |
| `script-src` | `browser-lab.js`, `auth-lab.js`, `auth-flow.js`, `app.js` | `'self'` — власні JS, без `unsafe-inline` |
| `style-src` | Вбудований `<style>` у `auth-flow.html` | `'self' 'unsafe-inline'` — тимчасовий компроміс для CSS, не для JS |
| `connect-src` | Внутрішні виклики `fetch` до API | `'self'` — блокування сторонніх endpoint для JS-запитів |
| `img-src` | Локальні зображення / data URI | `'self' data:` — заборона ресурсів із 4173 |
| `object-src` | Об'єкти | `'none'` |
| `base-uri` | HTML base | `'none'` |
| `frame-ancestors` | Захист від iframe | `'none'` — заборона вбудовування сторінки |
| `form-action` | HTML-форми | `'self'` |

| Header / контроль | Фактичне значення | Межа захисту |
|---|---|---|
| `X-Content-Type-Options` | `nosniff` | Заборона MIME sniffing; не замінює правильний Content-Type |
| `Referrer-Policy` | `no-referrer` | Не передавати `Referer`; не шифрує запит |
| `Content-Security-Policy` | Enforcement, наведені директиви | Додатковий бар'єр для XSS; не замінює безпечний DOM render |
| `frame-ancestors` | `'none'` | Заборона iframe; протидія clickjacking |

**Перевірено:** документ `200` та headers: [HTTP](lab05-evidence/22-csp-headers.png); функціональний рендер інциденту після CSP: [сторінка + Console null](lab05-evidence/23-csp-normal-render.png); контрольована ручна вставка `<img onerror>` у DOM спричинила повідомлення про порушення `script-src 'self'`, атрибут лишився `null`: [CSP блокує inline](lab05-evidence/24-csp-block-inline.png); вбудовування через iframe з `4173` заблоковано `frame-ancestors 'none'`: [iframe](lab05-evidence/25-csp-block-frame.png).

**Окреме кероване порушення `img-src`:** із `browser-lab.html` виконано `new Image()` з `src = "http://localhost:4173/csp-test.png"`. Chrome повідомив про порушення `img-src 'self' data:` та заблокував дію. [Доказ](lab05-evidence/29-csp-block-external-image.png).

**Перевірка основного browser flow під CSP:** сторінка `/browser-lab.html` успішно відображає дані інцидентів (`GET 200`). На `/auth-lab.html` підтверджено заголовки CSP, `nosniff`, `no-referrer` та успішний вхід Alice (`Login: 204`) — [знімок авторизації та CSP](lab05-evidence/31-auth-lab-csp-login.png). Головна сторінка `/` повернула `200 OK`, завантажила список із 7 інцидентів і містила необхідні security headers — [знімок головної сторінки](lab05-evidence/32-home-csp-headers.png). Під час завершальної перевірки користувач не виявив помилок у Console. Режим CSP Report-Only не досліджувався, оскільки для рівня «Добре» окремі докази цього режиму не потрібні.

## 5. Фінальні перевірки та підсумок

- Повний набір тестів після остаточної зміни коду `ddf9cfa`: **33 succeeded, 0 failed, 0 skipped** (`dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release`, 08.11.2026, тривалість 20,0 с). [Раніший знімок успішного прогону 33/33](lab05-evidence/26-tests-33-pass.png); фінальний результат додатково збережений у термінальному журналі.
- Кодова історія: `3f07849` → `8b1dc1b` → `462af20` → `ae28fdb` → `ddf9cfa`. [Попередній знімок історії](lab05-evidence/27-git-commits.png), [коміт Development-only](lab05-evidence/30-git-development-only-commit.png).
- Першопричина XSS — небезпечні sinks `innerHTML`; усунена безпечним DOM-виведенням, а CSP працює як додатковий бар'єр.
- CORS дозволяє читання відповідей лише сторінці з `http://localhost:4173`, але не замінює автентифікацію, авторизацію чи захист від CSRF. Cookie-запити в реальних сценаріях потребують окремої перевірки CSRF-механізмів. Пояснення й відповіді на контрольні питання включити в DEL-03.

**Перед завершенням:** зберегти документ та скріншоти в Git, створити/перевірити `v0.5.0` тільки після перевірки робочого дерева, перенести fixed state до `main` і надати доступ через погоджений remote або перевірений Git bundle. Основний browser flow під CSP, cookie-дослід, `dotnet build`, 33/33 тестів та фінальний REG-CORS-01 після `ddf9cfa` уже перевірено. Рівень «Відмінно» не заявляється.
