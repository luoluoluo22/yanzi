$ErrorActionPreference='Stop'
$paths=@('F:\Desktop\kaifa\raccoon-mcp', (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\Extensions\raccoon-manager\service'))
foreach($root in $paths){
  $peer=Join-Path $root 'src\peer-manager.js'
  $body=[IO.File]::ReadAllText($peer)
  $old=@'
export function findPeer(deviceId) {
  return normalizedPeers().find(peer => peer.id === deviceId) || null;
}
'@
  $new=@'
export function resolveDeviceSelector(selector) {
  const key = String(selector ?? "").trim();
  if (!key) return localId;
  const localName = os.hostname();
  const peers = normalizedPeers();
  const matches = [
    ...(key.toLowerCase() === localName.toLowerCase() || key === localId ? [{ id: localId, name: localName }] : []),
    ...peers.filter(peer => peer.id === key || peer.name.toLowerCase() === key.toLowerCase())
  ];
  const ids = [...new Set(matches.map(item => item.id))];
  if (!ids.length) throw new Error(`Unknown Raccoon device "${key}". Available: ${[localName, ...peers.map(peer => peer.name)].join(", ")}`);
  if (ids.length > 1) throw new Error(`Ambiguous Raccoon device "${key}"; choose a unique deviceId.`);
  return ids[0];
}

export function findPeer(selector) {
  const id = resolveDeviceSelector(selector);
  if (id === localId) return null;
  return normalizedPeers().find(peer => peer.id === id) || null;
}
'@
  if($body.Contains($old)){ $body=$body.Replace($old,$new) }
  elseif(!$body.Contains('export function resolveDeviceSelector')){throw 'peer selector anchor absent'}
  $body=$body.Replace('if (!deviceId || deviceId === localId) return null;','if (!deviceId || resolveDeviceSelector(deviceId) === localId) return null;')
  $body=$body.Replace('if (targetId && targetId !== localId) {','if (targetId && resolveDeviceSelector(targetId) !== localId) {')
  [IO.File]::WriteAllText($peer,$body,[Text.UTF8Encoding]::new($false))
  $tools=Join-Path $root 'src\tools.js'
  $body=[IO.File]::ReadAllText($tools)
  $old='const register = server.registerTool.bind(server);'
  $new='const register = server.registerTool.bind(server);'
  # All native tools receive deviceId/deviceName and use the same forwarding logic as compatibility tools.
  $target='server.registerTool = (name, definition, handler) => register(name, {' 
  $replacement=@'
server.registerTool = (name, definition, handler) => register(name, {
    ...definition,
    inputSchema: {
      ...(definition.inputSchema || {}),
      deviceId: z.string().optional(),
      deviceName: z.string().optional()
    },
'@
  if($body.Contains($target) -and !$body.Contains('deviceName: z.string().optional()')){ $body=$body.Replace($target,$replacement) }
  $start='      if (config.readOnly && !readOnlyTools.has(name)) throw new Error(`${name} is disabled by RACCOON_READ_ONLY.`);'
  $inject=@'
      const request = args[0] ?? {};
      const selector = request.deviceName || request.deviceId;
      if (selector && resolveDeviceSelector(selector) !== localDeviceId()) {
        const forwarded = { ...request };
        delete forwarded.deviceId;
        delete forwarded.deviceName;
        const remote = await callPeerToolByDeviceId(selector, name, forwarded);
        ok = !remote?.isError;
        recordToolCall({ tool: name, args: { device: selector }, result: remote, ok, durationMs: Date.now() - started });
        return remote;
      }
      if (selector) {
        args[0] = { ...request };
        delete args[0].deviceId;
        delete args[0].deviceName;
      }
'@
  if(!$body.Contains('const selector = request.deviceName || request.deviceId;')){
    if(!$body.Contains($start)){ throw 'tool wrapper anchor absent' }
    $body=$body.Replace($start,$start+[Environment]::NewLine+$inject)
  }
  $body=$body.Replace('import { callPeerToolByDeviceId, localDeviceId } from "./peer-manager.js";','import { callPeerToolByDeviceId, localDeviceId, resolveDeviceSelector } from "./peer-manager.js";')
  [IO.File]::WriteAllText($tools,$body,[Text.UTF8Encoding]::new($false))
  Write-Output "ROUTING_UPDATED $root"
}
