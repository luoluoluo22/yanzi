(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  root.YanziMusicSession = api;
})(typeof window === 'undefined' ? globalThis : window, function () {
  'use strict';
  const KEY = 'yanzi_music_resume_v2';
  const QUEUES = ['recommend', 'favorites'];

  function validQueue(key) {
    return QUEUES.includes(key) ? key : 'recommend';
  }
  function parseDuration(value) {
    if (typeof value === 'number') return Number.isFinite(value) && value > 0 ? value : 0;
    if (typeof value !== 'string') return 0;
    const parts = value.split(':').map(Number);
    if (parts.length < 2 || parts.length > 3 || parts.some(v => !Number.isFinite(v) || v < 0)) return 0;
    return parts.reduce((total, n) => total * 60 + n, 0);
  }
  function isPreview(actual, expected) {
    return Number.isFinite(actual) && actual > 1 &&
      Number.isFinite(expected) && expected >= 65 &&
      actual < Math.max(expected * 0.8, expected - 20);
  }
  function verifiedFull(actual, expected) {
    return Number.isFinite(actual) && actual > 1 &&
      Number.isFinite(expected) && expected > 0 &&
      actual >= Math.max(1, expected - 8);
  }
  function read(storage) {
    try {
      const value = JSON.parse(storage.getItem(KEY) || '{}');
      return value && typeof value === 'object' && !Array.isArray(value) ? value : {};
    } catch (_) { return {}; }
  }
  function save(storage, key, songId, seconds) {
    if (!QUEUES.includes(key) || !Number.isFinite(Number(songId)) || Number(songId) <= 0) return;
    const value = read(storage);
    value[key] = {
      songId: Number(songId),
      position: Number.isFinite(seconds) && seconds > 0 ? Math.floor(seconds * 10) / 10 : 0,
      updatedAt: Date.now()
    };
    try { storage.setItem(KEY, JSON.stringify(value)); } catch (_) {}
  }
  function findResume(storage, key, songs, legacyId, legacyKey) {
    const queue = validQueue(key);
    if (!Array.isArray(songs) || !songs.length) return null;
    const stored = read(storage)[queue];
    const match = stored && songs.find(s => Number(s.song_id) === Number(stored.songId));
    if (match) return { song: match, seconds: Number(stored.position) || 0 };
    const legacy = legacyKey === queue && songs.find(s => Number(s.song_id) === Number(legacyId));
    return { song: legacy || songs[0], seconds: 0 };
  }
  function freshUrl(url, timestamp) {
    if (typeof url !== 'string' || !/^https?:\/\//i.test(url)) return url;
    try {
      const parsed = new URL(url);
      parsed.searchParams.set('yanzi_request', String(timestamp || Date.now()));
      return parsed.toString();
    } catch (_) { return url; }
  }
  function seekPosition(seconds, duration) {
    if (!Number.isFinite(seconds) || seconds <= 0 || !Number.isFinite(duration) || duration <= 0) return 0;
    if (seconds >= duration - 2) return 0;
    return Math.min(seconds, duration - 2);
  }
  return { KEY, validQueue, parseDuration, isPreview, verifiedFull, read, save, findResume, freshUrl, seekPosition };
});
