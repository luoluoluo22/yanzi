// 燕子小程序 - 网易云音乐 (高密度宽幅布局 + 常驻底栏播放器)
let playlists = {
  recommend: [],
  favorites: []
};
let currentPlaylistKey = 'recommend'; // 'recommend' 或 'favorites'
let currentDisplayList = [];

// 正在播放的状态
let playingPlaylistKey = 'recommend';
let currentPlayingId = null;
let isPlaying = false;
let playbackLastError = '';
let playMode = 'list'; // 'list', 'random', 'single'
let filterMode = 'all'; // 'all', 'full'
let skipTrial = false;

// 缓存状态集合 (存储已缓存的 song_id)
const cachedSongIds = new Set();
let currentBlobUrl = null;

const DEFAULT_COVER = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'%3E%3Crect width='100' height='100' fill='%23252530'/%3E%3Ccircle cx='50' cy='50' r='30' fill='%23323242'/%3E%3Cpath d='M42 35v30l24-15z' fill='%23ec4141'/%3E%3C/svg%3E";

// DOM 元素
const audio = document.getElementById('audioPlayer');
const MusicSession = window.YanziMusicSession;
let playlistsInitialized = false;
let pendingSeek = null;
let loadGeneration = 0;
let lastCheckpointAt = 0;

function checkpointPlayback(force = false) {
  if (!MusicSession || !currentPlayingId || pendingSeek) return;
  if (!currentTrack() || !Number.isFinite(audio.currentTime)) return;
  const now = Date.now();
  if (!force && now - lastCheckpointAt < 3000) return;
  lastCheckpointAt = now;
  MusicSession.save(localStorage, playingPlaylistKey, currentPlayingId, audio.ended ? 0 : audio.currentTime);
}

function resumeFor(key) {
  return MusicSession.findResume(
    localStorage, key, playlists[key] || [],
    Number(localStorage.getItem('yanzi_music_song_id')),
    localStorage.getItem('yanzi_music_playlist_key') || key
  );
}

function currentTrack() {
  return (playlists[playingPlaylistKey] || []).find(song => Number(song.song_id) === Number(currentPlayingId));
}

function isRemoteAudio(url) { return /^https?:\/\//i.test(String(url || '')); }

function updateActualAudioBadge(song, actual) {
  if (!song || !Number.isFinite(actual) || actual <= 0) return;
  const expected = MusicSession.parseDuration(song.duration);
  const preview = MusicSession.isPreview(actual, expected);
  const full = MusicSession.verifiedFull(actual, expected);
  song.is_full = full;
  song.actual_duration = actual;
  song.preview_only = preview;
  songBadge.className = preview ? 'bar-badge trial' : 'bar-badge full';
  songBadge.textContent = preview ? ('试听 ' + Math.round(actual) + ' 秒') : (full ? '完整播放' : '实际音源');
  songBadge.title = preview
    ? ('当前音源仅 ' + formatTime(actual) + '，歌曲标注 ' + song.duration + '。已刷新本次可用播放地址。')
    : ('实际音频时长 ' + formatTime(actual));
  if (modalBadgeContainer) modalBadgeContainer.textContent = songBadge.textContent;
  const row = document.querySelector('.song-row[data-song-id="' + song.song_id + '"]');
  if (row) {
    const old = row.querySelector('.tag-vip-trial');
    if (full && old) old.remove();
    if (preview && !old) {
      const tag = document.createElement('span');
      tag.className = 'tag-vip-trial';
      tag.textContent = Math.round(actual) + '秒试听';
      row.querySelector('.cell-title-wrap')?.appendChild(tag);
    } else if (preview && old) {
      old.textContent = Math.round(actual) + '秒试听';
    }
  }
}

function waitForMediaMetadata(timeout = 2800) {
  if (audio.readyState >= 1 && Number.isFinite(audio.duration)) return Promise.resolve(true);
  return new Promise(resolve => {
    let done = false;
    const finish = ok => {
      if (done) return;
      done = true;
      audio.removeEventListener('loadedmetadata', onLoad);
      audio.removeEventListener('error', onError);
      clearTimeout(timer);
      resolve(ok);
    };
    const onLoad = () => finish(true);
    const onError = () => finish(false);
    const timer = setTimeout(() => finish(false), timeout);
    audio.addEventListener('loadedmetadata', onLoad);
    audio.addEventListener('error', onError);
    audio.load();
  });
}


// 底栏元素
const barCoverWrap = document.getElementById('barCoverWrap');
const coverImg = document.getElementById('coverImg');
const songTitle = document.getElementById('songTitle');
const songArtist = document.getElementById('songArtist');
const songBadge = document.getElementById('songBadge');

const btnPlay = document.getElementById('btnPlay');
const playIcon = document.getElementById('playIcon');
const btnPrev = document.getElementById('btnPrev');
const btnNext = document.getElementById('btnNext');
const btnMode = document.getElementById('btnMode');
const modeIcon = document.getElementById('modeIcon');

const progressBar = document.getElementById('progressBar');
const progressThumb = document.getElementById('progressThumb');
const progressContainer = document.getElementById('progressContainer');
const currentTimeEl = document.getElementById('currentTime');
const totalTimeEl = document.getElementById('totalTime');

const volumeSlider = document.getElementById('volumeSlider');
const btnVolume = document.getElementById('btnVolume');
const volumeIcon = document.getElementById('volumeIcon');

const skipTrialCheckbox = document.getElementById('skipTrialCheckbox');
const btnCacheAll = document.getElementById('btnCacheAll');
const cachedCountEl = document.getElementById('cachedCount');

// 客户端缓存监听收录与浮动提示
const syncSwitch = document.getElementById('syncSwitch');
const syncLabelText = document.getElementById('syncLabelText');
const btnManualSync = document.getElementById('btnManualSync');
const toastTip = document.getElementById('toastTip');

function showToast(text, duration = 2500) {
  if (!toastTip) return;
  toastTip.textContent = text;
  toastTip.classList.add('show');
  clearTimeout(showToast._timer);
  showToast._timer = setTimeout(() => {
    toastTip.classList.remove('show');
  }, duration);
}

// 列表与导航
const songListBody = document.getElementById('songListBody');
const tabNavRecommend = document.getElementById('tabNavRecommend');
const tabNavFavorites = document.getElementById('tabNavFavorites');
const recommendCountEl = document.getElementById('recommendCount');
const favoriteCountEl = document.getElementById('favoriteCount');

const tabAll = document.getElementById('tabAll');
const tabFullOnly = document.getElementById('tabFullOnly');
const totalCountEl = document.getElementById('totalCount');
const fullCountEl = document.getElementById('fullCount');
const searchInput = document.getElementById('searchInput');

// 歌单 Hero 操作栏 DOM
const playlistHeroBadge = document.getElementById('playlistHeroBadge');
const heroBadgeSvg = document.getElementById('heroBadgeSvg');
const heroBadgeDay = document.getElementById('heroBadgeDay');
const playlistHeroTitle = document.getElementById('playlistHeroTitle');
const playlistMetaSummary = document.getElementById('playlistMetaSummary');
const playlistMetaReady = document.getElementById('playlistMetaReady');
const btnPlayAll = document.getElementById('btnPlayAll');
const btnShufflePlay = document.getElementById('btnShufflePlay');
const btnModeCapsule = document.getElementById('btnModeCapsule');
const capsuleIcon = document.getElementById('capsuleIcon');
const capsuleText = document.getElementById('capsuleText');

// 播放队列抽屉 DOM
const btnQueueToggle = document.getElementById('btnQueueToggle');
const queueBadge = document.getElementById('queueBadge');
const queueDrawer = document.getElementById('queueDrawer');
const drawerBackdrop = document.getElementById('drawerBackdrop');
const btnQueueClose = document.getElementById('btnQueueClose');
const btnLocatePlaying = document.getElementById('btnLocatePlaying');
const drawerCountBadge = document.getElementById('drawerCountBadge');
const drawerList = document.getElementById('drawerList');

// 沉浸黑胶大视图
const vinylModal = document.getElementById('vinylModal');
const modalBackdrop = document.getElementById('modalBackdrop');
const btnModalClose = document.getElementById('btnModalClose');
const vinylDisc = document.getElementById('vinylDisc');
const vinylTonearm = document.getElementById('vinylTonearm');
const modalCoverImg = document.getElementById('modalCoverImg');
const modalSongTitle = document.getElementById('modalSongTitle');
const modalSongArtist = document.getElementById('modalSongArtist');
const modalSongAlbum = document.getElementById('modalSongAlbum');
const modalBadgeContainer = document.getElementById('modalBadgeContainer');
const modalBtnPrev = document.getElementById('modalBtnPrev');
const modalBtnPlay = document.getElementById('modalBtnPlay');
const modalBtnNext = document.getElementById('modalBtnNext');
const modalPlayIcon = document.getElementById('modalPlayIcon');

// K歌实时歌词 DOM 与状态
const lyricsViewport = document.getElementById('lyricsViewport');
const lyricsScrollWrap = document.getElementById('lyricsScrollWrap');
const btnToggleTrans = document.getElementById('btnToggleTrans');
const btnLyricsFontUp = document.getElementById('btnLyricsFontUp');
const btnLyricsFontDown = document.getElementById('btnLyricsFontDown');

let currentLyrics = [];
let currentLyricIndex = -1;
let currentLyricsSongId = null;
let showTranslation = true;
let lyricFontSize = parseInt(localStorage.getItem('yanzi_music_lyric_font_size') || '16', 10);
const lyricsCache = new Map();
let isUserScrollingLyrics = false;
let userScrollTimer = null;

// 封面图片加载兜底
coverImg.onerror = function() { this.src = DEFAULT_COVER; };
modalCoverImg.onerror = function() { this.src = DEFAULT_COVER; };

// --- IndexedDB 本地音频缓存引擎 ---
const DB_NAME = 'YanziMusicStorage';
const DB_VERSION = 1;
const STORE_NAME = 'audio_cache';
let dbInstance = null;

function openDB() {
  return new Promise((resolve, reject) => {
    if (dbInstance) return resolve(dbInstance);
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = (e) => {
      const db = e.target.result;
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME, { keyPath: 'song_id' });
      }
    };
    req.onsuccess = (e) => {
      dbInstance = e.target.result;
      resolve(dbInstance);
    };
    req.onerror = (e) => reject(e);
  });
}

