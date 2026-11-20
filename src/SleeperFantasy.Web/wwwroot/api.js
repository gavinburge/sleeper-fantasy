window.sleeperApi = {
    request: async function (path, method, body, antiforgeryToken) {
        const headers = {};
        if (body) {
            headers["Content-Type"] = "application/json";
        }
        if (antiforgeryToken) {
            headers["RequestVerificationToken"] = antiforgeryToken;
        }

        const baseUrl = window.sleeperApiBaseUrl || "/";
        const response = await fetch(new URL(path, baseUrl), {
            method: method,
            headers: headers,
            body: body || undefined,
            credentials: "include"
        });

        return {
            status: response.status,
            body: await response.text()
        };
    }
};
