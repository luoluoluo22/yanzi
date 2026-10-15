const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const modulePath = path.resolve(__dirname, '..', 'app', 'music-session.js');
const Music = require(modulePath);

function makeStorage() {
  const map = new Map();
  return {
    getItem: key => map.get(key) ?? null,
    setItem: (key, value) => map.set(key, value),
    removeItem: key => map.delete(key)
  };
}
const recommend = [{song_id:10,name:'A'}, {song_id:11,name:'B'}];
const favorites = [{song_id:20,name:'C'}, {song_id:21,name:'D'}];

test('independent favorites and recommendation queues, retaining seconds', () => {
  const local = makeStorage();
  Music.save(local, 'recommend', 11, 70.75);
  Music.save(local, 'favorites', 21, 158.75);
  const rec = Music.findResume(local, 'recommend', recommend);
  const fav = Music.findResume(local, 'favorites', favorites);
  assert.equal(rec.song.song_id, 11);
  assert.equal(rec.seconds, 70.7);
  assert.equal(fav.song.song_id, 21);
  assert.equal(fav.seconds, 158.7);
});
test('missing song falls back to selected playlist first song, not other playlist', () => {
  const local = makeStorage();
  Music.save(local, 'recommend', 11, 21);
  Music.save(local, 'favorites', 999, 58);
  assert.equal(Music.findResume(local, 'favorites', favorites).song.song_id, 20);
  assert.equal(Music.findResume(local, 'favorites', favorites).seconds, 0);
});
test('legacy favorite state only accepted for favorite queue', () => {
  const local = makeStorage();
  assert.equal(Music.findResume(local, 'favorites', favorites, 21, 'favorites').song.song_id, 21);
  assert.equal(Music.findResume(local, 'recommend', recommend, 21, 'favorites').song.song_id, 10);
});
test('preview classification uses actual duration versus track duration', () => {
  assert.equal(Music.parseDuration('04:15'), 255);
  assert.equal(Music.parseDuration('01:02:03'), 3723);
  assert.equal(Music.isPreview(45, 255), true);
  assert.equal(Music.verifiedFull(45, 255), false);
  assert.equal(Music.verifiedFull(252, 255), true);
  assert.equal(Music.isPreview(190, 255), true);
});
test('resume seek clamps to media length and avoids expired clips', () => {
  assert.equal(Music.seekPosition(123, 250), 123);
  assert.equal(Music.seekPosition(238, 45), 0);
  assert.equal(Music.seekPosition(44, 45), 0);
  assert.equal(Music.seekPosition(0, 100), 0);
});
test('play URL refresh only affects remote source', () => {
  const url = 'https://api.injahow.cn/meting/?type=url&id=123';
  const fresh = Music.freshUrl(url, 1234567);
  assert.equal(new URL(fresh).searchParams.get('yanzi_request'), '1234567');
  assert.equal(new URL(fresh).searchParams.get('id'), '123');
  assert.equal(Music.freshUrl('../../../ExtensionStorage/song.mp3', 123), '../../../ExtensionStorage/song.mp3');
});
test('malformed persisted playback data cannot crash restoration', () => {
  const local = makeStorage();
  local.setItem(Music.KEY, '{not-json');
  assert.equal(Music.findResume(local, 'favorites', favorites).song.song_id, 20);
  Music.save(local, 'favorites', 21, 19.5);
  assert.equal(Music.findResume(local, 'favorites', favorites).seconds, 19.5);
});