async function getCachedAudio(songId) {
  try {
    const db = await openDB();
    return new Promise((resolve) => {
      const tx = db.transaction(STORE_NAME, 'readonly');
      const store = tx.objectStore(STORE_NAME);
      const req = store.get(songId);
      req.onsuccess = () => resolve(req.result?.verifiedFull === true ? req.result.blob : null);
      req.onerror = () => resolve(null);
    });
  } catch (err) {
    console.warn('IndexedDB get error:', err);
    return null;
  }
}

async function saveCachedAudio(songId, blob, verifiedDuration = 0) {
  try {
    const db = await openDB();
    return new Promise((resolve) => {
      const tx = db.transaction(STORE_NAME, 'readwrite');
      const store = tx.objectStore(STORE_NAME);
      store.put({
        song_id: songId,
        blob: blob,
        size: blob.size,
        saved_at: Date.now(),
        verifiedFull: verifiedDuration > 0,
        verifiedDuration
      });
      tx.oncomplete = () => {
        cachedSongIds.add(songId);
        updateCacheCount();
        updateRowCacheStatus(songId, true);
        resolve(true);
      };
      tx.onerror = () => resolve(false);
    });
  } catch (err) {
    console.warn('IndexedDB save error:', err);
    return false;
  }
}

async function loadAllCachedIds() {
  try {
    const db = await openDB();
    const tx = db.transaction(STORE_NAME, 'readonly');
    const store = tx.objectStore(STORE_NAME);
    const req = store.getAll();
    req.onsuccess = () => {
      cachedSongIds.clear();
      (req.result || []).filter(item => item?.verifiedFull).forEach(item => cachedSongIds.add(item.song_id));
      updateCacheCount();
      renderList(currentDisplayList);
    };
  } catch (err) {
    console.warn('Failed to load cached keys:', err);
  }
}

function updateCacheCount() {
  if (cachedCountEl) {
    cachedCountEl.textContent = cachedSongIds.size;
  }
}

function updateRowCacheStatus(songId, isCached) {
  const row = document.querySelector(`.song-row[data-song-id="${songId}"]`);
  if (row) {
    const durCell = row.querySelector('.cell-duration');
    if (durCell) {
      const dot = durCell.querySelector('.icon-cached-dot');
      if (isCached && !dot) {
        const svg = document.createElement('span');
        svg.innerHTML = `<svg viewBox="0 0 24 24" class="icon-cached-dot" title="已本地缓存"><path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"/></svg>`;
        durCell.insertBefore(svg.firstChild, durCell.firstChild);
      }
    }
  }
}

function formatTime(seconds) {
  if (isNaN(seconds) || seconds < 0) return '00:00';
  const mins = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${mins < 10 ? '0' : ''}${mins}:${secs < 10 ? '0' : ''}${secs}`;
}

// 加载两张歌单数据 (带时间戳确保获取最新解密歌曲)
async function loadPlaylists() {
  let loaded = false;
  try {
    const res = await fetch(`playlists.json?t=${Date.now()}`, { cache: 'no-store' });
    if (res.ok) {
      const data = await res.json();
      playlists.recommend = data.recommend || [];
      playlists.favorites = data.favorites || [];
      loaded = true;
    }
  } catch (err) {
    console.warn('Fetch playlists.json failed:', err);
  }

  if (!loaded) {
    if (window.__NETEASE_PLAYLISTS__) {
      playlists.recommend = window.__NETEASE_PLAYLISTS__.recommend || [];
      playlists.favorites = window.__NETEASE_PLAYLISTS__.favorites || [];
    } else if (window.PLAYLISTS_DATA) {
      playlists.recommend = window.PLAYLISTS_DATA.recommend || [];
      playlists.favorites = window.PLAYLISTS_DATA.favorites || [];
    }
  }

  recommendCountEl.textContent = playlists.recommend.length;
  favoriteCountEl.textContent = playlists.favorites.length;

  await loadAllCachedIds();

  // 0. 记忆恢复：恢复上次保存的播放模式
  const savedMode = localStorage.getItem('yanzi_music_play_mode') || 'list';
  setPlayMode(savedMode, false);

  // Manual refresh may replace song metadata, but MUST NOT restart playback.
  if (playlistsInitialized) {
    applyFilter();
    return;
  }
  playlistsInitialized = true;

  const savedTab = MusicSession.validQueue(localStorage.getItem('yanzi_music_tab') || 'recommend');
  playingPlaylistKey = savedTab;
  switchPlaylist(savedTab, false);
  const stored = resumeFor(savedTab);
  if (stored) {
    await loadTrack(stored.song, false, { restorePosition: stored.seconds });
    setTimeout(() => {
      const row = document.querySelector('.song-row[data-song-id="' + stored.song.song_id + '"]');
      row?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }, 250);
  }
}

// 切换当前浏览的歌单 (持久化记录当前 Tab)
async function switchPlaylist(key, resetSearch = true) {
  key = MusicSession.validQueue(key);
  const changed = currentPlaylistKey !== key;
  const wasPlaying = !audio.paused && !!currentPlayingId;
  if (changed && resetSearch) checkpointPlayback(true);
  currentPlaylistKey = key;
  localStorage.setItem('yanzi_music_tab', key);
  if (key === 'recommend') {
    tabNavRecommend.classList.add('active');
    tabNavFavorites.classList.remove('active');
  } else {
    tabNavFavorites.classList.add('active');
    tabNavRecommend.classList.remove('active');
  }
  if (resetSearch) searchInput.value = '';
  applyFilter();
  if (changed && resetSearch) {
    playingPlaylistKey = key;
    const stored = resumeFor(key);
    if (stored)
      await loadTrack(stored.song, wasPlaying, { restorePosition: stored.seconds });
  }
}

function getCurrentListSource() {
  return playlists[currentPlaylistKey] || [];
}

function applyFilter() {
  const listSource = getCurrentListSource();
  const query = searchInput.value.trim().toLowerCase();
  let filtered = listSource;

  if (filterMode === 'full') {
    filtered = filtered.filter(s => s.is_full);
  }

  if (query) {
    filtered = filtered.filter(s =>
      (s.name && s.name.toLowerCase().includes(query)) ||
      (s.artists && s.artists.toLowerCase().includes(query)) ||
      (s.album && s.album.toLowerCase().includes(query))
    );
  }

  currentDisplayList = filtered;
  totalCountEl.textContent = listSource.length;
  fullCountEl.textContent = listSource.filter(s => s.is_full).length;

  updatePlaylistHeroInfo();
  renderList(currentDisplayList);
}

// 渲染紧凑宽幅列表
function renderList(list) {
  songListBody.innerHTML = '';
  list.forEach((item) => {
    const tr = document.createElement('tr');
    const isCur = (currentPlayingId === item.song_id);
    tr.className = `song-row ${isCur ? 'active' : ''}`;
    tr.dataset.songId = item.song_id;

    const isCached = cachedSongIds.has(item.song_id);
    const cachedIconHtml = isCached
      ? `<svg viewBox="0 0 24 24" class="icon-cached-dot" title="已本地缓存"><path fill="currentColor" d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"/></svg>`
      : ``;

    // 序号列：如果是正在播放的曲目，展示高保真动态波形跳动图标
    let numHtml = item.index;
    if (isCur) {
      numHtml = isPlaying
        ? `<div class="playing-equalizer active" title="正在播放"><span class="eq-bar bar-1"></span><span class="eq-bar bar-2"></span><span class="eq-bar bar-3"></span><span class="eq-bar bar-4"></span></div>`
        : `<div class="playing-equalizer" title="已暂停"><span class="eq-bar bar-1"></span><span class="eq-bar bar-2"></span><span class="eq-bar bar-3"></span><span class="eq-bar bar-4"></span></div>`;
    }

    // 标签微标
    let tagsHtml = '';
    if (item.has_local) {
      tagsHtml += `<span class="tag-local" title="网易云客户端已下载无损母带">本地原声</span>`;
    } else if (!item.is_full) {
      tagsHtml += `<span class="tag-vip-trial" title="VIP专享，当前为试听片段">VIP</span>`;
    }
    if (item.reason && item.reason !== '红心喜爱' && item.reason !== '本地已下载') {
      tagsHtml += `<span class="tag-recommend-reason">${item.reason}</span>`;
    }

    const coverSrc = item.cover_url || DEFAULT_COVER;

    tr.innerHTML = `
      <td class="td-num">${numHtml}</td>
      <td class="td-title">
        <div class="cell-title-wrap">
          <img src="${coverSrc}" class="mini-cover" loading="lazy" alt="" onerror="this.src='${DEFAULT_COVER}'">
          <span class="song-title-text" title="${item.name}">${item.name}</span>
          ${tagsHtml}
        </div>
      </td>
      <td class="cell-artist" title="${item.artists}">${item.artists}</td>
      <td class="cell-album" title="${item.album}">${item.album}</td>
      <td class="cell-duration">${cachedIconHtml}<span>${item.duration || '00:00'}</span></td>
    `;

    tr.addEventListener('click', () => {
      playingPlaylistKey = currentPlaylistKey;
      const stored = resumeFor(currentPlaylistKey);
      const restorePosition = stored && Number(stored.song.song_id) === Number(item.song_id)
        ? stored.seconds : 0;
      void loadTrack(item, true, { restorePosition });
    });

    songListBody.appendChild(tr);
  });
}

