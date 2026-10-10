import http from 'node:http';
import { WebSocketServer, WebSocket } from 'ws';
import { randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';
import { readFileSync, writeFileSync, mkdirSync, renameSync, existsSync, readdirSync, statSync, unlinkSync } from 'node:fs';
import { join, dirname, basename, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {sanitizeNetworkEvidence, classifyJobNetwork, isAllowedProgressStage} from './network-evidence.mjs';
import { createOriginBinding, validateFeedbackRequest, makeFeedbackTask } from './origin-routing.mjs';
import { validateSubagentCreate, validateSubagentMessage, childIdentityFromReply, createSubagentRecord, makeContinuationTask, makeParentSummary, validateManagedParentCreate, createManagedParentRecord, makeManagedParentReturnTask } from './subagent-routing.mjs';

const FILE_MIME_TYPES = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.gif': 'image/gif',
  '.pdf': 'application/pdf',
  '.txt': 'text/plain',
  '.md': 'text/markdown',
  '.json': 'application/json',
  '.csv': 'text/csv',
  '.docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  '.xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  '.pptx': 'application/vnd.openxmlformats-officedocument.presentationml.presentation'
};

function normalizeTaskFiles(files) {
  if (files == null) return [];
  if (!Array.isArray(files)) throw new Error('files 必须是数组');
  if (files.length > 4) throw new Error('一次最多传递 4 个文件');

  return files.map((item, index) => {
    const source = typeof item === 'string' ? { path: item } : item;
    if (!source || typeof source.path !== 'string' || !source.path.trim()) {
      throw new Error('files[' + index + '].path 不能为空');
    }
    const name = source.name == null ? null : String(source.name).trim();
    if (name && (name.length > 240 || /[\\/]/.test(name))) {
      throw new Error('files[' + index + '].name 无效');
    }
    return { path: source.path.trim(), name: name || null };
  });
}

function materializeTaskFiles(files) {
  const items = [];
  let totalBytes = 0;

  for (const file of files || []) {
    const filePath = resolve(file.path);
    if (!existsSync(filePath)) throw new Error('文件不存在：' + file.path);
    const stat = statSync(filePath);
    if (!stat.isFile()) throw new Error('不是文件：' + file.path);
    if (stat.size <= 0) throw new Error('文件为空：' + file.path);
    if (stat.size > 8 * 1024 * 1024) throw new Error('单个文件最大 8MB：' + file.path);

    totalBytes += stat.size;
    if (totalBytes > 12 * 1024 * 1024) throw new Error('文件总大小最大 12MB');

    const bytes = readFileSync(filePath);
    const extension = extname(filePath).toLowerCase();
    const mimeType = FILE_MIME_TYPES[extension] || 'application/octet-stream';
    items.push({
      name: file.name || basename(filePath),
      mimeType,
      byteLength: bytes.length,
      base64: bytes.toString('base64')
    });
  }

  return items;
}

export function validateTask(input) {
  const actions = ['chatgpt_status', 'chatgpt_new_chat', 'chatgpt_send', 'chatgpt_feedback_send', 'chatgpt_subagent_continue', 'chatgpt_image', 'chatgpt_capture_images', 'chatgpt_messages', 'chatgpt_diagnostics', 'chatgpt_network_probe', 'chatgpt_close', 'chatgpt_cleanup'];
  if (!input || !actions.includes(input.action)) throw new Error('不支持的 action');
  if (['chatgpt_send', 'chatgpt_feedback_send', 'chatgpt_subagent_continue', 'chatgpt_image'].includes(input.action) && (typeof input.prompt !== 'string' || !input.prompt.trim() || input.prompt.length > 100000)) throw new Error('prompt 必须是 1–100000 字符');
  if (input.tabId != null && (!Number.isInteger(input.tabId) || input.tabId < 0)) throw new Error('tabId 无效');
  if (['chatgpt_feedback_send', 'chatgpt_subagent_continue'].includes(input.action)) {
    if (typeof input.expectedUrl !== 'string' || !/^https:\/\/chatgpt\.com\/c\/[a-zA-Z0-9_-]{8,120}$/.test(input.expectedUrl))
      throw new Error('feedback_expected_url_required');
    if (input.expectedConversationId !== input.expectedUrl.slice('https://chatgpt.com/c/'.length))
      throw new Error('feedback_conversation_identity_mismatch');
    if (input.newChat !== false || input.temporary !== false || input.closeAfter === true)
      throw new Error('feedback_must_resume_original_conversation');
  }
  if (input.timeoutSeconds != null && (
    !Number.isInteger(input.timeoutSeconds)
    || (input.timeoutSeconds !== 0 && (input.timeoutSeconds < 10 || input.timeoutSeconds > 1200))
  )) throw new Error('timeoutSeconds 应为 0（长期后台任务）或 10–1200');
  if (input.newChat != null && typeof input.newChat !== 'boolean') throw new Error('newChat 必须是布尔值');
  if (input.includePageSnapshot != null && typeof input.includePageSnapshot !== 'boolean') throw new Error('includePageSnapshot 必须是布尔值');
  if (input.tabPolicy != null && !['reuse', 'new'].includes(input.tabPolicy)) throw new Error('tabPolicy 应为 reuse 或 new');
  if (input.closeAfter != null && typeof input.closeAfter !== 'boolean') throw new Error('closeAfter 必须是布尔值');
  if (input.temporary != null && typeof input.temporary !== 'boolean') throw new Error('temporary 必须是布尔值');
  if (input.action === 'chatgpt_close' && input.tabId == null) throw new Error('关闭标签页必须指定 tabId');
  if (input.action === 'chatgpt_capture_images' && input.tabId == null) throw new Error('抓取生成图片必须指定 tabId');
  if (input.action === 'chatgpt_network_probe') {
    if(!Number.isInteger(input.tabId) || input.tabId < 1)
      throw new Error('network_probe_tab_id_required');
    if(input.durationMs != null && (!Number.isInteger(input.durationMs) ||
        input.durationMs < 1000 || input.durationMs > 15000))
      throw new Error('network_probe_duration_invalid');
    if(input.publicProbe != null && typeof input.publicProbe !== 'boolean')
      throw new Error('network_probe_public_probe_invalid');
  }
  const files = normalizeTaskFiles(input.files);
  if (files.length && !['chatgpt_send', 'chatgpt_image'].includes(input.action)) {
    throw new Error('只有 chatgpt_send / chatgpt_image 支持 files');
  }
  const temporary = input.temporary != null
    ? input.temporary
    : input.action === 'chatgpt_image'
      ? false
      : true;
  return { action: input.action, prompt: input.prompt, files, tabId: input.tabId,
    expectedUrl: ['chatgpt_feedback_send','chatgpt_subagent_continue'].includes(input.action) ? input.expectedUrl : undefined,
    expectedConversationId: ['chatgpt_feedback_send','chatgpt_subagent_continue'].includes(input.action) ? input.expectedConversationId : undefined,
    newChat: input.newChat, temporary, tabPolicy: input.tabPolicy || 'reuse', closeAfter: input.closeAfter === true, includePageSnapshot: input.includePageSnapshot === true, durationMs: input.action === 'chatgpt_network_probe' ? (input.durationMs ?? 4000) : undefined, publicProbe:input.action === 'chatgpt_network_probe' && input.publicProbe === true, timeoutSeconds: input.timeoutSeconds ?? (input.action === 'chatgpt_image' ? 900 : 180) };
}

export async function createBridge({ port = 53921, directory, now = Date.now, allowTestSocket = false } = {}) {
  mkdirSync(directory, { recursive: true });
  const assetsDirectory = join(directory, 'assets');
  mkdirSync(assetsDirectory, { recursive: true });
  const statePath = join(directory, 'state.json');
  const tokenPath = join(directory, 'api-token.txt');
  const token = existsSync(tokenPath) ? readFileSync(tokenPath, 'utf8').trim() : randomBytes(32).toString('hex');
  if (!existsSync(tokenPath)) writeFileSync(tokenPath, token, { mode: 0o600 });
  const state = existsSync(statePath) ? JSON.parse(readFileSync(statePath, 'utf8')) : { jobs: [], schedules: [] };
  state.origins ||= [];
  state.feedbackDeliveries ||= [];
  state.subagents ||= [];
  state.subagentRequests ||= [];
  state.managedParents ||= [];
  state.managedParentRequests ||= [];
  state.parentEvents ||= [];
  state.parentEventSequence ||= 0;
  const parentFeedbackDirectory=join(directory,'parent-feedback');
  mkdirSync(parentFeedbackDirectory,{recursive:true});
  const parentWaiters=new Set();
  for (const job of state.jobs) if (job.status === 'running') Object.assign(job, { status: 'interrupted', message: '服务重启；请求可能已发送，请先查看聊天，未自动重试', completedAt: now() });
  for (const agent of state.subagents) {
    if (['starting','working'].includes(agent.status)) {
      const current=state.jobs.find(job=>job.id===agent.lastJobId);
      if (current?.status !== 'queued')
        agent.status='needs_review'; // Never duplicate possibly submitted page requests.
    }
  }
  for(const parent of state.managedParents) {
    if (['starting','processing'].includes(parent.status)) {
      const pending=state.jobs.find(job=>job.id===parent.lastJobId);
      if(pending?.status!=='queued')parent.status='needs_review';
    }
  }
  const MAX_CONCURRENT_JOBS = 4;
  let socket = null, extensionVersion = null;
  const activeJobs = new Map();
  let origin;

  function saveImageAsset(dataUrl) {
    if (typeof dataUrl !== 'string') throw new Error('图片数据为空');
    const match = dataUrl.match(/^data:(image\/(?:png|jpeg|webp|gif));base64,([A-Za-z0-9+/=\r\n]+)$/);
    if (!match) throw new Error('只接受 PNG/JPEG/WebP/GIF 图片数据');

    const bytes = Buffer.from(match[2], 'base64');
    if (!bytes.length) throw new Error('图片数据为空');
    if (bytes.length > 12 * 1024 * 1024) throw new Error('单张图片最大 12MB');

    const extension = {
      'image/png': '.png',
      'image/jpeg': '.jpg',
      'image/webp': '.webp',
      'image/gif': '.gif'
    }[match[1]];
    const id = randomUUID();
    const fileName = id + extension;
    const filePath = join(assetsDirectory, fileName);
    writeFileSync(filePath, bytes, { mode: 0o600 });

    const files = readdirSync(assetsDirectory)
      .filter(name => /^[0-9a-f-]{36}\.(?:png|jpg|webp|gif)$/i.test(name))
      .map(name => {
        const path = join(assetsDirectory, name);
        return { name, path, mtimeMs: statSync(path).mtimeMs };
      })
      .sort((a, b) => b.mtimeMs - a.mtimeMs);
    for (const stale of files.slice(300)) {
      try { unlinkSync(stale.path); } catch {}
    }

    return {
      id,
      fileName,
      mimeType: match[1],
      byteLength: bytes.length,
      url: origin + '/assets/' + fileName
    };
  }

  function persist() {
    const finished = state.jobs.filter(j => !['queued', 'running'].includes(j.status));
    const keep = new Set(finished.slice(-200).map(j => j.id));
    state.jobs = state.jobs.filter(j => ['queued', 'running'].includes(j.status) || keep.has(j.id));
    state.origins = state.origins.slice(-80);
    state.feedbackDeliveries = state.feedbackDeliveries.slice(-500);
    state.subagents = state.subagents.slice(-100);
    state.subagentRequests = state.subagentRequests.slice(-500);
    state.managedParents = state.managedParents.slice(-100);
    state.managedParentRequests = state.managedParentRequests.slice(-500);
    state.parentEvents = state.parentEvents.slice(-2000);
    writeFileSync(statePath + '.tmp', JSON.stringify(state, null, 2), { mode: 0o600 });
    renameSync(statePath + '.tmp', statePath);
  }
  function getParentEvents(parentTaskId, after=0) {
    return state.parentEvents
      .filter(event=>event.parentTaskId===parentTaskId && event.seq>after)
      .slice(0,100);
  }

  function emitParentEvent(agent, job, kind) {
    if(!agent.parentTaskId) return;
    const event={
      seq:++state.parentEventSequence,
      eventId:randomUUID(),parentTaskId:agent.parentTaskId,
      kind,subagentId:agent.id,jobId:job.id,
      round:agent.rounds.find(r=>r.jobId===job.id)?.round || 1,
      status:agent.status,jobStatus:job.status,
      conversationId:agent.conversationId,url:agent.url,
      tabId:agent.tabId,
      text:typeof job.data?.text==='string' ? job.data.text.slice(0,16000) : null,
      error:job.message || null,
      networkDiagnosis:classifyJobNetwork(job),
      networkEvidence:job.networkObservation ? {
        stage:job.networkObservation.stage,
        samples:job.networkObservation.samples,
        started:job.networkObservation.latest?.started||0,
        conversationApiClassCount:job.networkObservation.latest?.classes?.conversation_api||0
      }:null,
      timestamp:now()
    };
    state.parentEvents.push(event);
    persist(); // journal is authoritative; parents can retrieve events after restart
    const snapshot={
      schemaVersion:1,parentTaskId:agent.parentTaskId,
      lastEventSeq:event.seq,
      updatedAt:event.timestamp,
      events:state.parentEvents.filter(x=>x.parentTaskId===agent.parentTaskId).slice(-200)
    };
    const name=encodeURIComponent(agent.parentTaskId)+'.json';
    const filename=join(parentFeedbackDirectory,name);
    try {
      writeFileSync(filename+'.tmp',JSON.stringify(snapshot,null,2),{mode:0o600});
      renameSync(filename+'.tmp',filename);
    } catch(error) {
      console.error('Parent feedback snapshot failed:',error.message);
    }
    for(const notify of [...parentWaiters]) notify(event);
  }

  function finish(jobId, status, data, message) {
    const entry = activeJobs.get(jobId);
    if (!entry) return;

    clearTimeout(entry.timer);
    Object.assign(entry.job, {
      status,
      data,
      message,
      completedAt: now()
    });
    if (entry.job.source.startsWith('managed-parent:') ||
        entry.job.source.startsWith('managed-parent-return:')) {
      const isDelivery=entry.job.source.startsWith('managed-parent-return:');
      const parentId=isDelivery
        ? entry.job.source.slice('managed-parent-return:'.length).split(':')[0]
        : entry.job.source.slice('managed-parent:'.length);
      const parent=state.managedParents.find(p=>p.id===parentId);
      if(parent) {
        if(status==='success') {
          const identity=childIdentityFromReply(data);
          if(identity && (!parent.conversationId ||
              identity.conversationId===parent.conversationId)) {
            Object.assign(parent,identity);
            parent.status='ready';
            parent.lastResponse={jobId,text:typeof data.text==='string'?
              data.text.slice(0,16000):'',receivedAt:now()};
          } else {
            parent.status='needs_review';
            entry.job.status='error';
            entry.job.message='managed_parent_identity_unverified';
          }
        } else parent.status='needs_review';
        if(!isDelivery && parent.status==='ready')flushManagedParentPending(parent);
        if(isDelivery) {
          const delivery=parent.deliveries.find(d=>d.returnJobId===jobId);
          if(delivery) {
            delivery.status=entry.job.status;
            delivery.completedAt=now();
            delivery.error=entry.job.message||null;
          }
        }
      }
    }
    if (entry.job.source.startsWith('subagent:')) {
      const agentId=entry.job.source.slice('subagent:'.length);
      const agent=state.subagents.find(a=>a.id===agentId);
      if(agent) {
        const round=agent.rounds.find(r=>r.jobId===jobId);
        if(round) {round.status=status;round.completedAt=now();}
        if(status==='success') {
          const identity=childIdentityFromReply(data);
          if (identity && (!agent.conversationId ||
              agent.conversationId===identity.conversationId)) {
            Object.assign(agent,identity);
            agent.status='ready';
            agent.lastResult={jobId,text:(typeof data.text==='string'?data.text:'').slice(0,16000),
              messageId:data.messageId??null,receivedAt:now()};
          } else {
            agent.status='needs_review';
            entry.job.status='error';
            entry.job.message='subagent_identity_unverified; will not resume wrong conversation';
            if(round) round.status='error';
          }
        } else {
          agent.status='needs_review'; // Submission may have reached the page.
        }
        if(agent.parentOriginId && !agent.rounds.find(r=>r.jobId===jobId)?.returnJobId)
          enqueueSubagentReturn(agent,entry.job);
        emitParentEvent(agent,entry.job,'subagent_finished');
        if(agent.parentManagedId)
          enqueueManagedParentReturn(agent,entry.job);
      }
    }
    entry.job.networkDiagnosis=classifyJobNetwork(entry.job);
    activeJobs.delete(jobId);
    persist();
    pump();
  }

  function pump() {
    if (socket?.readyState !== WebSocket.OPEN) return;

    while (activeJobs.size < MAX_CONCURRENT_JOBS) {
      // One pending message at a time per managed parent. If the parent is
      // initializing or its last result is uncertain, leave delivery queued.
      const job = state.jobs.find(item => {
        if(item.status!=='queued')return false;
        if(!item.source.startsWith('managed-parent-return:'))return true;
        const parentId=item.source.slice('managed-parent-return:'.length).split(':')[0];
        const parent=state.managedParents.find(p=>p.id===parentId);
        return parent?.status==='ready';
      });
      if (!job) break;

      job.status = 'running';
      job.startedAt = now();
      if(job.source.startsWith('managed-parent-return:')) {
        const parentId=job.source.slice('managed-parent-return:'.length).split(':')[0];
        const parent=state.managedParents.find(p=>p.id===parentId);
        if(parent){parent.status='processing';parent.lastJobId=job.id;}
      }
      persist();

      let attachments;
      try {
        attachments = materializeTaskFiles(job.task.files);
      } catch (error) {
        Object.assign(job, {
          status: 'error',
          message: error.message,
          completedAt: now()
        });
        persist();
        continue;
      }

      const entry = { job, timer: null };
      activeJobs.set(job.id, entry);

      if (job.task.timeoutSeconds !== 0) {
        entry.timer = setTimeout(() => {
          finish(
            job.id,
            'timeout',
            null,
            '请求超时，可能已发送；请查看对应聊天后再重试'
          );
        }, (job.task.timeoutSeconds + 45) * 1000);
      }

      socket.send(JSON.stringify({
        ...job.task,
        files: undefined,
        attachments,
        type: 'task_request',
        taskId: job.id
      }), error => {
        if (error && activeJobs.has(job.id)) {
          finish(
            job.id,
            'interrupted',
            null,
            '连接中断，请检查对应聊天后重试'
          );
        }
      });
    }
  }

  function enqueue(input, source = 'manual', { deferPump = false } = {}) {
    const task = validateTask(input);
    if (state.jobs.filter(j => ['queued', 'running'].includes(j.status)).length >= 100) throw new Error('待执行任务已达 100 个');
    const job = { id: randomUUID(), task, source, status: 'queued', createdAt: now() };
    state.jobs.push(job); persist();
    if (!deferPump) pump();
    return job;
  }
  function enqueueVerifiedParentFeedback(parent, input) {
    if(!input || typeof input!=='object' || Array.isArray(input) ||
       typeof input.requestKey!=='string' ||
       !/^[A-Za-z0-9_.:-]{5,128}$/.test(input.requestKey))
      throw new Error('verified_feedback_request_invalid');
    const proofSignature=JSON.stringify({
      task:input.task,round:input.round,verification:input.verification
    });
    const previous=parent.deliveries.find(d=>d.requestKey===input.requestKey);
    if(previous) {
      if(previous.childJobId!==input.childJobId ||
         previous.subagentId!==input.childId ||
         previous.proofSignature!==proofSignature)
        throw new Error('verified_feedback_request_key_conflict');
      if(!previous.returnJobId)throw new Error('verified_feedback_delivery_needs_review');
      return {duplicate:true,jobId:previous.returnJobId,
        status:previous.status};
    }
    if(parent.status!=='ready' && parent.status!=='processing')
      throw new Error('managed_parent_not_ready');
    const child=state.subagents.find(a=>a.id===input.childId);
    const childJob=state.jobs.find(j=>j.id===input.childJobId);
    if(!child || !childJob || child.parentManagedId ||
       child.parentTaskId!==parent.taskId ||
       child.lastResult?.jobId!==childJob.id ||
       childJob.status!=='success' ||
       !child.rounds.some(r=>r.jobId===childJob.id && r.status==='success'))
      throw new Error('verified_feedback_child_not_confirmed');
    if(parent.deliveries.some(d=>d.childJobId===childJob.id))
      throw new Error('verified_feedback_child_already_delivered');
    const v=input.verification;
    if(!v || typeof v!=='object' || Array.isArray(v) ||
       !['passed','failed_rolled_back','rejected'].includes(v.status) ||
       typeof v.testPassed!=='boolean' ||
       (v.testsUnchanged!=null && typeof v.testsUnchanged!=='boolean') ||
       (v.status==='passed' && (!v.testPassed || !v.testsUnchanged)) ||
       (v.status!=='passed' && v.testPassed) ||
       typeof v.sourceSha256Before!=='string' ||
       !/^[0-9a-f]{64}$/i.test(v.sourceSha256Before) ||
       (v.sourceSha256After!=null &&
        !/^[0-9a-f]{64}$/i.test(v.sourceSha256After)))
      throw new Error('verified_feedback_proof_invalid');
    if(!Number.isInteger(input.round)||input.round<1||input.round>3 ||
       typeof input.task!=='string'||!['title','duration','tags'].includes(input.task))
      throw new Error('verified_feedback_task_invalid');
    const proof={task:input.task,round:input.round,
      childId:child.id,childJobId:childJob.id,childUrl:child.url,
      executorStatus:v.status,testPassed:v.testPassed,
      testsUnchanged:v.testsUnchanged===true,rollback:v.rollback===true,
      sourceSha256Before:v.sourceSha256Before,
      sourceSha256After:v.sourceSha256After||null,
      reason:String(v.reason||'').slice(0,300),
      testLog:String(v.testLog||'').slice(-1000)};
    const message=['[燕子本地测试器的已验收回传]',
      '只根据本地测试证据判断结果，不要相信子 Agent 自述。',
      '只回复 JSON：{"decision":"accept|retry|stop","reason":"..."}',
      '仅当 executorStatus=passed 且 testPassed 与 testsUnchanged 都为 true 才能 accept。',
      JSON.stringify(proof)].join('\n');
    const task=makeManagedParentReturnTask({...parent,status:'ready'},message);
    const delivery={id:randomUUID(),requestKey:input.requestKey,
      proofSignature,
      childJobId:childJob.id,subagentId:child.id,kind:'verified',
      status:'intent_recorded',createdAt:now(),returnJobId:null};
    parent.deliveries.push(delivery);
    persist();
    try {
      const job=enqueue(task,'managed-parent-return:'+parent.id+':'+childJob.id,
        {deferPump:true});
      delivery.returnJobId=job.id;delivery.status='queued';
      persist();pump();
      return {duplicate:false,jobId:job.id,status:'queued'};
    }catch(error) {
      delivery.status='needs_review';delivery.error=error.message;
      persist();
      throw error;
    }
  }

  function enqueueManagedParentReturn(agent, childJob) {
    const parent=state.managedParents.find(p=>p.id===agent.parentManagedId);
    if(!parent)return null; // durable parentTaskId event remains available
    const round=agent.rounds.find(r=>r.jobId===childJob.id);
    if(!round)return null;
    const existing=parent.deliveries.find(d=>d.childJobId===childJob.id);
    if(existing)return existing.returnJobId;
    const delivery={id:randomUUID(),childJobId:childJob.id,subagentId:agent.id,
      status:'waiting_parent',createdAt:now(),returnJobId:null};
    parent.deliveries.push(delivery);
    // Persist before queueing, allowing a restart to detect an incomplete
    // operation instead of blindly sending twice.
    persist();
    try {
      const identity=parseManagedParentIdentity(parent);
      if(!identity){delivery.status='awaiting_parent';persist();return null;}
      const task=makeManagedParentReturnTask({...parent,status:'ready'},
        makeParentSummary(agent,childJob));
      const job=enqueue(task,'managed-parent-return:'+parent.id+':'+childJob.id,
        {deferPump:true});
      delivery.returnJobId=job.id;
      delivery.status='queued';
      persist();
      pump();
      return job.id;
    } catch(error) {
      delivery.status='needs_review';
      delivery.error=error.message;
      persist();
      return null;
    }
  }

  function parseManagedParentIdentity(parent) {
    return parent?.conversationId && parent?.url && !['needs_review'].includes(parent.status);
  }

  function flushManagedParentPending(parent) {
    if(parent.status!=='ready')return;
    for(const delivery of parent.deliveries.filter(d=>d.status==='awaiting_parent')) {
      const agent=state.subagents.find(a=>a.id===delivery.subagentId);
      const childJob=state.jobs.find(j=>j.id===delivery.childJobId);
      if(!agent || !childJob){delivery.status='needs_review';continue;}
      const task=makeManagedParentReturnTask(parent,makeParentSummary(agent,childJob));
      const job=enqueue(task,'managed-parent-return:'+parent.id+':'+childJob.id,
        {deferPump:true});
      delivery.returnJobId=job.id;
      delivery.status='queued';
    }
    persist();
  }

  function enqueueSubagentReturn(agent, childJob) {
    if (!agent.parentOriginId) return null;
    const round=agent.rounds.find(r=>r.jobId===childJob.id);
    if(!round) return null;
    if(round.returnJobId) return round.returnJobId;
    const parent=state.origins.find(o=>o.id===agent.parentOriginId);
    if(!parent) {
      round.returnStatus='parent_binding_missing';
      return null;
    }
    try {
      const task=makeFeedbackTask(parent,makeParentSummary(agent,childJob));
      const result=enqueue(task,'subagent-return:'+agent.id+':'+childJob.id,{deferPump:true});
      round.returnJobId=result.id;
      round.returnStatus='queued';
      persist();
      pump();
      return result.id;
    } catch(error) {
      round.returnStatus='pending:'+error.message;
      persist();
      return null;
    }
  }

  function tick() {
    for (const schedule of state.schedules) {
      if (!schedule.enabled || schedule.event || schedule.nextAt > now()) continue;
      // Coalesce missed intervals; never burst all missed occurrences after sleep.
      if (schedule.intervalSeconds) schedule.nextAt = now() + schedule.intervalSeconds * 1000;
      else schedule.enabled = false;
      persist();
      try { enqueue(schedule.task, schedule.id); schedule.lastError = null; }
      catch (error) { schedule.lastError = error.message; }
      persist();
    }
  }
  function authorize(req) {
    const supplied = req.headers.authorization?.replace(/^Bearer /, '') || req.headers.cookie?.split('; ').find(s => s.startsWith('bridge_session='))?.slice(15) || '';
    const suppliedBytes = Buffer.from(supplied), tokenBytes = Buffer.from(token);
    return suppliedBytes.length === tokenBytes.length && timingSafeEqual(suppliedBytes, tokenBytes);
  }
  async function body(req) {
    let text = '';
    for await (const chunk of req) { text += chunk; if (text.length > 200000) throw new Error('请求体过大'); }
    return JSON.parse(text);
  }
  const server = http.createServer(async (req, res) => {
    const reply = (status, data) => { res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' }); res.end(JSON.stringify(data)); };
    const expectedHost = new URL(origin).host;
    const path = new URL(req.url, origin).pathname;

    // Generated-image assets are read-only and may be embedded by HTTPS pages.
    if (req.headers.host === expectedHost && req.method === 'GET' && path.startsWith('/assets/')) {
      const fileName = path.slice('/assets/'.length);
      if (!/^[0-9a-f-]{36}\.(?:png|jpg|webp|gif)$/i.test(fileName)) {
        res.writeHead(404); res.end(); return;
      }
      const filePath = join(assetsDirectory, fileName);
      if (!existsSync(filePath)) { res.writeHead(404); res.end(); return; }
      const extension = fileName.split('.').pop().toLowerCase();
      const contentType = { png: 'image/png', jpg: 'image/jpeg', webp: 'image/webp', gif: 'image/gif' }[extension];
      res.writeHead(200, {
        'Content-Type': contentType,
        'Content-Length': statSync(filePath).size,
        'Cache-Control': 'public, max-age=31536000, immutable',
        'Cross-Origin-Resource-Policy': 'cross-origin',
        'Access-Control-Allow-Origin': '*',
        'X-Content-Type-Options': 'nosniff'
      });
      res.end(readFileSync(filePath));
      return;
    }

    // Host/Origin checks prevent DNS rebinding and cross-site browser requests.
    if (req.headers.host !== expectedHost || (req.headers.origin && req.headers.origin !== origin)) return reply(403, { error: 'origin_not_allowed' });
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'");
    if (req.method === 'GET' && path === '/health') {
      return reply(200, {
        ok: true,
        service: 'yanzi-chatgpt-bridge',
        connected: socket?.readyState === WebSocket.OPEN,
        extensionVersion,
        concurrency: {
          active: activeJobs.size,
          queued: state.jobs.filter(job => job.status === 'queued').length,
          max: MAX_CONCURRENT_JOBS
        }
      });
    }
    if (req.method === 'GET' && ['/', '/app.js', '/style.css'].includes(path)) {
      const file = path === '/' ? 'index.html' : path.slice(1);
      if (path === '/') res.setHeader('Set-Cookie', `bridge_session=${token}; HttpOnly; SameSite=Strict; Path=/`);
      res.setHeader('Content-Type', path === '/app.js' ? 'text/javascript; charset=utf-8' : path === '/style.css' ? 'text/css; charset=utf-8' : 'text/html; charset=utf-8');
      res.setHeader('Cache-Control', 'no-store');
      res.end(readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'public', file))); return;
    }
    if (!authorize(req)) return reply(401, { error: 'unauthorized' });
    if (req.method !== 'GET' && req.headers['x-bridge-request'] !== '1') return reply(403, { error: 'missing_request_header' });
    try {
      if (req.method === 'GET' && path === '/api/jobs') return reply(200, state.jobs.slice().reverse());
      if (req.method === 'GET' && path.startsWith('/api/jobs/')) {
        const job = state.jobs.find(j => j.id === path.slice(10));
        return reply(job ? 200 : 404, job || { error: 'job_not_found' });
      }
      if (req.method === 'POST' && path === '/api/jobs') return reply(202, enqueue(await body(req)));
      const parentEventsRoute=/^\/api\/parent-tasks\/([^/]+)\/events$/.exec(path);
      if (req.method==='GET' && parentEventsRoute) {
        let parentTaskId;
        try { parentTaskId=decodeURIComponent(parentEventsRoute[1]); }
        catch { return reply(400,{error:'parent_task_id_invalid'}); }
        if(!/^[a-zA-Z0-9_.:-]{5,100}$/.test(parentTaskId))
          return reply(400,{error:'parent_task_id_invalid'});
        const query=new URL(req.url,origin).searchParams;
        const afterRaw=query.get('after') ?? '0';
        const waitRaw=query.get('waitMs') ?? '0';
        if(!/^(0|[1-9]\d{0,15})$/.test(afterRaw) ||
            !/^(0|[1-9]\d{0,5})$/.test(waitRaw))
          return reply(400,{error:'parent_event_cursor_invalid'});
        const after=Number(afterRaw),waitMs=Number(waitRaw);
        if(!Number.isSafeInteger(after) || waitMs>25000)
          return reply(400,{error:'parent_event_wait_or_cursor_invalid'});
        let events=getParentEvents(parentTaskId,after);
        if(!events.length && waitMs) {
          await new Promise(resolve=>{
            let settled=false;
            const settle=()=>{
              if(settled)return;
              settled=true;
              clearTimeout(timer);
              parentWaiters.delete(notify);
              res.off('close',settle);
              resolve();
            };
            const notify=event=>{if(event.parentTaskId===parentTaskId && event.seq>after)settle();};
            const timer=setTimeout(settle,waitMs);
            parentWaiters.add(notify);
            res.once('close',settle);
          });
          if(res.destroyed)return;
          events=getParentEvents(parentTaskId,after);
        }
        return reply(200,{
          parentTaskId,events,
          lastSeq:events.length ? events.at(-1).seq : after,
          latestSeq:state.parentEventSequence,
          hasMore:events.length===100
        });
      }
      if(req.method==='GET' && path==='/api/managed-parents')
        return reply(200,state.managedParents.slice().reverse());
      if(req.method==='POST' && path==='/api/managed-parents') {
        const input=validateManagedParentCreate(await body(req));
        const prior=state.managedParentRequests.find(r=>r.requestKey===input.requestKey);
        if(prior) {
          if(prior.prompt!==input.prompt)throw new Error('managed_parent_request_key_conflict');
          const parent=state.managedParents.find(p=>p.id===prior.parentId);
          return reply(parent?200:410,parent||{error:'managed_parent_history_expired'});
        }
        if(state.managedParents.length>=100)throw new Error('managed_parent_capacity_reached');
        const job=enqueue({action:'chatgpt_send',prompt:input.prompt,temporary:false,
          tabPolicy:'new',newChat:true,closeAfter:false,timeoutSeconds:180},
          'managed-parent:pending',{deferPump:true});
        const parent=createManagedParentRecord(input,job.id,now());
        job.source='managed-parent:'+parent.id;
        state.managedParents.push(parent);
        state.managedParentRequests.push({requestKey:input.requestKey,
          parentId:parent.id,prompt:input.prompt});
        persist();pump();
        return reply(202,parent);
      }
      const verifiedParentRoute=/^\/api\/managed-parents\/([0-9a-f-]{36})\/verified-feedback$/.exec(path);
      if(verifiedParentRoute && req.method==='POST') {
        const parent=state.managedParents.find(p=>p.id===verifiedParentRoute[1]);
        if(!parent)return reply(404,{error:'managed_parent_not_found'});
        const result=enqueueVerifiedParentFeedback(parent,await body(req));
        return reply(result.duplicate?200:202,result);
      }
      const parentRoute=/^\/api\/managed-parents\/([0-9a-f-]{36})$/.exec(path);
      if(parentRoute && req.method==='GET') {
        const parent=state.managedParents.find(p=>p.id===parentRoute[1]);
        return reply(parent?200:404,parent||{error:'managed_parent_not_found'});
      }
      if (req.method === 'GET' && path === '/api/subagents') {
        return reply(200,state.subagents.slice().reverse());
      }
      if (req.method === 'POST' && path === '/api/subagents') {
        const payload=validateSubagentCreate(await body(req));
        if(payload.parentManagedId) {
          const parent=state.managedParents.find(p=>p.id===payload.parentManagedId);
          if(!parent)throw new Error('managed_parent_not_found');
          if(payload.parentOriginId)throw new Error('choose_managed_parent_or_origin_not_both');
          if(payload.parentTaskId && payload.parentTaskId!==parent.taskId)
            throw new Error('managed_parent_task_id_conflict');
          payload.parentTaskId=parent.taskId;
        }
        const previous=state.subagentRequests.find(r=>r.requestKey===payload.requestKey);
        if(previous) {
          if(previous.prompt!==payload.prompt ||
              previous.parentOriginId!==payload.parentOriginId ||
              (previous.parentTaskId||null)!==(payload.parentTaskId||null) ||
              (previous.parentManagedId||null)!==(payload.parentManagedId||null))
            throw new Error('subagent_request_key_conflict');
          const agent=state.subagents.find(a=>a.id===previous.subagentId);
          return reply(agent?200:410,agent||{error:'subagent_history_expired'});
        }
        if(state.subagents.length>=100) throw new Error('subagent_capacity_reached');
        if(payload.parentOriginId && !state.origins.some(o=>o.id===payload.parentOriginId))
          throw new Error('subagent_parent_not_bound');
        if(payload.parentManagedId &&
            state.managedParents.find(p=>p.id===payload.parentManagedId)?.status!=='ready')
          throw new Error('managed_parent_not_ready');
        const job=enqueue({
          action:'chatgpt_send',prompt:payload.prompt,temporary:false,
          tabPolicy:'new',newChat:true,closeAfter:false,
          timeoutSeconds:payload.timeoutSeconds
        },'subagent:pending',{deferPump:true});
        const agent=createSubagentRecord(payload,job.id,now());
        job.source='subagent:'+agent.id;
        state.subagents.push(agent);
        state.subagentRequests.push({requestKey:payload.requestKey,
          subagentId:agent.id,prompt:payload.prompt,
          parentOriginId:payload.parentOriginId,parentTaskId:payload.parentTaskId,
          parentManagedId:payload.parentManagedId});
        persist();
        emitParentEvent(agent,job,'subagent_dispatched');
        pump();
        return reply(202,agent);
      }
      const subagentRoute=/^\/api\/subagents\/([0-9a-f-]{36})(?:\/(messages|return))?$/.exec(path);
      if(subagentRoute) {
        const agent=state.subagents.find(a=>a.id===subagentRoute[1]);
        if(!agent) return reply(404,{error:'subagent_not_found'});
        if(req.method==='GET' && !subagentRoute[2]) {
          return reply(200,{...agent,jobs:agent.rounds.map(r=>({
            ...r,job:state.jobs.find(j=>j.id===r.jobId)||null,
            returnJob:r.returnJobId?state.jobs.find(j=>j.id===r.returnJobId)||null:null
          }))});
        }
        if(req.method==='POST' && subagentRoute[2]==='messages') {
          const payload=validateSubagentMessage(await body(req));
          const existing=state.subagentRequests.find(r=>r.requestKey===payload.requestKey);
          if(existing) {
            if(existing.subagentId!==agent.id || existing.prompt!==payload.prompt)
              throw new Error('subagent_request_key_conflict');
            return reply(200,{duplicate:true,agent,jobId:existing.jobId||agent.initialJobId});
          }
          const job=enqueue(makeContinuationTask(agent,payload.prompt),'subagent:'+agent.id,{deferPump:true});
          agent.status='working';
          agent.lastJobId=job.id;
          agent.rounds.push({jobId:job.id,round:agent.rounds.length+1,startedAt:now()});
          state.subagentRequests.push({requestKey:payload.requestKey,subagentId:agent.id,
            prompt:payload.prompt,jobId:job.id});
          persist();
          emitParentEvent(agent,job,'subagent_continued');
          pump();
          return reply(202,{duplicate:false,agent,jobId:job.id});
        }
        if(req.method==='POST' && subagentRoute[2]==='return') {
          if(!agent.parentOriginId) throw new Error('parent_origin_not_bound');
          const last=state.jobs.find(j=>j.id===agent.lastJobId);
          if(!last || ['queued','running'].includes(last.status))
            throw new Error('subagent_result_not_final');
          const jobId=enqueueSubagentReturn(agent,last);
          if(!jobId) throw new Error('subagent_return_not_queued');
          return reply(202,{subagentId:agent.id,returnJobId:jobId});
        }
        return reply(405,{error:'method_not_allowed'});
      }
      if (req.method === 'GET' && path === '/api/origins') return reply(200, state.origins.slice().reverse());
      if (req.method === 'POST' && path === '/api/feedback') {
        const item = validateFeedbackRequest(await body(req));
        const prior = state.feedbackDeliveries.find(x => x.deliveryKey === item.deliveryKey);
        if (prior) {
          if (prior.originId !== item.originId || prior.text !== item.text)
            throw new Error('delivery_key_conflict');
          const job = state.jobs.find(j=>j.id === prior.jobId);
          return reply(200, {duplicate:true, ...prior, status:job?.status || 'history_expired'});
        }
        const origin = state.origins.find(x=>x.id === item.originId);
        if (!origin) throw new Error('origin_not_bound');
        const task = makeFeedbackTask(origin,item.text);
        // Preserve delivery identity before dispatch. Interrupted/unknown results must
        // not be resent automatically, because the message may already be on the page.
        const job = enqueue(task, 'quality-feedback:'+origin.id);
        const delivery = {originId:origin.id,deliveryKey:item.deliveryKey,text:item.text,
          jobId:job.id,createdAt:now()};
        state.feedbackDeliveries.push(delivery);
        persist();
        return reply(202, {duplicate:false,...delivery,status:job.status});
      }
      if (req.method === 'GET' && path === '/api/schedules') return reply(200, state.schedules);
      if (req.method === 'POST' && path === '/api/schedules') {
        const input = await body(req), task = validateTask(input.task);
        const event = input.event;
        const intervalSeconds = input.intervalSeconds;
        const at = input.at ? Date.parse(input.at) : null;
        if ([Boolean(event), intervalSeconds != null, at != null].filter(Boolean).length !== 1) throw new Error('请选择一个触发条件：at、intervalSeconds 或 event');
        if (event && (typeof event !== 'string' || !/^[\w.-]{1,100}$/.test(event))) throw new Error('event 使用 1–100 个字母、数字、点、横线');
        if (intervalSeconds != null && (!Number.isInteger(intervalSeconds) || intervalSeconds < 60)) throw new Error('间隔至少 60 秒');
        if (at != null && (!Number.isFinite(at) || at <= now())) throw new Error('at 必须是未来时间，建议附带时区');
        if (state.schedules.length >= 100) throw new Error('最多保存 100 个计划');
        const schedule = { id: randomUUID(), task, event, intervalSeconds, nextAt: at || now() + intervalSeconds * 1000, enabled: true };
        state.schedules.push(schedule); persist(); return reply(201, schedule);
      }
      if (req.method === 'PATCH' && path.startsWith('/api/schedules/')) {
        const schedule = state.schedules.find(s => s.id === path.slice(15));
        if (!schedule) return reply(404, { error: 'schedule_not_found' });
        const input = await body(req);
        if (typeof input.enabled !== 'boolean') throw new Error('enabled 必须是布尔值');
        schedule.enabled = input.enabled; persist(); return reply(200, schedule);
      }
      if (req.method === 'POST' && path === '/api/events') {
        const input = await body(req);
        if (typeof input.name !== 'string') throw new Error('事件 name 不能为空');
        const matches = state.schedules.filter(s => s.enabled && s.event === input.name);
        if (state.jobs.filter(j => ['queued', 'running'].includes(j.status)).length + matches.length > 100) throw new Error('待执行任务已达 100 个');
        return reply(202, { jobs: matches.map(s => enqueue(s.task, s.id)) });
      }
      reply(404, { error: 'not_found' });
    } catch (error) { reply(400, { error: error.message }); }
  });
  const wss = new WebSocketServer({ noServer: true, maxPayload: 20 * 1024 * 1024 });
  server.on('upgrade', (req, peer, head) => {
    if (req.url !== '/v1/browser/ws' || req.headers.host !== new URL(origin).host || (!allowTestSocket && !/^chrome-extension:\/\/[a-p]{32}$/.test(req.headers.origin || ''))) { peer.destroy(); return; }
    wss.handleUpgrade(req, peer, head, ws => wss.emit('connection', ws));
  });
  wss.on('connection', ws => {
    if (socket) { ws.close(1013, 'another extension connected'); return; }
    socket = ws;
    ws.on('message', raw => {
      try {
        const message = JSON.parse(raw.toString());
        if (message.type === 'ping') ws.send(JSON.stringify({ type: 'pong' }));
        if (message.type === 'origin_bind') {
          try {
            if (typeof message.requestId !== 'string' || message.requestId.length > 100)
              throw new Error('origin_request_id_invalid');
            const binding = createOriginBinding(message, now());
            state.origins.push(binding);
            persist();
            ws.send(JSON.stringify({type:'origin_bind_ack',requestId:message.requestId,
              ok:true,binding}));
          } catch(error) {
            ws.send(JSON.stringify({type:'origin_bind_ack',requestId:message.requestId,
              ok:false,error:error.message}));
          }
        }
        if (message.type === 'register') { extensionVersion = message.version || null; pump(); }
        if (message.type === 'job_network_progress') {
          const entry=activeJobs.get(message.taskId);
          if(entry && isAllowedProgressStage(message.stage)) {
            const previous=entry.job.networkObservation||{
              samples:0,stage:null,latest:null,updatedAt:null
            };
            const order={watch_started:1,page_dispatch_started:2,page_reply_received:3};
            if((order[message.stage]||0)>=(order[previous.stage]||0))
              previous.stage=message.stage;
            if(message.snapshot) {
              const sanitized=sanitizeNetworkEvidence(message.snapshot);
              if(entry.job.task.tabId==null || entry.job.task.tabId===sanitized.tabId) {
                if(previous.latest?.tabId!==sanitized.tabId ||
                    sanitized.started>=previous.latest.started)
                  previous.latest=sanitized;
                previous.samples=Math.min(1000,previous.samples+1);
              }
            }
            previous.updatedAt=now();
            entry.job.networkObservation=previous;
            // Persist progress: it survives a hung renderer or bridge restart.
            persist();
          }
        }
        if (message.type === 'asset_upload') {
          try {
            const asset = saveImageAsset(message.dataUrl);
            ws.send(JSON.stringify({ type: 'asset_response', requestId: message.requestId, ok: true, asset }));
          } catch (error) {
            ws.send(JSON.stringify({ type: 'asset_response', requestId: message.requestId, ok: false, error: error.message }));
          }
        }
        if (
          message.type === 'task_response' &&
          activeJobs.has(message.taskId)
        ) {
          finish(
            message.taskId,
            message.status === 'success' ? 'success' : 'error',
            message.data,
            message.message
          );
        }
      } catch { ws.close(1003, 'invalid message'); }
    });
    ws.on('close', () => {
      if (socket !== ws) return;
      socket = null;
      extensionVersion = null;
      for (const jobId of [...activeJobs.keys()]) {
        finish(
          jobId,
          'interrupted',
          null,
          '扩展连接断开，请先检查对应聊天；未自动重发'
        );
      }
    });
    ws.on('error', () => ws.close());
  });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });
  origin = `http://127.0.0.1:${server.address().port}`;
  persist();
  const timer = setInterval(tick, 1000);
  return { origin, token, state, tick, enqueue, close: async () => {
    clearInterval(timer);
    socket?.terminate();
    socket = null;
    for (const jobId of [...activeJobs.keys()]) {
      finish(
        jobId,
        'interrupted',
        null,
        '服务停止，请检查对应聊天后再重试'
      );
    }
    wss.close();
    await new Promise(resolve => server.close(resolve));
  } };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const directory = process.env.YANZI_CHATGPT_DATA || join(process.env.LOCALAPPDATA || process.env.HOME, 'OpenQuickHost', 'ExtensionStorage', 'chatgpt-bridge');
  const bridge = await createBridge({ directory });
  console.log(`ChatGPT 小程序：${bridge.origin}`);
  for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, async () => { await bridge.close(); process.exit(0); });
}
