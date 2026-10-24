export const siteAdapters = {
  chatgpt: {
    id: 'chatgpt', name: 'ChatGPT', icon: '✳', category: 'AI 对话',
    description: '临时对话、提示词填入、发送确认与最新回答读取。',
    homeUrl: 'https://chatgpt.com/?temporary-chat=true',
    allowedHosts: ['chatgpt.com', 'www.chatgpt.com'],
    resultSelectors: [],
    actions: [
      { id: 'open', label: '打开临时聊天', description: '创建或切换到 ChatGPT 临时聊天。' },
      { id: 'new_chat', label: '新建临时对话', description: '在当前会话打开一个新的临时聊天。' },
      { id: 'fill_prompt', label: '填入提示词', description: '只填写输入框，不发送消息。', input: 'text' },
      { id: 'send_prompt', label: '发送提示词', description: '发送前会要求二次确认。', input: 'text', requiresConfirm: true },
      { id: 'read_response', label: '读取最新回答', description: '读取当前页面最后一条助手回答。' },
      { id: 'read_page', label: '读取页面', description: '读取页面标题、正文摘要和语义快照。' },
      { id: 'reload', label: '刷新页面', description: '刷新当前 ChatGPT 页面。' },
      { id: 'close_session', label: '关闭会话', description: '关闭当前网站的独立浏览器会话。' }
    ]
  },
  xiaohongshu: {
    id: 'xiaohongshu', name: '小红书', icon: '红', category: '内容社区',
    description: '搜索笔记、提取可见结果、打开笔记并读取页面。',
    homeUrl: 'https://www.xiaohongshu.com/',
    allowedHosts: ['xiaohongshu.com', 'www.xiaohongshu.com'],
    searchUrl: query => 'https://www.xiaohongshu.com/search_result?keyword=' + encodeURIComponent(query),
    resultSelectors: ['a[href*="/explore/"]', 'a[href*="/discovery/item/"]', 'a[href*="/note/"]'],
    titleSelectors: ['.note-item .title', '.note-title', '.title', '[class*="title"]'],
    detailSelectors: {
      title: ['h1', '.note-title', '.note-item .title', '[class*="title"]'],
      description: ['.note-content', '.note-desc', '.desc', '[class*="desc"]'],
      author: ['.author-wrapper .username', '.user-info .name', '[class*="author"] [class*="name"]'],
      publishedAt: ['time[datetime]', '[class*="publish-time"]', '[class*="publishTime"]'],
      tags: ['a[href*="tag"]', '.tag', '[class*="tag"]'],
      engagement: {
        likes: ['[class*="like"] [class*="count"]', '[class*="like-count"]', '[aria-label*="赞"]'],
        comments: ['[class*="comment"] [class*="count"]', '[class*="comment-count"]'],
        saves: ['[class*="collect"] [class*="count"]', '[class*="collect-count"]'],
        shares: ['[class*="share"] [class*="count"]', '[class*="share-count"]']
      }
    },
    actions: [
      { id: 'open', label: '打开首页', description: '打开小红书首页。' },
      { id: 'search', label: '搜索笔记', description: '按关键词打开搜索结果页。', input: 'query' },
      { id: 'read_page', label: '读取页面', description: '读取当前页面正文和语义快照。' },
      { id: 'extract_results', label: '提取笔记结果', description: '提取当前页可见笔记标题、链接和摘要。' },
      { id: 'extract_details', label: '提取笔记详情', description: '结构化读取当前笔记标题、正文、作者、时间、互动指标、标签和可见媒体地址。' },
      { id: 'open_result', label: '打开指定结果', description: '打开提取结果中的指定序号。', input: 'index' },
      { id: 'reload', label: '刷新页面', description: '刷新当前小红书页面。' },
      { id: 'close_session', label: '关闭会话', description: '关闭当前网站的独立浏览器会话。' }
    ]
  },
  douyin: {
    id: 'douyin', name: '抖音', icon: '音', category: '短视频',
    description: '搜索视频、提取可见视频卡片、打开结果并读取页面。',
    homeUrl: 'https://www.douyin.com/',
    allowedHosts: ['douyin.com', 'www.douyin.com'],
    searchUrl: query => 'https://www.douyin.com/search/' + encodeURIComponent(query),
    resultSelectors: ['div.search-result-card'],
    titleSelectors: ['[data-e2e=video-card-title]', '[class*="video-card-title"]', '.title', '[class*="title"]'],
    detailSelectors: {
      title: ['h1', '[data-e2e="video-title"]', '[class*="video-title"]'],
      description: ['[data-e2e="video-desc"]', '[class*="video-desc"]', '[class*="desc"]'],
      author: ['[data-e2e="video-author-name"]', '[class*="author-name"]', '[class*="user-name"]'],
      publishedAt: ['time[datetime]', '[class*="publish-time"]', '[class*="publishTime"]'],
      tags: ['a[href*="challenge"]', 'a[href*="search"]', '[class*="tag"]'],
      engagement: {
        likes: ['[data-e2e="like-count"]', '[class*="like-count"]'],
        comments: ['[data-e2e="comment-count"]', '[class*="comment-count"]'],
        saves: ['[data-e2e="collect-count"]', '[class*="collect-count"]'],
        shares: ['[data-e2e="share-count"]', '[class*="share-count"]']
      }
    },
    actions: [
      { id: 'open', label: '打开首页', description: '打开抖音首页。' },
      { id: 'search', label: '搜索视频', description: '按关键词打开抖音搜索结果。', input: 'query' },
      { id: 'read_page', label: '读取页面', description: '读取当前页面正文和语义快照。' },
      { id: 'extract_results', label: '提取视频结果', description: '提取当前页可见视频标题、链接和摘要。' },
      { id: 'extract_details', label: '提取视频详情', description: '结构化读取当前视频标题、描述、作者、时间、互动指标、标签和可见媒体地址。' },
      { id: 'open_result', label: '打开指定结果', description: '打开提取结果中的指定序号。', input: 'index' },
      { id: 'reload', label: '刷新页面', description: '刷新当前抖音页面。' },
      { id: 'close_session', label: '关闭会话', description: '关闭当前网站的独立浏览器会话。' }
    ]
  },
  bilibili: {
    id: 'bilibili', name: '哔哩哔哩', icon: 'B', category: '视频社区',
    description: '搜索视频、提取视频卡片、打开视频并读取页面。',
    homeUrl: 'https://www.bilibili.com/',
    allowedHosts: ['bilibili.com', 'www.bilibili.com', 'search.bilibili.com', 'm.bilibili.com'],
    searchUrl: query => 'https://search.bilibili.com/all?keyword=' + encodeURIComponent(query),
    resultSelectors: ['a[href*="/video/"]', 'a[href*="/bangumi/play/"]'],
    titleSelectors: ['.bili-video-card__info--tit', '.bili-video-card__info--tit a', '.video-name', '.title', '[class*="title"]'],
    detailSelectors: {
      title: ['h1', '.video-title', '[class*="video-title"]'],
      description: ['.desc-info-text', '.video-desc', '[class*="desc-info"]'],
      author: ['.up-name', '.username', '[class*="up-name"]'],
      publishedAt: ['time[datetime]', '.pubdate', '[class*="pubdate"]'],
      tags: ['a[href*="tag"]', '.tag-link', '[class*="tag"]'],
      engagement: {
        views: ['.view-text', '.view', '[class*="view-count"]'],
        likes: ['.video-like', '[title*="点赞"]', '[class*="like-count"]'],
        comments: ['.reply', '[class*="reply-count"]'],
        coins: ['[title*="投币"]', '[class*="coin"]'],
        favorites: ['[title*="收藏"]', '[class*="favorite"]'],
        danmaku: ['.danmaku', '[class*="danmaku"]']
      }
    },
    actions: [
      { id: 'open', label: '打开首页', description: '打开哔哩哔哩首页。' },
      { id: 'search', label: '搜索视频', description: '按关键词打开 B 站搜索结果。', input: 'query' },
      { id: 'read_page', label: '读取页面', description: '读取当前页面正文和语义快照。' },
      { id: 'extract_results', label: '提取视频结果', description: '提取当前页可见视频标题、链接和摘要。' },
      { id: 'extract_details', label: '提取视频详情', description: '结构化读取当前视频标题、描述、作者、时间、互动指标、标签和可见媒体地址。' },
      { id: 'open_result', label: '打开指定结果', description: '打开提取结果中的指定序号。', input: 'index' },
      { id: 'reload', label: '刷新页面', description: '刷新当前哔哩哔哩页面。' },
      { id: 'close_session', label: '关闭会话', description: '关闭当前网站的独立浏览器会话。' }
    ]
  }
};
