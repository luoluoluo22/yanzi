import { DurableObject } from 'cloudflare:workers';
import { DEVICE_MESSAGE_PROTOCOL, messageMatchesDevice } from './device-message-protocol.js';
import { isAccountWakeEvent } from './device-relay-events.js';

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
    server.serializeAttachment({ userId, deviceId, platform,
      credentialId: request.headers.get('X-Yanzi-Relay-Credential') || null,
      expiresAt: Number(request.headers.get('X-Yanzi-Relay-Expires') || 0),
      capabilities: { receiveAccountChat: request.headers.get('X-Yanzi-Relay-Account-Chat') === 'true' } });
    server.send(JSON.stringify({ type: 'ready', protocol: 1, protocolVersion: DEVICE_MESSAGE_PROTOCOL.version, serverNow: new Date().toISOString() }));
    return new Response(null, { status: 101, webSocket: client });
  }

  async publish(event) {
    let delivered = 0;
    for (const socket of this.ctx.getWebSockets()) {
      const peer = socket.deserializeAttachment();
      if (!peer || peer.userId !== event.userId) continue;
      if (peer.credentialId) {
        const credential = await this.env.DB.prepare('SELECT revoked_at, expires_at FROM device_credentials WHERE user_id = ? AND device_id = ? AND credential_id = ?')
          .bind(peer.userId, peer.deviceId, peer.credentialId).first();
        if (!credential || credential.revoked_at || credential.expires_at <= Date.now() / 1000) {
          try { socket.close(1008, 'Device credential expired or revoked'); } catch {}
          continue;
        }
      }
      const device = await this.env.DB.prepare('SELECT capabilities_json FROM user_devices WHERE user_id = ? AND device_id = ?').bind(peer.userId, peer.deviceId).first();
      if (!device || JSON.parse(device.capabilities_json || '{}').disabled === true) {
        try { socket.close(1008, 'Device registration removed'); } catch {}
        continue;
      }
      const message = event.message;
      const matches = isAccountWakeEvent(event) ? true : event.type === 'receipt'
        ? peer.deviceId === event.sourceDeviceId
        : messageMatchesDevice(message, peer);
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
  revoke(userId, deviceId) {
    for (const socket of this.ctx.getWebSockets()) {
      const peer = socket.deserializeAttachment();
      if (peer?.userId === userId && peer.deviceId === deviceId) socket.close(1008, 'Device registration removed');
    }
    return {ok:true};
  }
  revokeCredential(userId, credentialId) {
    for (const socket of this.ctx.getWebSockets()) {
      const peer = socket.deserializeAttachment();
      if (peer?.userId === userId && peer.credentialId === credentialId) socket.close(1008, 'Device credential revoked');
    }
    return {ok:true};
  }

  async webSocketMessage(socket, message) {
    const peer = socket.deserializeAttachment();
    if (peer?.credentialId && peer.expiresAt <= Date.now() / 1000) { socket.close(1008, 'Device credential expired'); return; }
    if (message === 'ping') socket.send('pong');
    else if (message === 'sync') socket.send(JSON.stringify({ type: 'messages-ready' }));
  }
  async webSocketClose(socket, code, reason) { try { socket.close(code, reason); } catch {} }
  async webSocketError(socket) { try { socket.close(1011, 'Reconnect'); } catch {} }
}