// 核心播放：优先从本地 IndexedDB 缓存读取，未缓存时发起异步后台缓存
async function loadTrack(song, autoPlay = true, options = {}) {
  if (!song) return false;
  checkpointPlayback(true);
  const generation = ++loadGeneration;

  // 若开启了“自动跳过试听”，且当前选中的是试听歌曲，自动找下一首完整版
  if (skipTrial && !song.is_full) {
    const nextSong = findNextFullSong(song);
    if (nextSong && nextSong.song_id !== song.song_id) {
      song = nextSong;
    }
  }

  currentPlayingId = Number(song.song_id);
  localStorage.setItem('yanzi_music_song_id', String(song.song_id));
  localStorage.setItem('yanzi_music_playlist_key', playingPlaylistKey);
  const seekSeconds = Number(options.restorePosition) || 0;
  pendingSeek = { songId: Number(song.song_id), seconds: seekSeconds };
  if (seekSeconds > 0) currentTimeEl.textContent = formatTime(seekSeconds);

  // 更新底栏信息
  songTitle.textContent = song.name;
  songTitle.title = song.name;
  songArtist.textContent = song.artists;
  songArtist.title = song.artists;
  const coverSrc = song.cover_url || DEFAULT_COVER;
  coverImg.src = coverSrc;

  // 更新底栏状态徽章
  if (song.has_local) {
    songBadge.className = 'bar-badge full';
    songBadge.textContent = '本地原声';
    songBadge.title = '网易云客户端已下载母带音频，免流量无损播放';
  } else if (song.is_full) {
    songBadge.className = 'bar-badge full';
    songBadge.textContent = '完整';
    songBadge.title = '完整音质畅听';
  } else {
    songBadge.className = 'bar-badge trial';
    songBadge.textContent = 'VIP试听';
    songBadge.title = '非VIP账号试听片段 (约30s)';
  }

  // 同步更新沉浸黑胶大视图
  modalSongTitle.textContent = song.name;
  modalSongArtist.textContent = song.artists;
  modalSongAlbum.textContent = song.album ? `专辑: ${song.album}` : '';
  modalCoverImg.src = coverSrc;
  modalBadgeContainer.innerHTML = song.has_local
    ? `<span class="bar-badge full">客户端本地已下载母带 (${song.duration || '全曲'})</span>`
    : (song.is_full
      ? `<span class="bar-badge full">完整畅听 (${song.duration || '全曲'})</span>`
      : `<span class="bar-badge trial">VIP 试听片段 (约30秒)</span>`);

  // 异步加载该曲目的高保真K歌歌词
  loadLyricsForSong(song.song_id);

  // 系统级多媒体元数据更新
  setupMediaSession(song);

  // 释放上一个 ObjectURL
  if (currentBlobUrl) {
    URL.revokeObjectURL(currentBlobUrl);
    currentBlobUrl = null;
  }

  // 0. 本地路径纠错与即时优先探测 (Just-In-Time Local Probe)
  if (song.audio_url && song.audio_url.includes('../../../../ExtensionStorage/')) {
    song.audio_url = song.audio_url.replace('../../../../ExtensionStorage/', '../../../ExtensionStorage/');
    song.has_local = true;
    song.is_full = true;
  }

  // 若尚未标记本地原声，即时探测 ExtensionStorage 目录是否已有热同步解密文件
  if (!song.has_local) {
    const candidateFiles = [
      `../../../ExtensionStorage/yanzi-cloudmusic-player/audio_cache/${song.song_id}.m4a`,
      `../../../ExtensionStorage/yanzi-cloudmusic-player/audio_cache/${song.song_id}.mp3`,
      `../../../ExtensionStorage/yanzi-cloudmusic-player/audio_cache/${song.song_id}.flac`
    ];
    for (const testUrl of candidateFiles) {
      try {
        const testResp = await fetch(testUrl, { method: 'HEAD' });
        if (testResp.ok) {
          song.audio_url = testUrl;
          song.has_local = true;
          song.is_full = true;
          songBadge.className = 'bar-badge full';
          songBadge.textContent = '本地原声';
          break;
        }
      } catch (_) {}
    }
  }

  // Per-track fresh request. Old unverified IndexedDB clips never shadow it.
  const storedSource = song.audio_url || '';
  if (storedSource.startsWith('../../')) {
    audio.src = storedSource;
    cachedSongIds.add(song.song_id);
    updateCacheCount();
    updateRowCacheStatus(song.song_id, true);

    // A previously copied local audio clip might itself be a short preview.
    // Retry through the same public playback provider; no access bypass.
    if (MusicSession.parseDuration(song.duration) >= 70) {
      await waitForMediaMetadata();
      if (generation !== loadGeneration) return false;
      if (MusicSession.isPreview(audio.duration, MusicSession.parseDuration(song.duration))) {
        const localSource = audio.src;
        if (seekSeconds > 0) pendingSeek = { songId: Number(song.song_id), seconds: seekSeconds };
        const fresh = MusicSession.freshUrl(
          'https://api.injahow.cn/meting/?type=url&id=' + encodeURIComponent(song.song_id));
        audio.src = fresh;
        const usable = await waitForMediaMetadata(4500);
        if (generation !== loadGeneration) return false;
        if (!usable) {
          audio.src = localSource;
          await waitForMediaMetadata(1800);
        }
      }
    }
  } else if (isRemoteAudio(storedSource)) {
    audio.src = MusicSession.freshUrl(storedSource);
    audio.load();
  } else {
    const verified = await getCachedAudio(song.song_id);
    if (generation !== loadGeneration) return false;
    if (verified) {
      currentBlobUrl = URL.createObjectURL(verified);
      audio.src = currentBlobUrl;
    } else {
      console.warn('No usable audio source for song', song.song_id);
      return false;
    }
  }

  // 更新列表高亮行
  document.querySelectorAll('.song-row').forEach(row => {
    if (row.dataset.songId == song.song_id) {
      row.classList.add('active');
    } else {
      row.classList.remove('active');
    }
  });

  if (generation !== loadGeneration) return false;
  if (autoPlay) return await playAudio();
  pauseAudio();
  return true;
}

