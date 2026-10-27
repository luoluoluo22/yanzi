/* Yanzi ChatGPT Keep Going (自动续聊) 兼容入口
   实际代码已迁移至 webapps/chatgpt-keep-going/content.js
*/
(() => {
  'use strict';
  if (globalThis.__yanziKeepGoingLoaded) return;
  // 直接复用 webapps 模块的加载防护
  try {
    const script = document.createElement('script');
    script.src = chrome.runtime.getURL('webapps/chatgpt-keep-going/content.js');
    (document.head || document.documentElement).appendChild(script);
    script.remove();
  } catch {}
})();
