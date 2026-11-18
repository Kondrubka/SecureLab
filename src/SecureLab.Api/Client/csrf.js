window.secureLabCsrfFetch = async function (url, options = {}) {
    const tokenResponse = await fetch("/api/auth/csrf-token", {
        credentials: "same-origin",
        cache: "no-store"
    });

    if (!tokenResponse.ok) {
        return tokenResponse;
    }

    const data = await tokenResponse.json();

    if (!data.token) {
        throw new Error("CSRF token is missing");
    }

    const headers = new Headers(options.headers);
    headers.set("X-SecureLab-CSRF", data.token);

    return fetch(url, {
        ...options,
        credentials: "same-origin",
        headers
    });
};