// 后台异步缓存音频数据
async function fetchAndCacheAudio(songId, url) {
  if (!isRemoteAudio(url)) return false;
  const song = playlists.recommend.concat(playlists.favorites)
    .find(item => Number(item.song_id) === Number(songId));
  const expected = MusicSession.parseDuration(song?.duration);
  if (!expected) return false;
  try {
    const resp = await fetch(MusicSession.freshUrl(url), { cache: 'no-store' });
    if (!resp.ok) return false;
    const blob = await resp.blob();
    if (blob.size < 1024 || blob.size > 60 * 1024 * 1024) return false;
    const blobUrl = URL.createObjectURL(blob);
    try {
      const probe = new Audio();
      const actual = await new Promise(resolve => {
        const timer = setTimeout(() => resolve(0), 6500);
        probe.onloadedmetadata = () => { clearTimeout(timer); resolve(probe.duration); };
        probe.onerror = () => { clearTimeout(timer); resolve(0); };
        probe.src = blobUrl;
      });
      if (!MusicSession.verifiedFull(actual, expected)) return false;
      return await saveCachedAudio(songId, blob, actual);
    } finally { URL.revokeObjectURL(blobUrl); }
  } catch (err) {
    console.warn('[AutoCache] Track not cached (network/permission):', songId, err);
    return false;
  }
}

// 一键预缓存当前歌单
async function precacheCurrentPlaylist() {
  btnCacheAll.disabled = true;
  const originalSpan = btnCacheAll.querySelector('span');

  const listSource = getCurrentListSource();
  let doneCount = 0;
  let newlyCached = 0;
  for (const s of listSource) {
    if (!cachedSongIds.has(s.song_id)) {
      if (originalSpan) originalSpan.textContent = `缓存 (${doneCount + 1}/${listSource.length})`;
      if (await fetchAndCacheAudio(s.song_id, s.audio_url)) newlyCached++;
      doneCount++;
    }
  }

  btnCacheAll.disabled = false;
  if (originalSpan) originalSpan.textContent = '缓存完成';
  showToast('已验证并新增缓存 ' + newlyCached + ' 首，试听片段不会伪装成完整歌曲。', 4000);
  setTimeout(() => {
    updateCacheCount();
  }, 2500);
}

function findNextFullSong(currentSong) {
  const currentPool = playlists[playingPlaylistKey] || playlists.recommend;
  const curIdx = currentPool.findIndex(s => s.song_id === currentSong.song_id);
  for (let step = 1; step <= currentPool.length; step++) {
    const nextIdx = (curIdx + step) % currentPool.length;
    if (currentPool[nextIdx].is_full) return currentPool[nextIdx];
  }
  return null;
}

// --- 播放模式与高级播放控制 (4 种模式：列表循环 / 随机播放 / 单曲循环 / 顺序播放) ---
const PLAY_MODES = [
  {
    key: 'list',
    name: '列表循环',
    icon: '<path fill="currentColor" d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z"/>',
    tip: '顺序播放并循环往复'
  },
  {
    key: 'random',
    name: '随机播放',
    icon: '<path fill="currentColor" d="M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z"/>',
    tip: '随机无序随心畅听'
  },
  {
    key: 'single',
    name: '单曲循环',
    icon: '<path fill="currentColor" d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4zm-4-2V9h-1l-2 1v1h1.5v4H13z"/>',
    tip: '单首歌曲循环沉浸'
  },
  {
    key: 'order',
    name: '顺序播放',
    icon: '<path fill="currentColor" d="M3 5h18v2H3V5zm0 6h12v2H3v-2zm0 6h8v2H3v-2zm16-4v6h-2v-6h-2l3-3 3 3h-2z"/>',
    tip: '顺序播放，播到最后自动停止'
  }
];

function setPlayMode(newModeKey, showFeedback = true) {
  const modeObj = PLAY_MODES.find(m => m.key === newModeKey) || PLAY_MODES[0];
  playMode = modeObj.key;
  localStorage.setItem('yanzi_music_play_mode', playMode);

  // 1. 同步底栏按钮
  if (btnMode && modeIcon) {
    btnMode.title = `播放模式: ${modeObj.name} (快捷键 P)`;
    modeIcon.innerHTML = modeObj.icon;
  }

  // 2. 同步头部 Hero 胶囊
  if (capsuleText && capsuleIcon) {
    capsuleText.textContent = modeObj.name;
    capsuleIcon.innerHTML = `<svg viewBox="0 0 24 24">${modeObj.icon}</svg>`;
  }

  if (showFeedback) {
    showToast(`播放模式：${modeObj.name}`, 1500);
  }
}

function cyclePlayMode() {
  const curIdx = PLAY_MODES.findIndex(m => m.key === playMode);
  const nextIdx = (curIdx + 1) % PLAY_MODES.length;
  setPlayMode(PLAY_MODES[nextIdx].key, true);
}

// 刷新或切换歌单时同步 Hero 头部信息
function updatePlaylistHeroInfo() {
  const isRec = (currentPlaylistKey === 'recommend');
  const pool = playlists[currentPlaylistKey] || [];
  const fullCount = pool.filter(s => s.is_full).length;

  if (playlistHeroTitle) {
    playlistHeroTitle.textContent = isRec ? '每日推荐' : '我喜欢的音乐';
  }

  if (playlistHeroBadge) {
    if (isRec) {
      playlistHeroBadge.classList.remove('badge-favorite');
      const today = new Date().getDate();
      if (heroBadgeDay) heroBadgeDay.textContent = String(today);
      if (heroBadgeSvg) {
        heroBadgeSvg.innerHTML = '<path fill="currentColor" d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-7 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm6 12H6v-1c0-2 4-3.1 6-3.1s6 1.1 6 3.1v1z"/>';
      }
    } else {
      playlistHeroBadge.classList.add('badge-favorite');
      if (heroBadgeDay) heroBadgeDay.textContent = '';
      if (heroBadgeSvg) {
        heroBadgeSvg.innerHTML = '<path fill="currentColor" d="M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z"/>';
      }
    }
  }

  if (playlistMetaSummary) {
    playlistMetaSummary.textContent = isRec
      ? `精心聚合每日专属推荐 · ${pool.length} 首歌曲`
      : `珍藏心动旋律 · ${pool.length} 首歌曲`;
  }

  if (playlistMetaReady) {
    playlistMetaReady.textContent = `${fullCount} 首无损母带就绪`;
  }

  if (queueBadge) {
    queueBadge.textContent = pool.length;
  }
}

// 播放全部 / 随机播放全部
function playAllTracks(shuffle = false) {
  const pool = (currentDisplayList && currentDisplayList.length > 0)
    ? currentDisplayList
    : (playlists[currentPlaylistKey] || playlists.recommend);

  if (!pool || pool.length === 0) {
    showToast('当前列表没有可播放歌曲', 2000);
    return;
  }

  playingPlaylistKey = currentPlaylistKey;

  if (shuffle) {
    // 随机播放模式
    setPlayMode('random', false);
    const randomIndex = Math.floor(Math.random() * pool.length);
    const chosenSong = pool[randomIndex];
    loadTrack(chosenSong, true);
    showToast(`开启随机播放 (共 ${pool.length} 首)`, 2200);
  } else {
    // 列表循环模式，从第 1 首开始
    setPlayMode('list', false);
    const firstSong = pool[0];
    loadTrack(firstSong, true);
    showToast(`开始顺序播放全部 (共 ${pool.length} 首)`, 2200);
  }
}

// 播放队列抽屉 (Queue Drawer)
function toggleQueueDrawer(forceState) {
  if (!queueDrawer) return;
  const isOpen = (typeof forceState === 'boolean') ? forceState : !queueDrawer.classList.contains('open');
  if (isOpen) {
    queueDrawer.classList.add('open');
    if (drawerBackdrop) drawerBackdrop.classList.add('open');
    renderQueueDrawer();
  } else {
    queueDrawer.classList.remove('open');
    if (drawerBackdrop) drawerBackdrop.classList.remove('open');
  }
}

