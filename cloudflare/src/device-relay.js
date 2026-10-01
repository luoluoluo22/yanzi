import { DurableObject } from 'cloudflare:workers';

// One coordination object per account. Messages remain durable in D1, not in sockets.
export class DeviceRelay extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    this.ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair('ping', 'pong'));
  }

  async fetch(request) {
    const userId = request.headers.get('X-Yanzi-Relay-User');
    const deviceId = request.headers.get('X-Yanzi-Relay-Device');
    const platform = request.headers.get('X-Yanzi-Relay-Platform');
    if (request.headers.get('Upgrade')?.toLowerCase() !== 'websocket' || !userId || !deviceId)
      return new Response('Invalid relay handshake', { status: 400 });
    const pair = new WebSocketPair();
    const [client, server] = Object.values(pair);
    this.ctx.acceptWebSocket(server);
    server.serializeAttachment({ userId, deviceId, platform });
    server.send(JSON.stringify({ type: 'ready', protocol: 1, serverNow: new Date().toISOString() }));
    return new Response(null, { status: 101, webSocket: client });
  }

  async publish(event) {
    let delivered = 0;
    for (const socket of this.ctx.getWebSockets()) {
      const peer = socket.deserializeAttachment();
      if (!peer || peer.userId !== event.userId) continue;
      const message = event.message;
      const matches = event.type === 'receipt'
        ? peer.deviceId === event.sourceDeviceId
        : message.payload?.accountChat === true
          ? peer.deviceId !== message.sourceDeviceId && ['android', 'desktop'].includes(peer.platform)
          : message.targetDeviceId ? peer.deviceId === message.targetDeviceId : peer.platform === message.targetPlatform;
      if (!matches) continue;
      try {
        const body = JSON.stringify(event);
        socket.send(body.length <= 60000 ? body : JSON.stringify({ type: 'messages-ready' }));
        delivered++;
      } catch { try { socket.close(1011, 'Reconnect and resync'); } catch {} }
    }
    return { delivered };
  }
  isConnected(deviceId) { return this.ctx.getWebSockets().some(socket => socket.deserializeAttachment()?.deviceId === deviceId); }

  async webSocketMessage(socket, message) {
    if (message === 'ping') socket.send('pong');
    else if (message === 'sync') socket.send(JSON.stringify({ type: 'messages-ready' }));
  }
  async webSocketClose(socket, code, reason) { try { socket.close(code, reason); } catch {} }
  async webSocketError(socket) { try { socket.close(1011, 'Reconnect'); } catch {} }
}
