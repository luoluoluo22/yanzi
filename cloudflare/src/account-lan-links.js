// Only the authenticated account owner may fetch these device-to-device links.
export async function accountLanLink(secret, userId, source, peer, now = Date.now()) {
  const identities = [source.device_id, peer.device_id].sort();
  const context = JSON.stringify(['yanzi.account-lan.v1', userId, ...identities]);
  const signingKey = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), {name:'HMAC',hash:'SHA-256'}, false, ['sign']);
  const derive = async label => new Uint8Array(await crypto.subtle.sign('HMAC', signingKey, new TextEncoder().encode(label + context)));
  const pairId = [...await derive('id:')].slice(0,16).map(x => x.toString(16).padStart(2,'0')).join('');
  const key = btoa(String.fromCharCode(...await derive('key:')));
  const caps = JSON.parse(peer.capabilities_json || '{}');
  return {protocol:'yanzi.lan.aead.v1', mode:'account', ownerAccount:userId, pairId, key,
    deviceId:source.device_id, desktopDeviceId:peer.device_id, desktopName:peer.display_name || peer.device_id,
    peerPlatform:peer.platform, port:caps.lanPort || (peer.platform === 'desktop' ? 42980 : 42981),
    scopes:['chat','attachments','files.remote','terminal','extensions.run','account.owner'],
    expiresAt:new Date(now + 7 * 86400000).toISOString()};
}