function renderQueueDrawer() {
  if (!drawerList) return;
  const pool = playlists[playingPlaylistKey] || playlists.recommend;
  if (drawerCountBadge) drawerCountBadge.textContent = `共 ${pool.length} 首`;

  drawerList.innerHTML = '';
  pool.forEach((item) => {
    const isCur = (item.song_id === currentPlayingId);
    const div = document.createElement('div');
    div.className = `drawer-item ${isCur ? 'active' : ''}`;
    div.dataset.songId = item.song_id;

    const eqHtml = isCur && isPlaying
      ? `<div class="playing-equalizer active" style="margin-right:4px"><span class="eq-bar bar-1"></span><span class="eq-bar bar-2"></span><span class="eq-bar bar-3"></span><span class="eq-bar bar-4"></span></div>`
      : ``;

    div.innerHTML = `
      <div class="drawer-item-left">
        ${eqHtml}
        <span class="drawer-item-title" title="${item.name}">${item.name}</span>
        <span class="drawer-item-artist" title="${item.artists}">- ${item.artists}</span>
      </div>
      <span class="drawer-item-duration">${item.duration || '00:00'}</span>
    `;

    div.addEventListener('click', () => {
      loadTrack(item, true);
      renderQueueDrawer();
    });

    drawerList.appendChild(div);
  });

  // 自动滚动到当前播放行
  const activeEl = drawerList.querySelector('.drawer-item.active');
  if (activeEl) {
    activeEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }
}

function locateCurrentSongInMainList() {
  if (!currentPlayingId) return;
  const targetRow = document.querySelector(`.song-row[data-song-id="${currentPlayingId}"]`);
  if (targetRow) {
    targetRow.scrollIntoView({ behavior: 'smooth', block: 'center' });
    targetRow.classList.add('active');
    showToast('已定位至当前播放曲目', 1200);
  }
}

async function playAudio() {
  try {
    if (!audio.getAttribute('src')) {
      const pool = playlists[playingPlaylistKey] || playlists.recommend;
      if (!pool.length) throw new Error('歌单为空，没有可播放的歌曲');
      await loadTrack(pool[0], false);
    }
    let playbackTimer;
    try {
      await Promise.race([
        audio.play(),
        new Promise((_, reject) => {
          playbackTimer = setTimeout(() => reject(new Error('音频启动超过25秒')), 25000);
        })
      ]);
    } finally {
      clearTimeout(playbackTimer);
    }
    playbackLastError = '';
    isPlaying = !audio.paused && !audio.error;
    if (!isPlaying) throw new Error('播放器没有进入播放状态');

    // 视觉动画与按钮状态同步
    vinylDisc.classList.add('spinning');
    if (vinylTonearm) vinylTonearm.classList.add('playing');
    playIcon.innerHTML = '<path fill="currentColor" d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/>';
    btnPlay.title = '暂停';
    if (modalPlayIcon) modalPlayIcon.innerHTML = '<path fill="currentColor" d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/>';

    // 更新列表中的动态均衡器
    const curRow = document.querySelector(`.song-row[data-song-id="${currentPlayingId}"]`);
    if (curRow) {
      const numCell = curRow.querySelector('.td-num');
      if (numCell) numCell.innerHTML = `<div class="playing-equalizer active" title="正在播放"><span class="eq-bar bar-1"></span><span class="eq-bar bar-2"></span><span class="eq-bar bar-3"></span><span class="eq-bar bar-4"></span></div>`;
    }

    if (queueDrawer && queueDrawer.classList.contains('open')) {
      renderQueueDrawer();
    }
    return true;
  } catch (err) {
    audio.pause();
    isPlaying = false;
    playbackLastError = err?.message || String(err);
    console.error('[MusicPlayback] Failed:', err);
    showToast('播放失败：' + (err?.message || String(err)), 3500);
    return false;
  }
}

function pauseAudio() {
  checkpointPlayback(true);
  audio.pause();
  isPlaying = false;
  vinylDisc.classList.remove('spinning');
  if (vinylTonearm) vinylTonearm.classList.remove('playing');
  playIcon.innerHTML = '<path fill="currentColor" d="M8 5v14l11-7z"/>';
  btnPlay.title = '播放';
  if (modalPlayIcon) modalPlayIcon.innerHTML = '<path fill="currentColor" d="M8 5v14l11-7z"/>';

  const curRow = document.querySelector(`.song-row[data-song-id="${currentPlayingId}"]`);
  if (curRow) {
    const numCell = curRow.querySelector('.td-num');
    if (numCell) numCell.innerHTML = `<div class="playing-equalizer" title="已暂停"><span class="eq-bar bar-1"></span><span class="eq-bar bar-2"></span><span class="eq-bar bar-3"></span><span class="eq-bar bar-4"></span></div>`;
  }

  if (queueDrawer && queueDrawer.classList.contains('open')) {
    renderQueueDrawer();
  }
}

async function togglePlay() {
  if (!audio.paused) {
    pauseAudio();
    showToast('已暂停播放', 1200);
    return true;
  }
  return await playAudio();
}

function nextTrack() {
  const currentPool = playlists[playingPlaylistKey] || playlists.recommend;
  if (currentPool.length === 0) return;

  let targetPool = (filterMode === 'full' || skipTrial) ? currentPool.filter(s => s.is_full) : currentPool;
  if (targetPool.length === 0) targetPool = currentPool;

  if (playMode === 'random') {
    let nextItem;
    if (targetPool.length === 1) {
      nextItem = targetPool[0];
    } else {
      const candidates = targetPool.filter(s => s.song_id !== currentPlayingId);
      nextItem = candidates[Math.floor(Math.random() * candidates.length)] || targetPool[0];
    }
    return loadTrack(nextItem, true);
  } else if (playMode === 'order') {
    let curPoolIdx = targetPool.findIndex(s => s.song_id === currentPlayingId);
    if (curPoolIdx >= targetPool.length - 1) {
      pauseAudio();
      showToast('已播放到列表中最后一首', 2000);
      return;
    }
    return loadTrack(targetPool[curPoolIdx + 1], true);
  } else {
    // 列表循环 / 单曲循环手动切下一首
    let curPoolIdx = targetPool.findIndex(s => s.song_id === currentPlayingId);
    let nextPoolIdx = (curPoolIdx + 1) % targetPool.length;
    return loadTrack(targetPool[nextPoolIdx], true);
  }
}

function prevTrack() {
  const currentPool = playlists[playingPlaylistKey] || playlists.recommend;
  if (currentPool.length === 0) return;

  let targetPool = (filterMode === 'full' || skipTrial) ? currentPool.filter(s => s.is_full) : currentPool;
  if (targetPool.length === 0) targetPool = currentPool;

  if (playMode === 'random') {
    let prevItem;
    if (targetPool.length === 1) {
      prevItem = targetPool[0];
    } else {
      const candidates = targetPool.filter(s => s.song_id !== currentPlayingId);
      prevItem = candidates[Math.floor(Math.random() * candidates.length)] || targetPool[0];
    }
    return loadTrack(prevItem, true);
  } else {
    let curPoolIdx = targetPool.findIndex(s => s.song_id === currentPlayingId);
    let prevPoolIdx = curPoolIdx <= 0 ? targetPool.length - 1 : curPoolIdx - 1;
    return loadTrack(targetPool[prevPoolIdx], true);
  }
}

// 音频加载监听：动态嗅探真实音频时长，尝试完整播放
audio.addEventListener('loadedmetadata', () => {
  const dur = audio.duration;
  if (!Number.isFinite(dur) || dur <= 0) return;
  totalTimeEl.textContent = formatTime(dur);
  updateActualAudioBadge(currentTrack(), dur);
  if (pendingSeek && Number(pendingSeek.songId) === Number(currentPlayingId)) {
    const seek = MusicSession.seekPosition(pendingSeek.seconds, dur);
    pendingSeek = null;
    if (seek > 0) {
      try { audio.currentTime = seek; }
      catch (err) { console.warn('Resume seek could not be applied:', err); }
    }
    checkpointPlayback(true);
  }

});

// 进度监听
audio.addEventListener('timeupdate', () => {
  checkpointPlayback();
  const cur = audio.currentTime;
  const dur = audio.duration;
  currentTimeEl.textContent = formatTime(cur);
  if (!isNaN(dur) && dur > 0) {
    totalTimeEl.textContent = formatTime(dur);
    const pct = (cur / dur) * 100;
    progressBar.style.width = `${pct}%`;
    progressThumb.style.left = `${pct}%`;
  }
  // K歌歌词实时高亮与平滑滚动同步
  updateLyricProgress(cur);
});

audio.addEventListener('ended', () => {
  if (currentPlayingId) {
    pendingSeek = null;
    MusicSession.save(localStorage, playingPlaylistKey, currentPlayingId, 0);
  }
  if (playMode === 'single') {
    audio.currentTime = 0;
    playAudio();
  } else {
    nextTrack();
  }
});

