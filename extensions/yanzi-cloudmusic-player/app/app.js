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

// 沉浸黑胶大视图
const vinylModal = document.getElementById('vinylModal');
const modalBackdrop = document.getElementById('modalBackdrop');
const btnModalClose = document.getElementById('btnModalClose');
const vinylDisc = document.getElementById('vinylDisc');
const modalCoverImg = document.getElementById('modalCoverImg');
const modalSongTitle = document.getElementById('modalSongTitle');
const modalSongArtist = document.getElementById('modalSongArtist');
const modalSongAlbum = document.getElementById('modalSongAlbum');
const modalBadgeContainer = document.getElementById('modalBadgeContainer');

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
      req.onsuccess = () => resolve(req.result ? req.result.blob : null);
      req.onerror = () => resolve(null);
    });
  } catch (err) {
    console.warn('IndexedDB get error:', err);
    return null;
  }
}

async function saveCachedAudio(songId, blob) {
  try {
    const db = await openDB();
    return new Promise((resolve) => {
      const tx = db.transaction(STORE_NAME, 'readwrite');
      const store = tx.objectStore(STORE_NAME);
      store.put({
        song_id: songId,
        blob: blob,
        size: blob.size,
        saved_at: Date.now()
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
    const req = store.getAllKeys();
    req.onsuccess = () => {
      cachedSongIds.clear();
      (req.result || []).forEach(id => cachedSongIds.add(id));
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

  // 1. 记忆恢复：恢复上次打开的标签页 (避免刷新总是跳回“每日推荐”)
  const savedTab = localStorage.getItem('yanzi_music_tab') || 'recommend';
  switchPlaylist(savedTab, false);

  // 2. 记忆恢复：恢复上次停留在的歌曲 (无需从头开始)
  const savedSongId = Number(localStorage.getItem('yanzi_music_song_id'));
  const savedPlaylistKey = localStorage.getItem('yanzi_music_playlist_key') || savedTab;
  const targetPool = playlists[savedPlaylistKey] || playlists[savedTab] || playlists.recommend;

  let targetSong = targetPool.find(s => s.song_id === savedSongId);
  if (!targetSong && targetPool.length > 0) {
    targetSong = targetPool[0];
  }

  if (targetSong) {
    await loadTrack(targetSong, false);
    // 列表视口平滑滚动定位到这首歌所在的行
    setTimeout(() => {
      const row = document.querySelector(`.song-row[data-song-id="${targetSong.song_id}"]`);
      if (row) {
        row.scrollIntoView({ block: 'center', behavior: 'smooth' });
      }
    }, 250);
  }
}

// 切换当前浏览的歌单 (持久化记录当前 Tab)
function switchPlaylist(key, resetSearch = true) {
  currentPlaylistKey = key;
  localStorage.setItem('yanzi_music_tab', key);

  if (key === 'recommend') {
    tabNavRecommend.classList.add('active');
    tabNavFavorites.classList.remove('active');
  } else {
    tabNavFavorites.classList.add('active');
    tabNavRecommend.classList.remove('active');
  }

  if (resetSearch) {
    searchInput.value = '';
  }
  applyFilter();
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
      <td class="td-num">${item.index}</td>
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
      loadTrack(item, true);
    });

    songListBody.appendChild(tr);
  });
}

// 核心播放：优先从本地 IndexedDB 缓存读取，未缓存时发起异步后台缓存
async function loadTrack(song, autoPlay = true) {
  if (!song) return;

  // 若开启了“自动跳过试听”，且当前选中的是试听歌曲，自动找下一首完整版
  if (skipTrial && !song.is_full) {
    const nextSong = findNextFullSong(song);
    if (nextSong && nextSong.song_id !== song.song_id) {
      song = nextSong;
    }
  }

  currentPlayingId = song.song_id;
  localStorage.setItem('yanzi_music_song_id', String(song.song_id));
  localStorage.setItem('yanzi_music_playlist_key', playingPlaylistKey);

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

  // 1. 如果是本地已有物理音频文件 (开头为 ../../)
  if (song.audio_url && song.audio_url.startsWith('../../')) {
    audio.src = song.audio_url;
    cachedSongIds.add(song.song_id);
    updateCacheCount();
    updateRowCacheStatus(song.song_id, true);
  } else {
    // 2. 尝试从本地 IndexedDB 缓存读取
    const cachedBlob = await getCachedAudio(song.song_id);
    const isFromCache = !!cachedBlob;

    if (isFromCache) {
      currentBlobUrl = URL.createObjectURL(cachedBlob);
      audio.src = currentBlobUrl;
      cachedSongIds.add(song.song_id);
      updateCacheCount();
      updateRowCacheStatus(song.song_id, true);
    } else {
      // 3. 从网络加载并自动在后台缓存
      audio.src = song.audio_url;
      fetchAndCacheAudio(song.song_id, song.audio_url);
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

  if (autoPlay) {
    return await playAudio();
  }
  pauseAudio();
  return true;
}

// 后台异步缓存音频数据
async function fetchAndCacheAudio(songId, url) {
  try {
    const resp = await fetch(url);
    if (resp.ok) {
      const blob = await resp.blob();
      if (blob.size > 1024) {
        await saveCachedAudio(songId, blob);
      }
    }
  } catch (err) {
    console.warn(`[AutoCache] Failed to cache song ${songId}:`, err);
  }
}

// 一键预缓存当前歌单
async function precacheCurrentPlaylist() {
  btnCacheAll.disabled = true;
  const originalSpan = btnCacheAll.querySelector('span');

  const listSource = getCurrentListSource();
  let doneCount = 0;
  for (const s of listSource) {
    if (!cachedSongIds.has(s.song_id)) {
      if (originalSpan) originalSpan.textContent = `缓存 (${doneCount + 1}/${listSource.length})`;
      await fetchAndCacheAudio(s.song_id, s.audio_url);
      doneCount++;
    }
  }

  btnCacheAll.disabled = false;
  if (originalSpan) originalSpan.textContent = `全部已缓存`;
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

async function playAudio() {
  try {
    if (!audio.getAttribute('src')) {
      const pool = playlists[playingPlaylistKey] || playlists.recommend;
      if (!pool.length) throw new Error('歌单为空，没有可播放的歌曲');
      await loadTrack(pool[0], false);
    }
    // On a freshly created hidden WebView, file:// media preloading can be slow.
    // Keep the user-gesture playback request alive; clear the timer on success.
    // If it really times out, cancel the pending play so it cannot start later.
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
    vinylDisc.classList.add('spinning');
    playIcon.innerHTML = '<path fill="currentColor" d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/>';
    btnPlay.title = '暂停';
    return true;
  } catch (err) {
    audio.pause(); // Abort a still-pending play(), preventing ghost playback.
    isPlaying = false;
    playbackLastError = err?.message || String(err);
    console.error('[MusicPlayback] Failed:', err);
    showToast('播放失败：' + (err?.message || String(err)), 3500);
    return false;
  }
}

function pauseAudio() {
  audio.pause();
  isPlaying = false;
  vinylDisc.classList.remove('spinning');
  playIcon.innerHTML = '<path fill="currentColor" d="M8 5v14l11-7z"/>';
  btnPlay.title = '播放';
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
    let nextItem = targetPool[Math.floor(Math.random() * targetPool.length)];
    return loadTrack(nextItem, true);
  } else {
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

  let curPoolIdx = targetPool.findIndex(s => s.song_id === currentPlayingId);
  let prevPoolIdx = curPoolIdx <= 0 ? targetPool.length - 1 : curPoolIdx - 1;
  return loadTrack(targetPool[prevPoolIdx], true);
}

function toggleMode() {
  if (playMode === 'list') {
    playMode = 'random';
    btnMode.title = '播放模式: 随机播放';
    modeIcon.innerHTML = '<path fill="currentColor" d="M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z"/>';
  } else if (playMode === 'random') {
    playMode = 'single';
    btnMode.title = '播放模式: 单曲循环';
    modeIcon.innerHTML = '<path fill="currentColor" d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4zm-4-2V9h-1l-2 1v1h1.5v4H13z"/>';
  } else {
    playMode = 'list';
    btnMode.title = '播放模式: 列表循环';
    modeIcon.innerHTML = '<path fill="currentColor" d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z"/>';
  }
}

// 音频加载监听：动态嗅探真实音频时长，尝试完整播放
audio.addEventListener('loadedmetadata', () => {
  const dur = audio.duration;
  if (!isNaN(dur) && dur > 0) {
    totalTimeEl.textContent = formatTime(dur);

    // 动态嗅探：只要实际拿到的音频超过 80 秒，证明拿到的是完整全曲！
    if (dur >= 80) {
      const curPool = playlists[playingPlaylistKey] || playlists.recommend;
      const curSong = curPool.find(s => s.song_id === currentPlayingId);
      if (curSong) {
        curSong.is_full = true;
        // 动态提升底栏徽章为完整播放
        songBadge.className = 'bar-badge full';
        songBadge.textContent = curSong.has_local ? '本地原声' : '完整播放';
        songBadge.title = `音频已成功加载完整版 (时长 ${formatTime(dur)})`;

        // 动态移除列表上的 VIP 限制标签
        const row = document.querySelector(`.song-row[data-song-id="${curSong.song_id}"]`);
        if (row) {
          const vipTag = row.querySelector('.tag-vip-trial');
          if (vipTag) vipTag.remove();
        }
      }
    }
  }
});

// 进度监听
audio.addEventListener('timeupdate', () => {
  const cur = audio.currentTime;
  const dur = audio.duration;
  currentTimeEl.textContent = formatTime(cur);
  if (!isNaN(dur) && dur > 0) {
    totalTimeEl.textContent = formatTime(dur);
    const pct = (cur / dur) * 100;
    progressBar.style.width = `${pct}%`;
    progressThumb.style.left = `${pct}%`;
  }
});

audio.addEventListener('ended', () => {
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
  }
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

// 沉浸黑胶大视图弹窗交互
barCoverWrap.addEventListener('click', () => {
  vinylModal.classList.add('active');
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
btnMode.addEventListener('click', toggleMode);

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
  };
} catch (_) {}

// 全局快捷键与轮盘模拟按键 (支持 Ctrl+Alt+Right / Ctrl+Alt+Left / Ctrl+Alt+Space 等)
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

  // 默认单键
  if (e.code === 'Space' && !e.ctrlKey && !e.altKey && !e.shiftKey) {
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
      case 'next': succeeded = await nextTrack() === true; break;
      case 'prev': succeeded = await prevTrack() === true; break;
      default: throw new Error('未知控制指令：' + action);
    }
    const playing = !audio.paused && !audio.error && !!audio.getAttribute('src');
    return {
      ok: !!succeeded && (action === 'status' ? true : (action === 'pause' ? !playing : (action === 'toggle' ? true : playing))),
      playing,
      track: songTitle.textContent || '',
      position: Number(audio.currentTime || 0),
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
// Private extension migration shim for Yanzi 1.0.26 rollback.
window.yanziCloudMusicControl = window.yanziExtensionControl;
