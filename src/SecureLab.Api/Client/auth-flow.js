const element = id => document.getElementById(id);

function show(id, message) {
    element(id).textContent = message;
}

async function checkProfile() {
    const response = await fetch("/api/me", {
        credentials: "same-origin"
    });

    if (response.status === 401) {
        show("profileResult", "Анонімний сеанс — 401 Unauthorized");
        return 401;
    }

    if (!response.ok ||
        !response.headers.get("content-type")?.includes("application/json")) {
        show("profileResult", `Помилка профілю: ${response.status}`);
        return response.status;
    }

    const profile = await response.json();

    show("profileResult", JSON.stringify({
        userId: profile.userId,
        userName: profile.userName,
        role: profile.role
    }, null, 2));

    return response.status;
}

// Реєстрація не виконує автоматичний вхід.
element("registerForm").addEventListener("submit", async event => {
    event.preventDefault();

    const form = event.currentTarget;
    const data = new FormData(form);

    const request = {
        userName: String(data.get("userName")).trim(),
        email: String(data.get("email")).trim(),
        displayName: String(data.get("displayName")).trim(),
        password: String(data.get("password"))
    };

    form.elements.password.value = "";

    try {
        const response = await fetch("/api/auth/register", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify(request)
        });

        show("registerResult",
            `Реєстрація: ${response.status}`);

        if (response.status === 201) {
            form.reset();
        }
    } catch {
        show("registerResult", "Помилка мережі.");
    }
});

// Вхід через стандартну Identity cookie.
element("loginForm").addEventListener("submit", async event => {
    event.preventDefault();

    const form = event.currentTarget;
    const data = new FormData(form);

    const request = {
        userName: String(data.get("userName")).trim(),
        password: String(data.get("password"))
    };

    form.elements.password.value = "";

    try {
        const response = await fetch("/api/auth/login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify(request)
        });

        show("loginResult", `Login: ${response.status}`);
        await checkProfile();
    } catch {
        show("loginResult", "Помилка мережі.");
    }
});

// Створення інциденту без поля ownerUserId.
element("incidentForm").addEventListener("submit", async event => {
    event.preventDefault();

    const data = new FormData(event.currentTarget);

    const occurredAt = new Date(
        String(data.get("occurredAtUtc")));

    if (Number.isNaN(occurredAt.getTime())) {
        show("incidentResult", "Некоректна дата.");
        return;
    }

    const request = {
        title: String(data.get("title")).trim(),
        description: String(data.get("description")).trim(),
        severity: String(data.get("severity")),
        occurredAtUtc: occurredAt.toISOString()
    };

    show("incidentRequest",
        "JSON без ownerUserId:\n" +
        JSON.stringify(request, null, 2));

    try {
        const response = await fetch("/api/incidents", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "same-origin",
            body: JSON.stringify(request)
        });

        show("incidentResult",
            `Створення інциденту: ${response.status}`);
    } catch {
        show("incidentResult", "Помилка мережі.");
    }
});

// Серверний вихід і повторна перевірка профілю.
element("logoutButton").addEventListener("click", async () => {
    try {
        const response = await fetch("/api/auth/logout", {
            method: "POST",
            credentials: "same-origin"
        });

        show("logoutResult", `Logout: ${response.status}`);

        if (response.status === 204) {
            show("profileResult", "Профіль очищено.");
            show("incidentResult", "Сеанс завершено.");
            show("incidentRequest", "JSON запиту очищено.");

            const status = await checkProfile();
            show("logoutResult",
                `Logout: 204; повторний /api/me: ${status}`);
        }
    } catch {
        show("logoutResult", "Помилка мережі.");
    }
});

element("checkProfile").addEventListener("click", () => {
    checkProfile().catch(() =>
        show("profileResult", "Помилка мережі."));
});

// Початковий стан — перевіряємо без входу.
checkProfile().catch(() =>
    show("profileResult", "Помилка мережі."));
