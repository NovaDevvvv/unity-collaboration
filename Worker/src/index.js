const CODE_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
const CODE_LENGTH = 4;
const SESSION_LIFETIME_MS = 24 * 60 * 60 * 1000;

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "access-control-allow-origin": "*",
      "cache-control": "no-store",
    },
  });
}

function randomCode() {
  const bytes = crypto.getRandomValues(new Uint8Array(CODE_LENGTH));
  return Array.from(bytes, value => CODE_ALPHABET[value % CODE_ALPHABET.length]).join("");
}

function validCode(value) {
  return typeof value === "string" && /^[A-Z0-9]{4}$/.test(value);
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "OPTIONS") {
      return new Response(null, {
        status: 204,
        headers: {
          "access-control-allow-origin": "*",
          "access-control-allow-methods": "GET, POST, OPTIONS",
          "access-control-allow-headers": "content-type",
        },
      });
    }

    if (url.pathname === "/v1/health" && request.method === "GET") {
      return json({ ok: true, service: "nova-collaboration-relay" });
    }

    if (url.pathname === "/v1/create" && request.method === "POST") {
      let body;
      try {
        body = await request.json();
      } catch {
        return json({ error: "A JSON request body is required." }, 400);
      }
      const name = String(body?.name || "").trim().slice(0, 48);
      if (!name) return json({ error: "A display name is required." }, 400);

      for (let attempt = 0; attempt < 32; attempt++) {
        const code = randomCode();
        const hostToken = crypto.randomUUID();
        const stub = env.SESSIONS.getByName(code);
        const result = await stub.fetch("https://session.internal/initialize", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ name, hostToken }),
        });
        if (result.status === 201) {
          return json({
            code,
            hostToken,
            connect: `wss://collaborate.novaa.dev/v1/connect?code=${code}`,
            expiresInSeconds: SESSION_LIFETIME_MS / 1000,
          }, 201);
        }
      }
      return json({ error: "Could not allocate a session code. Try again." }, 503);
    }

    if (url.pathname === "/v1/connect" && request.method === "GET") {
      const code = (url.searchParams.get("code") || "").trim().toUpperCase();
      const name = (url.searchParams.get("name") || "").trim().slice(0, 48);
      const id = (url.searchParams.get("id") || crypto.randomUUID()).trim().slice(0, 64);
      if (!validCode(code)) return json({ error: "Use a four-character session code." }, 400);
      if (!name) return json({ error: "A display name is required." }, 400);
      if (request.headers.get("Upgrade")?.toLowerCase() !== "websocket") {
        return json({ error: "This endpoint requires a WebSocket upgrade." }, 426);
      }

      const stub = env.SESSIONS.getByName(code);
      const internalUrl = new URL("https://session.internal/connect");
      internalUrl.searchParams.set("name", name);
      internalUrl.searchParams.set("id", id);
      internalUrl.searchParams.set("hostToken", url.searchParams.get("hostToken") || "");
      return stub.fetch(new Request(internalUrl, request));
    }

    return json({ error: "Not found." }, 404);
  },
};

export class CollaborationSession {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
  }

  async fetch(request) {
    const url = new URL(request.url);
    if (url.pathname === "/initialize" && request.method === "POST") {
      if (await this.ctx.storage.get("createdAt")) {
        return new Response("Session code is already allocated.", { status: 409 });
      }
      const body = await request.json();
      const now = Date.now();
      await this.ctx.storage.put({
        createdAt: now,
        hostName: String(body.name || "Host"),
        hostToken: String(body.hostToken || ""),
      });
      await this.ctx.storage.setAlarm(now + SESSION_LIFETIME_MS);
      return new Response("Created", { status: 201 });
    }

    if (url.pathname !== "/connect") return new Response("Not found", { status: 404 });
    if (!(await this.ctx.storage.get("createdAt"))) {
      return json({ error: "Session not found or expired." }, 404);
    }

    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    const participant = {
      id: url.searchParams.get("id") || crypto.randomUUID(),
      name: url.searchParams.get("name") || "Player",
      host: url.searchParams.get("hostToken") === await this.ctx.storage.get("hostToken"),
    };
    server.serializeAttachment(participant);
    this.ctx.acceptWebSocket(server);
    server.send(JSON.stringify({ type: "connected", id: participant.id, name: participant.name }));
    this.broadcast({ type: "presence", ...participant }, server);
    return new Response(null, { status: 101, webSocket: client });
  }

  webSocketMessage(socket, message) {
    if (typeof message !== "string") return;
    let payload;
    try {
      payload = JSON.parse(message);
    } catch {
      return;
    }
    const participant = socket.deserializeAttachment() || {};
    payload.id = participant.id;
    payload.name = participant.name;
    payload.host = Boolean(participant.host);
    this.broadcast(payload);
  }

  async webSocketClose(socket) {
    const participant = socket.deserializeAttachment() || {};
    if (participant.host) {
      await this.endSession("Host closed the session");
      return;
    }
    this.broadcast({ type: "leave", id: participant.id, name: participant.name }, socket);
  }

  async webSocketError(socket) {
    const participant = socket.deserializeAttachment() || {};
    if (participant.host) {
      await this.endSession("Host disconnected");
      return;
    }
    this.broadcast({ type: "leave", id: participant.id, name: participant.name }, socket);
  }

  broadcast(payload, excluded = null) {
    const encoded = JSON.stringify(payload);
    for (const socket of this.ctx.getWebSockets()) {
      if (socket === excluded) continue;
      try {
        socket.send(encoded);
      } catch {
        // Dead sockets are removed by the runtime close/error callbacks.
      }
    }
  }

  async alarm() {
    await this.endSession("Session expired");
  }

  async endSession(reason) {
    for (const socket of this.ctx.getWebSockets()) {
      try { socket.close(1001, reason); } catch {}
    }
    await this.ctx.storage.deleteAll();
  }
}
