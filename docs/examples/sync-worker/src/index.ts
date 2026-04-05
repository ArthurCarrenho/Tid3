export interface Env {
  SYNC_SESSIONS: DurableObjectNamespace;
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const userId = url.searchParams.get("userId");
    const deviceId = url.searchParams.get("deviceId");
    const isWebSocket = request.headers.get("Upgrade") === "websocket";

    // Handle sync requests via path or websocket upgrade headers
    if (userId && deviceId && (url.pathname === "/sync" || isWebSocket)) {
      const id = env.SYNC_SESSIONS.idFromName(userId);
      const session = env.SYNC_SESSIONS.get(id);
      return session.fetch(request);
    }

    if (url.pathname === "/sync") {
      return new Response("Missing userId/deviceId", { status: 400 });
    }

    return new Response("Tidal Sync Worker Online. Connect using WebSocket with userId and deviceId.", { status: 200 });
  },
};

interface DeviceInfo {
  deviceId: string;
  deviceName: string;
  lastActive: number;
}

export class SyncSession {
  state: DurableObjectState;
  clients: Map<WebSocket, DeviceInfo> = new Map();
  playbackState: any = null;

  constructor(state: DurableObjectState) {
    this.state = state;
    this.state.blockConcurrencyWhile(async () => {
      this.playbackState = await this.state.storage.get("playbackState") || null;
    });
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const deviceId = url.searchParams.get("deviceId")!;
    const deviceName = url.searchParams.get("deviceName") || "Unknown Device";

    const [client, server] = Object.values(new WebSocketPair());
    server.accept();

    const info: DeviceInfo = { deviceId, deviceName, lastActive: Date.now() };
    this.clients.set(server, info);

    // Initial Sync: Send current state AND current devices
    server.send(JSON.stringify({
      type: "INIT",
      state: this.playbackState,
      devices: Array.from(this.clients.values())
    }));

    // Broadcast "Device Joined"
    this.broadcast({ type: "DEVICE_JOINED", device: info }, server);

    server.addEventListener("message", async (msg) => {
      try {
        const payload = JSON.parse(msg.data as string);
        info.lastActive = Date.now();

        switch (payload.type) {
          case "UPDATE_STATE":
            this.playbackState = payload.data;
            await this.state.storage.put("playbackState", this.playbackState);
            this.broadcast({ type: "SYNC_STATE", data: this.playbackState }, server);
            break;

          case "COMMAND":
            // Route command (PLAY, PAUSE, TRANSFER, etc.)
            this.broadcast(payload, server);
            break;

          case "PING":
            server.send(JSON.stringify({ type: "PONG" }));
            break;
        }
      } catch (e) { console.error("WS Error:", e); }
    });

    server.addEventListener("close", () => {
      this.clients.delete(server);
      this.broadcast({ type: "DEVICE_LEFT", deviceId: info.deviceId });
    });

    return new Response(null, { status: 101, webSocket: client });
  }

  broadcast(payload: any, sender?: WebSocket) {
    const json = JSON.stringify(payload);
    for (const [ws, info] of this.clients.entries()) {
      if (ws !== sender && ws.readyState === WebSocket.OPEN) {
        // If targeted command, only send to target
        if (payload.targetDeviceId && payload.targetDeviceId !== info.deviceId) continue;
        ws.send(json);
      }
    }
  }
}