progressContainer.addEventListener('click', (e) => {
  const rect = progressContainer.getBoundingClientRect();
  const clickX = e.clientX - rect.left;
  const pct = Math.max(0, Math.min(1, clickX / rect.width));
  if (!isNaN(audio.duration) && audio.duration > 0) {
    audio.currentTime = pct * audio.duration;
    checkpointPlayback(true);
  }
});

audio.addEventListener('pause', () => checkpointPlayback(true));
audio.addEventListener('seeked', () => checkpointPlayback(true));
window.addEventListener('pagehide', () => checkpointPlayback(true));
document.addEventListener('visibilitychange', () => {
  if (document.hidden) checkpointPlayback(true);
});

// 音量控制
volumeSlider.addEventListener('input', (e) => {
  const vol = parseFloat(e.target.value);
  audio.volume = vol;
  updateVolumeIcon(vol);
});

function updateVolumeIcon(vol) {
  if (vol === 0) {
    volumeIcon.innerHTML = '<path fill="currentColor" d="M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51C20.63 14.91 21 13.5 21 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06c1.38-.31 2.63-.95 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z"/>';
  } else if (vol < 0.5) {
    volumeIcon.innerHTML = '<path fill="currentColor" d="M7 9v6h4l5 5V4L11 9H7zm11.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02z"/>';
  } else {
    volumeIcon.innerHTML = '<path fill="currentColor" d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z"/>';
  }
}

let lastVolume = 0.8;
btnVolume.addEventListener('click', () => {
  if (audio.volume > 0) {
    lastVolume = audio.volume;
    audio.volume = 0;
    volumeSlider.value = 0;
    updateVolumeIcon(0);
  } else {
    audio.volume = lastVolume;
    volumeSlider.value = lastVolume;
    updateVolumeIcon(lastVolume);
  }
});

// 主歌单 TAB 切换
tabNavRecommend.addEventListener('click', () => switchPlaylist('recommend'));
tabNavFavorites.addEventListener('click', () => switchPlaylist('favorites'));

// 搜索与过滤
searchInput.addEventListener('input', applyFilter);

tabAll.addEventListener('click', () => {
  filterMode = 'all';
  tabAll.classList.add('active');
  tabFullOnly.classList.remove('active');
  applyFilter();
});

tabFullOnly.addEventListener('click', () => {
  filterMode = 'full';
  tabFullOnly.classList.add('active');
  tabAll.classList.remove('active');
  applyFilter();
});

skipTrialCheckbox.addEventListener('change', (e) => {
  skipTrial = e.target.checked;
  const currentPool = playlists[playingPlaylistKey] || playlists.recommend;
  const curSong = currentPool.find(s => s.song_id === currentPlayingId);
  if (skipTrial && curSong && !curSong.is_full) {
    nextTrack();
  }
});

btnCacheAll.addEventListener('click', precacheCurrentPlaylist);

// ================= K歌实时歌词引擎核心 (LRC解析、高保真渲染、同步滚动、点击跳播) =================
function parseLrc(lrcText, tlyricText = '') {
  if (!lrcText || typeof lrcText !== 'string') return [];

  // 解析翻译歌词字典：按每半秒取整匹配
  const transMap = new Map();
  if (tlyricText && typeof tlyricText === 'string') {
    const tLines = tlyricText.split('\n');
    for (const line of tLines) {
      const match = line.match(/\[(\d{2}):(\d{2})(?:\.(\d{2,3}))?\](.*)/);
      if (match) {
        const min = parseInt(match[1], 10);
        const sec = parseInt(match[2], 10);
        const msStr = match[3] || '0';
        const ms = msStr.length === 2 ? parseInt(msStr, 10) * 10 : parseInt(msStr.padEnd(3, '0').slice(0, 3), 10);
        const time = +(min * 60 + sec + ms / 1000).toFixed(2);
        const text = match[4].trim();
        if (text) {
          transMap.set(Math.round(time * 2), text);
        }
      }
    }
  }

  const lines = lrcText.split('\n');
  const result = [];
  const timeRegex = /\[(\d{2}):(\d{2})(?:\.(\d{2,3}))?\]/g;

  for (const rawLine of lines) {
    const trimmed = rawLine.trim();
    if (!trimmed) continue;

    const matches = [...trimmed.matchAll(timeRegex)];
    if (!matches || matches.length === 0) continue;

    const text = trimmed.replace(timeRegex, '').trim();
    if (!text) continue;

    for (const match of matches) {
      const min = parseInt(match[1], 10);
      const sec = parseInt(match[2], 10);
      const msStr = match[3] || '0';
      const ms = msStr.length === 2 ? parseInt(msStr, 10) * 10 : parseInt(msStr.padEnd(3, '0').slice(0, 3), 10);
      const time = min * 60 + sec + ms / 1000;

      const transKey = Math.round(time * 2);
      const trans = transMap.get(transKey) || transMap.get(transKey - 1) || transMap.get(transKey + 1) || '';

      result.push({
        time: parseFloat(time.toFixed(2)),
        text,
        trans
      });
    }
  }

  result.sort((a, b) => a.time - b.time);
  return result;
}

async function fetchSongLyrics(songId) {
  if (!songId) return [];
  if (lyricsCache.has(songId)) {
    return lyricsCache.get(songId);
  }

  // 1. 尝试网易云官方接口 (支持原词与 tlyric 双语翻译)
  try {
    const neteaseUrl = `https://music.163.com/api/song/lyric?os=pc&id=${songId}&lv=-1&kv=-1&tv=-1`;
    const resp = await fetch(neteaseUrl);
    if (resp.ok) {
      const data = await resp.json();
      const rawLrc = data?.lrc?.lyric || '';
      const rawTrans = data?.tlyric?.lyric || '';
      if (rawLrc) {
        const parsed = parseLrc(rawLrc, rawTrans);
        lyricsCache.set(songId, parsed);
        return parsed;
      }
    }
  } catch (err) {
    console.warn('[Lyrics] NetEase direct fetch failed or CORS restricted, trying Meting backup...', err);
  }

  // 2. 尝试 Meting 跨域兼容接口 (直接返回文本 LRC)
  try {
    const metingUrl = `https://api.injahow.cn/meting/?type=lrc&id=${songId}`;
    const resp = await fetch(metingUrl);
    if (resp.ok) {
      const rawLrc = await resp.text();
      if (rawLrc && rawLrc.includes('[')) {
        const parsed = parseLrc(rawLrc, '');
        lyricsCache.set(songId, parsed);
        return parsed;
      }
    }
  } catch (err) {
    console.warn('[Lyrics] Meting backup fetch failed:', err);
  }

  lyricsCache.set(songId, []);
  return [];
}

async function loadLyricsForSong(songId) {
  currentLyricsSongId = songId;
  currentLyrics = [];
  currentLyricIndex = -1;

  if (lyricsScrollWrap) {
    lyricsScrollWrap.innerHTML = `
      <div class="lyrics-empty-state">
        <div class="lyrics-loading-dots"><span></span><span></span><span></span></div>
        <p class="lyrics-empty-text">正在同步歌词...</p>
      </div>
    `;
  }

  const lyrics = await fetchSongLyrics(songId);
  if (currentLyricsSongId !== songId) return;

  currentLyrics = lyrics;
  renderLyrics(lyrics);

  if (audio && !isNaN(audio.currentTime)) {
    updateLyricProgress(audio.currentTime, true);
  }
}

function renderLyrics(lyrics) {
  if (!lyricsScrollWrap) return;

  if (!lyrics || lyrics.length === 0) {
    lyricsScrollWrap.innerHTML = `
      <div class="lyrics-empty-state">
        <svg viewBox="0 0 24 24" style="width:36px;height:36px;opacity:0.4"><path fill="currentColor" d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"/></svg>
        <p class="lyrics-empty-text">纯音乐，请随旋律享受音乐时光</p>
      </div>
    `;
    return;
  }

  const frag = document.createDocumentFragment();
  lyrics.forEach((item, idx) => {
    const line = document.createElement('div');
    line.className = 'lyric-line';
    line.dataset.index = idx;
    line.dataset.time = item.time;
    line.title = `点击直接跳转至 ${formatTime(item.time)} 跟唱`;

    const origin = document.createElement('div');
    origin.className = 'lyric-origin';
    origin.textContent = item.text;
    line.appendChild(origin);

    if (item.trans) {
      const trans = document.createElement('div');
      trans.className = `lyric-trans ${showTranslation ? '' : 'hidden'}`;
      trans.textContent = item.trans;
      line.appendChild(trans);
    }

    line.addEventListener('click', () => {
      audio.currentTime = item.time;
      checkpointPlayback(true);
      if (audio.paused) {
        playAudio();
      }
      updateActiveLyricLine(idx, true);
      showToast(`已跳转跟唱：${formatTime(item.time)}`, 1000);
    });

    frag.appendChild(line);
  });

  lyricsScrollWrap.innerHTML = '';
  lyricsScrollWrap.appendChild(frag);
}

