import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseConversationUrl, createOriginBinding, validateFeedbackRequest, makeFeedbackTask } from '../origin-routing.mjs';

const cid = 'af123456-bb22-cc33-dd44-eeeeeeeeeeee';
const url = 'https://chatgpt.com/c/' + cid;

test('conversation URL normalization and strict host matching', () => {
  assert.deepEqual(parseConversationUrl(url + '?utm=1'), {url,conversationId:cid});
  assert.equal(parseConversationUrl('https://chatgpt.com/'),null);
  assert.equal(parseConversationUrl('https://evil-chatgpt.com/c/' + cid),null);
  assert.equal(parseConversationUrl('http://chatgpt.com/c/'+cid),null);
  assert.equal(parseConversationUrl('https://chatgpt.com.evil.test/c/'+cid),null);
  assert.equal(parseConversationUrl(url+'/messages'),null);
});

test('origin only from a verified non-temporary page; no blind tab selection', () => {
  assert.throws(()=>createOriginBinding({url,tabId:0}),/identity/);
  assert.throws(()=>createOriginBinding({url,tabId:12,conversationId:'wrong'}),/identity/);
  assert.throws(()=>createOriginBinding({url,tabId:12,temporary:true}),/temporary/);
  const o=createOriginBinding({url,tabId:12,conversationId:cid},1234);
  assert.equal(o.boundAt,1234);
  assert.equal(o.url,url);
  assert.equal(o.tabId,12);
  assert.equal(makeFeedbackTask(o,'完成').expectedUrl,url);
  assert.equal(makeFeedbackTask(o,'完成').action,'chatgpt_feedback_send');
});

test('delivery requires explicit origin ID and idempotency key',()=>{
  const id=createOriginBinding({url,tabId:1}).id;
  assert.deepEqual(validateFeedbackRequest({originId:id,deliveryKey:'run:001',text:'  已验收  '}),{
    originId:id,deliveryKey:'run:001',text:'已验收'
  });
  assert.throws(()=>validateFeedbackRequest({originId:id,text:'hello'}),/delivery_key/);
  assert.throws(()=>validateFeedbackRequest({originId:id,deliveryKey:'abcde',text:' '}),/feedback_text/);
});