function updateLyricProgress(currentTime, forceScroll = false) {
  if (!currentLyrics || currentLyrics.length === 0 || !lyricsScrollWrap) return;

  let matchedIdx = -1;
  for (let i = currentLyrics.length - 1; i >= 0; i--) {
    if (currentTime >= currentLyrics[i].time - 0.15) {
      matchedIdx = i;
      break;
    }
  }

  if (matchedIdx !== currentLyricIndex || forceScroll) {
    currentLyricIndex = matchedIdx;
    updateActiveLyricLine(matchedIdx, forceScroll);
  }
}

function updateActiveLyricLine(targetIdx, needScroll = false) {
  if (!lyricsScrollWrap) return;
  const lines = lyricsScrollWrap.querySelectorAll('.lyric-line');
  lines.forEach((el, idx) => {
    if (idx === targetIdx) {
      el.classList.add('active');
    } else {
      el.classList.remove('active');
    }
  });

  if (targetIdx >= 0 && lines[targetIdx] && (needScroll || !isUserScrollingLyrics)) {
    scrollLyricToCenter(lines[targetIdx]);
  }
}

function scrollLyricToCenter(targetLineEl) {
  if (!lyricsViewport || !targetLineEl) return;
  const viewportHeight = lyricsViewport.clientHeight;
  const targetOffsetTop = targetLineEl.offsetTop;
  const targetHeight = targetLineEl.clientHeight;
  const scrollTo = targetOffsetTop - (viewportHeight * 0.42) + (targetHeight / 2);

  lyricsViewport.scrollTo({
    top: Math.max(0, scrollTo),
    behavior: 'smooth'
  });
}

function setLyricFontSize(size) {
  lyricFontSize = Math.min(24, Math.max(13, size));
  document.documentElement.style.setProperty('--lyric-font-size', `${lyricFontSize}px`);
  localStorage.setItem('yanzi_music_lyric_font_size', String(lyricFontSize));
}

// 歌词辅助工具交互初始化
if (lyricsViewport) {
  const onUserScroll = () => {
    isUserScrollingLyrics = true;
    clearTimeout(userScrollTimer);
    userScrollTimer = setTimeout(() => {
      isUserScrollingLyrics = false;
    }, 2800);
  };
  lyricsViewport.addEventListener('wheel', onUserScroll, { passive: true });
  lyricsViewport.addEventListener('touchstart', onUserScroll, { passive: true });
}

if (btnToggleTrans) {
  btnToggleTrans.addEventListener('click', () => {
    showTranslation = !showTranslation;
    btnToggleTrans.classList.toggle('active', showTranslation);
    document.querySelectorAll('.lyric-trans').forEach(el => {
      el.classList.toggle('hidden', !showTranslation);
    });
    showToast(showTranslation ? '已开启双语翻译' : '已关闭双语翻译', 1200);
  });
}

if (btnLyricsFontUp) {
  btnLyricsFontUp.addEventListener('click', () => {
    setLyricFontSize(lyricFontSize + 2);
    showToast(`歌词字号：${lyricFontSize}px`, 1000);
  });
}

if (btnLyricsFontDown) {
  btnLyricsFontDown.addEventListener('click', () => {
    setLyricFontSize(lyricFontSize - 2);
    showToast(`歌词字号：${lyricFontSize}px`, 1000);
  });
}

// 应用存储的字号
setLyricFontSize(lyricFontSize);

// 沉浸黑胶大视图弹窗交互
barCoverWrap.addEventListener('click', () => {
  vinylModal.classList.add('active');
  if (audio && !isNaN(audio.currentTime)) {
    updateLyricProgress(audio.currentTime, true);
  }
});

modalBackdrop.addEventListener('click', () => {
  vinylModal.classList.remove('active');
});

btnModalClose.addEventListener('click', () => {
  vinylModal.classList.remove('active');
});

// 绑定操作按键
btnPlay.addEventListener('click', togglePlay);
btnPrev.addEventListener('click', prevTrack);
btnNext.addEventListener('click', nextTrack);
btnMode.addEventListener('click', cyclePlayMode);

// 歌单 Hero 操作栏按钮绑定
if (btnPlayAll) btnPlayAll.addEventListener('click', () => playAllTracks(false));
if (btnShufflePlay) btnShufflePlay.addEventListener('click', () => playAllTracks(true));
if (btnModeCapsule) btnModeCapsule.addEventListener('click', cyclePlayMode);

// 播放队列抽屉事件绑定
if (btnQueueToggle) btnQueueToggle.addEventListener('click', () => toggleQueueDrawer());
if (btnQueueClose) btnQueueClose.addEventListener('click', () => toggleQueueDrawer(false));
if (drawerBackdrop) drawerBackdrop.addEventListener('click', () => toggleQueueDrawer(false));
if (btnLocatePlaying) btnLocatePlaying.addEventListener('click', locateCurrentSongInMainList);

// 黑胶内部大号快捷按钮
if (modalBtnPrev) modalBtnPrev.addEventListener('click', prevTrack);
if (modalBtnPlay) modalBtnPlay.addEventListener('click', togglePlay);
if (modalBtnNext) modalBtnNext.addEventListener('click', nextTrack);

// 系统级 MediaSession (支持全局多媒体按键、任务栏/锁屏音乐控制)
function setupMediaSession(song) {
  if (!('mediaSession' in navigator)) return;
  try {
    navigator.mediaSession.metadata = new MediaMetadata({
      title: song.name,
      artist: song.artists,
      album: song.album || '网易云音乐',
      artwork: [
        { src: song.cover_url || DEFAULT_COVER, sizes: '128x128', type: 'image/jpeg' },
        { src: song.cover_url || DEFAULT_COVER, sizes: '512x512', type: 'image/jpeg' }
      ]
    });
  } catch (err) {
    console.warn('MediaSession metadata error:', err);
  }
}

if ('mediaSession' in navigator) {
  try {
    navigator.mediaSession.setActionHandler('play', () => playAudio());
    navigator.mediaSession.setActionHandler('pause', () => pauseAudio());
    navigator.mediaSession.setActionHandler('previoustrack', () => prevTrack());
    navigator.mediaSession.setActionHandler('nexttrack', () => nextTrack());
    navigator.mediaSession.setActionHandler('seekto', (details) => {
      if (details.seekTime && audio.duration) {
        audio.currentTime = details.seekTime;
      }
    });
  } catch (err) {
    console.warn('MediaSession action handler error:', err);
  }
}

// 本地 IPC 控制指令监听与外部通信通道 (支持轮盘菜单/脚本非侵入式后台控制)
let lastHandledCommandTime = 0;

async function checkStartupCommand() {
  try {
    const res = await fetch(`control.json?t=${Date.now()}`, { cache: 'no-store' });
    if (res.ok) {
      const cmd = await res.json();
      if (cmd && cmd.time) {
        lastHandledCommandTime = cmd.time;
        // 如果指令时间在最近 15 秒内，说明是刚刚外部触发拉起小程序的
        const isFresh = (Date.now() - cmd.time) < 15000;
        if (isFresh) {
          console.log('[IPC Control] Executing startup pending command:', cmd.action);
          if (cmd.action === 'next') {
            nextTrack();
          } else if (cmd.action === 'prev') {
            prevTrack();
          } else if (cmd.action === 'toggle' || cmd.action === 'play') {
            playAudio();
          }
        }
      }
    }
  } catch (_) {}
}

async function pollControlCommand() {
  try {
    const res = await fetch(`control.json?t=${Date.now()}`, { cache: 'no-store' });
    if (res.ok) {
      const cmd = await res.json();
      if (cmd && cmd.time && cmd.time > lastHandledCommandTime) {
        lastHandledCommandTime = cmd.time;
        console.log('[IPC Control] Executing real-time command:', cmd.action);
        if (cmd.action === 'next') {
          nextTrack();
        } else if (cmd.action === 'prev') {
          prevTrack();
        } else if (cmd.action === 'toggle') {
          togglePlay();
        } else if (cmd.action === 'play') {
          playAudio();
        } else if (cmd.action === 'pause') {
          pauseAudio();
        } else if (cmd.action === 'shuffle') {
          playAllTracks(true);
        } else if (cmd.action === 'playall') {
          playAllTracks(false);
        } else if (cmd.action === 'mode') {
          cyclePlayMode();
        } else if (cmd.action === 'sync') {
          await loadPlaylists();
        }
      }
    }
  } catch (_) {}
}

// 每 250ms 极速检查本地指令管道
setInterval(pollControlCommand, 250);

// 跨窗口/跨标签 BroadcastChannel 辅助控制通道
try {
  const controlChannel = new BroadcastChannel('yanzi_cloudmusic_control');
  controlChannel.onmessage = (e) => {
    const cmd = e.data?.action;
    if (cmd === 'next') nextTrack();
    else if (cmd === 'prev') prevTrack();
    else if (cmd === 'toggle') togglePlay();
    else if (cmd === 'play') playAudio();
    else if (cmd === 'pause') pauseAudio();
    else if (cmd === 'shuffle') playAllTracks(true);
    else if (cmd === 'playall') playAllTracks(false);
    else if (cmd === 'mode') cyclePlayMode();
  };
} catch (_) {}

function toggleMute() {
  if (audio.volume > 0) {
    lastVolume = audio.volume;
    audio.volume = 0;
    volumeSlider.value = 0;
    updateVolumeIcon(0);
    showToast('已静音 (快捷键 M)', 1200);
  } else {
    audio.volume = lastVolume || 0.8;
    volumeSlider.value = audio.volume;
    updateVolumeIcon(audio.volume);
    showToast(`恢复音量: ${Math.round(audio.volume * 100)}%`, 1200);
  }
}

// 全局桌面级快捷键与轮盘模拟按键 (支持 Ctrl+Alt 组合及单键)
window.addEventListener('keydown', (e) => {
  if (document.activeElement === searchInput) return;

  // 1. 多媒体硬件键 / 虚拟键直接响应
  if (e.code === 'MediaPlayPause' || e.key === 'MediaPlayPause') {
    e.preventDefault();
    togglePlay();
    return;
  }
  if (e.code === 'MediaTrackNext' || e.key === 'MediaTrackNext') {
    e.preventDefault();
    nextTrack();
    return;
  }
  if (e.code === 'MediaTrackPrevious' || e.key === 'MediaTrackPrevious') {
    e.preventDefault();
    prevTrack();
    return;
  }

  // 2. 轮盘模拟常用快捷键: Ctrl+Alt+Right (下一首), Ctrl+Alt+Left (上一首), Ctrl+Alt+Space (播放/暂停)
  if (e.ctrlKey && e.altKey) {
    if (e.code === 'ArrowRight' || e.key === 'ArrowRight') {
      e.preventDefault();
      nextTrack();
      return;
    } else if (e.code === 'ArrowLeft' || e.key === 'ArrowLeft') {
      e.preventDefault();
      prevTrack();
      return;
    } else if (e.code === 'Space' || e.key === ' ' || e.keyCode === 32) {
      e.preventDefault();
      togglePlay();
      return;
    }
  }

  // 3. 增强型快捷键操作
  if (e.shiftKey && e.code === 'Enter') {
    e.preventDefault();
    playAllTracks(false);
  } else if ((e.code === 'KeyP' || e.key === 'p' || e.key === 'P') && !e.ctrlKey && !e.altKey) {
    cyclePlayMode();
  } else if ((e.code === 'KeyL' || e.key === 'l' || e.key === 'L') && !e.ctrlKey && !e.altKey) {
    toggleQueueDrawer();
  } else if ((e.code === 'KeyM' || e.key === 'm' || e.key === 'M') && !e.ctrlKey && !e.altKey) {
    toggleMute();
  } else if (e.code === 'Space' && !e.ctrlKey && !e.altKey && !e.shiftKey) {
    e.preventDefault();
    togglePlay();
  } else if (e.code === 'ArrowRight' && !e.ctrlKey && !e.altKey) {
    nextTrack();
  } else if (e.code === 'ArrowLeft' && !e.ctrlKey && !e.altKey) {
    prevTrack();
  } else if (e.code === 'ArrowUp') {
    audio.volume = Math.min(1, audio.volume + 0.05);
    volumeSlider.value = audio.volume;
    updateVolumeIcon(audio.volume);
  } else if (e.code === 'ArrowDown') {
    audio.volume = Math.max(0, audio.volume - 0.05);
    volumeSlider.value = audio.volume;
    updateVolumeIcon(audio.volume);
  } else if (e.code === 'Escape') {
    vinylModal.classList.remove('active');
    toggleQueueDrawer(false);
  }
});

// --- 收录监听开关与一键收录逻辑 ---
function initSyncSwitch() {
  if (!syncSwitch) return;

  // 默认记录状态 (初始为关闭状态，需要收录时用户开启，避免浪费性能)
  const savedState = localStorage.getItem('yanzi_sync_listen') === 'true';
  syncSwitch.checked = savedState;
  updateSyncSwitchUI(savedState);

  syncSwitch.addEventListener('change', () => {
    const isChecked = syncSwitch.checked;
    localStorage.setItem('yanzi_sync_listen', String(isChecked));
    updateSyncSwitchUI(isChecked);

    if (isChecked) {
      showToast('已开启客户端缓存实时收录监听');
    } else {
      showToast('已关闭收录监听，专注于纯净播放');
    }
  });

  if (btnManualSync) {
    btnManualSync.addEventListener('click', async () => {
      const icon = btnManualSync.querySelector('.icon-sync-spin');
      if (icon) icon.classList.add('rotating');
      showToast('正在扫描收录客户端最新缓存...');

      try {
        await loadPlaylists();
        const localCount = (playlists.recommend.concat(playlists.favorites)).filter(s => s.has_local).length;
        showToast(`收录完成！当前已沉淀 ${localCount} 首完整母带`);
      } catch (_) {
        showToast('收录刷新完成');
      } finally {
        setTimeout(() => {
          if (icon) icon.classList.remove('rotating');
        }, 600);
      }
    });
  }
}

function updateSyncSwitchUI(active) {
  if (syncLabelText) {
    syncLabelText.textContent = active ? '收录监听: 开' : '收录监听: 关';
  }
}

initSyncSwitch();

const yanziMusicReady = loadPlaylists().then(async () => {
  await checkStartupCommand();
  return true;
});

// The host invokes this directly with an authenticated, user-gesture-backed
// WebView2 CDP call. It returns the actual HTMLAudioElement state, never an
// optimistic "command sent" acknowledgment.
window.yanziExtensionControl = async function (action) {
  try {
    await yanziMusicReady;
    let succeeded = false;
    switch (action) {
      case 'toggle': succeeded = await togglePlay(); break;
      case 'play': succeeded = await playAudio(); break;
      case 'pause': pauseAudio(); succeeded = true; break;
      case 'status': succeeded = true; break;
      case 'favorites':
      case 'recommend':
        await switchPlaylist(action);
        succeeded = true;
        break;
      case 'next': succeeded = await nextTrack() === true; break;
      case 'prev': succeeded = await prevTrack() === true; break;
      default: throw new Error('未知控制指令：' + action);
    }
    const playing = !audio.paused && !audio.error && !!audio.getAttribute('src');
    return {
      ok: !!succeeded && (['status','favorites','recommend'].includes(action) ? true : (action === 'pause' ? !playing : (action === 'toggle' ? true : playing))),
      playing,
      track: songTitle.textContent || '',
      songId: Number(currentPlayingId) || 0,
      playlistKey: playingPlaylistKey,
      viewedPlaylistKey: currentPlaylistKey,
      position: pendingSeek ? Number(pendingSeek.seconds) : Number(audio.currentTime || 0),
      positionPending: !!pendingSeek,
      expectedDuration: MusicSession.parseDuration(currentTrack()?.duration),
      previewOnly: !!currentTrack()?.preview_only,
      audioState: {
        paused: audio.paused, readyState: audio.readyState, networkState: audio.networkState,
        errorCode: audio.error?.code || 0, errorMessage: audio.error?.message || '',
        source: audio.currentSrc || audio.src || '',
        bufferedRanges: audio.buffered?.length || 0,
        playbackLastError
      },
      error: succeeded ? '' : (playbackLastError || audio.error?.message || '播放没有成功：检查音频文件、WebView 自动播放权限或播放列表')
    };
  } catch (err) {
    return { ok: false, playing: !audio.paused, error: err?.message || String(err) };
  }
};
// Compatibility with a locally installed previous host.
window.yanziCloudMusicControl = window.yanziExtensionControl;
