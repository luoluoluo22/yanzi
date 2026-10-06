import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { Line2 } from 'three/addons/lines/Line2.js';
import { LineGeometry } from 'three/addons/lines/LineGeometry.js';
import { LineMaterial } from 'three/addons/lines/LineMaterial.js';
import { computeConstrainedLayerLayouts } from './layout.js';
import './style.css';

const phases = [
  { title: 'T0 · 现实接触', desc: '先出现少量可感知节点，不急着抽象。' },
  { title: 'T1 · 关系形成', desc: '开始记录“谁影响谁”，让孤立节点出现连接。' },
  { title: 'T2 · 局部网络', desc: '同一问题附近的关系逐渐变密，形成可用的小网。' },
  { title: 'T3 · 跨网连接', desc: '人、钱、资源、反馈、风险开始穿过多张网络。' },
  { title: 'T4 · 规律抽象', desc: '从多张网里提炼反馈、约束、激励、因果等高层结构。' },
];

const layers = [
  { id: 'physics', title: '物理网络', subtitle: '物质 · 能量 · 运动', color: '#4da3ff', y: 4.6 },
  { id: 'body', title: '生物 / 身体', subtitle: '身体 · 大脑 · 需求', color: '#42d98a', y: 2.3 },
  { id: 'relations', title: '人际关系', subtitle: '爱 · 信任 · 承诺', color: '#ff9a4d', y: 0 },
  { id: 'economy', title: '经济网络', subtitle: '价值 · 交换 · 激励', color: '#a675ff', y: -2.3 },
  { id: 'work', title: '工作 / 社会', subtitle: '角色 · 组织 · 规则', color: '#45d6d1', y: -4.6 },
];

const nodeData = [
  { id:'mass', label:'质量', layer:'physics', symbol:'●', pos:[-4.0, 0.1], activeFrom:0, desc:'物体参与引力与运动关系的基础属性。' },
  { id:'motion', label:'运动', layer:'physics', symbol:'➜', pos:[-1.3,-1.4], activeFrom:1, desc:'状态随时间变化，是物理网络里的典型动态关系。' },
  { id:'energy', label:'能量', layer:'physics', symbol:'⚡', pos:[2.0,-0.5], activeFrom:1, desc:'描述系统改变状态的能力，并连接多个物理过程。' },
  { id:'distance', label:'距离', layer:'physics', symbol:'↔', pos:[4.3,1.4], activeFrom:2, desc:'影响许多作用强度，是条件而不是孤立结论。' },
  { id:'time', label:'时间', layer:'physics', symbol:'◷', pos:[0.9,1.8], activeFrom:2, desc:'让静态关系变成演化过程，也是整套模型的外部维度。' },

  { id:'bodyNode', label:'身体', layer:'body', symbol:'◉', pos:[-3.8,-.7], activeFrom:0, desc:'生理状态承接外部条件，并把它们转化为体验与行为约束。' },
  { id:'brain', label:'大脑', layer:'body', symbol:'✦', pos:[-1.0,1.2], activeFrom:1, desc:'感觉、记忆与判断在这里形成可观察的行为结果。' },
  { id:'sleep', label:'睡眠', layer:'body', symbol:'☾', pos:[2.3,1.4], activeFrom:1, desc:'一个典型反馈节点，会影响注意力、情绪和工作效率。' },
  { id:'hormone', label:'激素', layer:'body', symbol:'⌬', pos:[4.0,-.8], activeFrom:2, desc:'把环境与身体反应连接起来的一类生物机制。' },
  { id:'need', label:'需求', layer:'body', symbol:'♡', pos:[.8,-1.7], activeFrom:2, desc:'需求驱动行为，并会跨入经济与人际网络。' },

  { id:'love', label:'爱', layer:'relations', symbol:'♥', pos:[-4.2,.9], activeFrom:1, desc:'不是单一行为，而是通过时间、承诺、资源等多条关系被观察。' },
  { id:'trust', label:'信任', layer:'relations', symbol:'◎', pos:[-1.7,-1.4], activeFrom:1, desc:'降低协作成本，也会被失信、兑现承诺等行为不断更新。' },
  { id:'commit', label:'承诺', layer:'relations', symbol:'✓', pos:[1.3,1.4], activeFrom:2, desc:'把未来行为绑定到当前关系，是跨时间的社会机制。' },
  { id:'conflict', label:'冲突', layer:'relations', symbol:'⚔', pos:[4.2,.4], activeFrom:2, desc:'目标、资源或预期不一致时形成的关系张力。' },
  { id:'investment', label:'资源投入', layer:'relations', symbol:'⇄', pos:[1.9,-1.5], activeFrom:2, desc:'时间、金钱、精力都可以成为关系投入，但不能简单等同于爱。' },

  { id:'market', label:'市场', layer:'economy', symbol:'▥', pos:[-4.1,-.4], activeFrom:1, desc:'大量交换关系聚合后的局部结构。' },
  { id:'exchange', label:'交换', layer:'economy', symbol:'⇆', pos:[-1.5,1.5], activeFrom:1, desc:'资源在主体之间转移时形成的核心关系。' },
  { id:'income', label:'收入', layer:'economy', symbol:'＋', pos:[1.2,-1.5], activeFrom:2, desc:'工作网络和经济网络之间的重要桥梁结果。' },
  { id:'price', label:'价格', layer:'economy', symbol:'¥', pos:[4.0,1.1], activeFrom:2, desc:'供需、成本、预期和制度共同作用后的可观察信号。' },
  { id:'incentive', label:'激励', layer:'economy', symbol:'↑', pos:[1.8,1.5], activeFrom:3, desc:'通过改变收益与成本，改变行为发生的概率。' },

  { id:'role', label:'角色', layer:'work', symbol:'◐', pos:[-4.0,1.0], activeFrom:1, desc:'人在组织中的位置，会约束其责任、权限与行为预期。' },
  { id:'org', label:'组织', layer:'work', symbol:'▦', pos:[-1.1,-1.5], activeFrom:1, desc:'通过规则、资源与协作把多个个体组织成更高层结构。' },
  { id:'collab', label:'协作', layer:'work', symbol:'⌘', pos:[2.0,1.2], activeFrom:2, desc:'不同角色围绕共同目标形成的动态关系。' },
  { id:'rule', label:'规则', layer:'work', symbol:'≡', pos:[4.2,-.9], activeFrom:2, desc:'让大量个体行为具备可预期性的结构性约束。' },
  { id:'task', label:'任务', layer:'work', symbol:'□', pos:[.8,.0], activeFrom:2, desc:'把目标转化为具体行动，是工作网络里的局部执行单元。' },
];

const bridgeData = [
  {
    id:'person', label:'人', symbol:'人', color:'#ff5f72', layers:['physics','body','relations','economy','work'],
    x:-2.6, z:.2, activeFrom:0,
    desc:'同一个人同时是物体、生物体、关系主体、消费者与组织角色。它不是五个节点，而是一个穿过五张网的立体节点。',
    links:['motion','bodyNode','trust','exchange','role']
  },
  {
    id:'money', label:'钱', symbol:'¥', color:'#f1b441', layers:['relations','economy','work'],
    x:.2, z:1.25, activeFrom:2,
    desc:'钱在经济网里是交换媒介，在关系网里可能表现为投入，在工作网里连接收入与报酬。',
    links:['investment','exchange','income','price']
  },
  {
    id:'resource', label:'资源', symbol:'资', color:'#45d77a', layers:['body','relations','economy','work'],
    x:.4, z:-1.55, activeFrom:2,
    desc:'资源是跨域概念：体力、时间、注意力、金钱、组织能力都可以成为不同网络中的稀缺输入。',
    links:['need','investment','market','task']
  },
  {
    id:'feedback', label:'反馈', symbol:'↻', color:'#a969ff', layers:['physics','body','relations','economy','work'],
    x:3.0, z:-.35, activeFrom:3,
    desc:'反馈不是某个领域专属知识，而是多个网络共享的高层结构：结果会反过来改变下一轮输入。',
    links:['motion','sleep','trust','incentive','rule']
  },
  {
    id:'risk', label:'风险', symbol:'!', color:'#ff8a45', layers:['physics','body','relations','economy','work'],
    x:3.35, z:1.55, activeFrom:3,
    desc:'风险描述未来状态的不确定损失，会同时受到物理条件、身体状态、关系、价格和规则影响。',
    links:['distance','hormone','conflict','price','rule']
  },
];

const edgeSeed = [
  ['mass','motion',1,'因果','引力作用','质量在作用条件下影响运动状态。'],
  ['motion','energy',1,'因果','状态转换','运动状态变化伴随能量状态变化。'],
  ['energy','distance',2,'约束','距离衰减','距离改变会影响作用强度及能量传递条件。'],
  ['distance','time',2,'相关','时空条件','距离与时间共同限定过程展开。'],
  ['time','motion',3,'因果','状态演化','时间使运动表现为连续的状态变化。'],

  ['bodyNode','brain',1,'因果','生理输入','身体状态通过神经与生理信号影响大脑。'],
  ['brain','sleep',1,'反馈','睡眠调节','大脑状态影响睡眠，睡眠又反过来改变大脑状态。'],
  ['sleep','hormone',2,'因果','节律调节','睡眠节律影响多种激素水平。'],
  ['hormone','need',2,'因果','内稳态驱动','生理信号改变饥饿、休息等需求强度。'],
  ['need','brain',3,'反馈','需求反馈','需求进入决策系统并影响注意与行为选择。'],

  ['love','trust',1,'因果','稳定预期','持续的重视和一致行为有助于建立信任。'],
  ['trust','commit',2,'因果','承诺形成','更高信任降低承诺未来关系的心理成本。'],
  ['commit','conflict',2,'约束','边界约束','承诺会约束冲突时可接受的行为范围。'],
  ['conflict','investment',3,'反馈','冲突反馈','冲突结果会提高或降低后续资源投入。'],
  ['investment','love',3,'代理','投入信号','资源投入可以成为重视程度的信号，但不是爱的充分条件。'],

  ['market','exchange',1,'交换','交易形成','市场为主体之间的交换提供匹配环境。'],
  ['exchange','income',2,'因果','价值实现','交换完成后，一方获得收入或其他回报。'],
  ['income','price',2,'相关','支付能力','收入会影响可支付价格和需求结构。'],
  ['price','incentive',3,'因果','价格信号','价格改变收益预期，从而改变行为激励。'],
  ['incentive','market',3,'反馈','供需反馈','激励改变参与行为，聚合后又重新塑造市场。'],

  ['role','org',1,'约束','角色嵌入','角色只有放入组织结构后才具有稳定责任与权限。'],
  ['org','collab',2,'因果','协作组织','组织通过流程与资源安排形成协作。'],
  ['collab','rule',2,'反馈','规则沉淀','重复协作会把有效做法沉淀为规则。'],
  ['rule','task',2,'约束','执行边界','规则限定任务如何被执行。'],
  ['task','role',3,'反馈','角色反馈','任务结果反过来调整角色评价与分工。'],
];

const initialLayout = computeConstrainedLayerLayouts({
  layers,
  nodeData,
  bridgeData,
  edgeSeed,
  warmupTicks: 260
});
for (const node of nodeData) {
  const p = initialLayout[node.id];
  if (p) node.pos = [p.x, p.z];
}

const layerMap = Object.fromEntries(layers.map(l => [l.id, l]));
const nodeMap = Object.fromEntries(nodeData.map(n => [n.id, n]));
const bridgeMap = Object.fromEntries(bridgeData.map(n => [n.id, n]));

const relationOverrides = JSON.parse(localStorage.getItem('world-model-3d:relations') || '{}');

const root = document.getElementById('scene');
const scene = new THREE.Scene();
scene.fog = new THREE.FogExp2(0x06101d, 0.027);

const camera = new THREE.PerspectiveCamera(42, 1, .1, 100);
camera.position.set(11.5, 12, 14.5);

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.8));
renderer.setClearColor(0x000000, 0);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.08;
root.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = .06;
controls.minDistance = 8;
controls.maxDistance = 36;
controls.minPolarAngle = .22;
controls.maxPolarAngle = 1.43;
controls.screenSpacePanning = true;
controls.mouseButtons.LEFT = THREE.MOUSE.ROTATE;
controls.mouseButtons.MIDDLE = THREE.MOUSE.PAN;
controls.mouseButtons.RIGHT = THREE.MOUSE.PAN;
controls.target.set(0, 0, 0);

scene.add(new THREE.AmbientLight(0x92cfff, 1.1));

const starGeo = new THREE.BufferGeometry();
const starCount = 700;
const starPos = new Float32Array(starCount * 3);
for (let i = 0; i < starCount; i++) {
  const r = 18 + Math.random() * 28;
  const theta = Math.random() * Math.PI * 2;
  const phi = Math.acos(2 * Math.random() - 1);
  starPos[i*3] = r * Math.sin(phi) * Math.cos(theta);
  starPos[i*3+1] = r * Math.cos(phi) * .65;
  starPos[i*3+2] = r * Math.sin(phi) * Math.sin(theta);
}
starGeo.setAttribute('position', new THREE.BufferAttribute(starPos, 3));
const stars = new THREE.Points(starGeo, new THREE.PointsMaterial({ color:0x4f86aa, size:.035, transparent:true, opacity:.5 }));
scene.add(stars);

const world = new THREE.Group();
scene.add(world);

const clickables = [];
const visualNodes = new Map();
const edgeObjects = [];
const edgeMap = new Map();
const pillarObjects = [];
const layerObjects = new Map();

const relationTypeColors = {
  '因果':'#57c7ff',
  '相关':'#9aa9b8',
  '约束':'#ffb24d',
  '反馈':'#b57aff',
  '交换':'#54d7a3',
  '代理':'#ff7f8e',
  '跨网':'#f1c75b'
};

function roundedRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.roundRect(x, y, w, h, r);
}

function makeTextTexture(text, color='#eaf7ff', border='#6ea8c7', options={}) {
  const canvas = document.createElement('canvas');
  canvas.width = options.width || 512;
  canvas.height = options.height || 144;
  const ctx = canvas.getContext('2d');
  ctx.clearRect(0,0,canvas.width,canvas.height);

  if (options.background !== false) {
    roundedRect(ctx, 8, 8, canvas.width-16, canvas.height-16, 26);
    ctx.fillStyle = options.bg || 'rgba(4,12,22,.90)';
    ctx.fill();
    ctx.lineWidth = 3;
    ctx.strokeStyle = border;
    ctx.stroke();
  }

  ctx.fillStyle = color;
  ctx.font = `${options.weight || 700} ${options.fontSize || 48}px "Segoe UI","Microsoft YaHei",sans-serif`;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, canvas.width/2, canvas.height/2 + (options.yOffset || 0));

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.minFilter = THREE.LinearFilter;
  texture.magFilter = THREE.LinearFilter;
  return texture;
}

function makeFlatText(text, color, border, width=1.25, height=.34, bg='rgba(4,12,22,.90)') {
  const mesh = new THREE.Mesh(
    new THREE.PlaneGeometry(width, height),
    new THREE.MeshBasicMaterial({
      map: makeTextTexture(text, color, border, { bg }),
      transparent:true,
      side:THREE.DoubleSide,
      depthWrite:false
    })
  );
  mesh.rotation.x = -Math.PI/2;
  mesh.renderOrder = 8;
  return mesh;
}

function updateFlatText(mesh, text, color, border, bg='rgba(4,12,22,.90)') {
  if (mesh.material.map) mesh.material.map.dispose();
  mesh.material.map = makeTextTexture(text, color, border, { bg });
  mesh.material.needsUpdate = true;
}

function makePlane(layer) {
  const group = new THREE.Group();

  const plane = new THREE.Mesh(
    new THREE.PlaneGeometry(12.5, 6.2),
    new THREE.MeshBasicMaterial({
      color: layer.color,
      transparent:true,
      opacity:.055,
      side:THREE.DoubleSide,
      depthWrite:false
    })
  );
  plane.rotation.x = -Math.PI/2;
  plane.position.y = layer.y;
  group.add(plane);

  const grid = new THREE.GridHelper(12.5, 12, layer.color, layer.color);
  grid.scale.z = .5;
  grid.position.y = layer.y + .012;
  const gridMats = Array.isArray(grid.material) ? grid.material : [grid.material];
  gridMats.forEach(m => { m.transparent = true; m.opacity = .10; });
  group.add(grid);

  const borderPts = [
    [-6.25,layer.y+.018,-3.1],[6.25,layer.y+.018,-3.1],[6.25,layer.y+.018,3.1],
    [-6.25,layer.y+.018,3.1],[-6.25,layer.y+.018,-3.1]
  ].map(v => new THREE.Vector3(...v));

  const border = new THREE.Line(
    new THREE.BufferGeometry().setFromPoints(borderPts),
    new THREE.LineBasicMaterial({color:layer.color, transparent:true, opacity:.28})
  );
  group.add(border);

  world.add(group);
  layerObjects.set(layer.id, {group, plane, grid, border, layer});
}
layers.forEach(makePlane);

function makeDisc(color, radius=.28) {
  const disc = new THREE.Mesh(
    new THREE.CircleGeometry(radius, 40),
    new THREE.MeshBasicMaterial({
      color,
      transparent:true,
      opacity:.94,
      side:THREE.DoubleSide,
      depthWrite:false
    })
  );
  disc.rotation.x = -Math.PI/2;
  disc.renderOrder = 6;

  const ring = new THREE.Mesh(
    new THREE.RingGeometry(radius+.035, radius+.065, 40),
    new THREE.MeshBasicMaterial({
      color,
      transparent:true,
      opacity:.38,
      side:THREE.DoubleSide,
      depthWrite:false
    })
  );
  ring.rotation.x = -Math.PI/2;
  ring.position.y = .003;
  ring.renderOrder = 5;

  return {disc, ring};
}

function createNode(n) {
  const layer = layerMap[n.layer];
  const group = new THREE.Group();
  group.position.set(n.pos[0], layer.y + .038, n.pos[1]);

  const cardWidth = Math.max(1.18, .58 + n.label.length * .46);
  const cardHeight = .60;
  const card = makeFlatText(
    n.label,
    '#f7fcff',
    layer.color,
    cardWidth,
    cardHeight,
    'rgba(10,35,54,.97)'
  );
  card.position.y = .008;
  card.userData = {
    nodeId:n.id,
    kind:'node',
    layerId:n.layer,
    cardWidth,
    cardHeight
  };
  group.add(card);

  world.add(group);
  clickables.push(card);
  visualNodes.set(n.id, {data:n, group, card, layerId:n.layer});
}
nodeData.forEach(createNode);

function createBridge(b) {
  const ys = b.layers.map(id => layerMap[id].y);
  const minY = Math.min(...ys), maxY = Math.max(...ys);
  const height = maxY - minY + .15;

  const pillar = new THREE.Mesh(
    new THREE.CylinderGeometry(.035,.035,height,10),
    new THREE.MeshBasicMaterial({color:b.color, transparent:true, opacity:.38})
  );
  pillar.position.set(b.x,(minY+maxY)/2,b.z);
  world.add(pillar);
  pillarObjects.push(pillar);

  const layerMeshes = new Map();
  for (const layerId of b.layers) {
    const y = layerMap[layerId].y;
    const group = new THREE.Group();
    group.position.set(b.x,y+.045,b.z);

    const cardWidth = Math.max(1.20, .62 + b.label.length * .48);
    const cardHeight = .64;
    const card = makeFlatText(
      b.label,
      '#fffaf1',
      b.color,
      cardWidth,
      cardHeight,
      'rgba(48,28,24,.98)'
    );
    card.position.y = .008;
    card.userData = {
      nodeId:b.id,
      kind:'bridge',
      layerId,
      cardWidth,
      cardHeight
    };
    group.add(card);

    world.add(group);
    clickables.push(card);
    layerMeshes.set(layerId,{group,card});
  }

  visualNodes.set(b.id,{data:b,pillar,layerMeshes});
}
bridgeData.forEach(createBridge);

function getVisualPosition(id, layerId=null) {
  const v = visualNodes.get(id);
  if (!v) return new THREE.Vector3();
  if (nodeMap[id]) return v.group.position.clone();
  const useLayer = layerId && v.layerMeshes.has(layerId) ? layerId : bridgeMap[id].layers[0];
  return v.layerMeshes.get(useLayer).group.position.clone();
}

function edgeIdFor(a,b,layerId) {
  return `${a}__${b}__${layerId}`;
}

function getCardSize(id, layerId) {
  const visual = visualNodes.get(id);
  if (!visual) return {width:1.2,height:.6};

  if (nodeMap[id]) {
    return {
      width: visual.card.userData.cardWidth || 1.2,
      height: visual.card.userData.cardHeight || .6
    };
  }

  const layerVisual = visual.layerMeshes.get(layerId);
  return {
    width: layerVisual?.card.userData.cardWidth || 1.2,
    height: layerVisual?.card.userData.cardHeight || .64
  };
}

function distanceToCardBoundary(dirX, dirZ, size) {
  const dx = Math.abs(dirX) < 1e-6 ? Infinity : (size.width / 2) / Math.abs(dirX);
  const dz = Math.abs(dirZ) < 1e-6 ? Infinity : (size.height / 2) / Math.abs(dirZ);
  return Math.min(dx,dz);
}

function clippedEdgePoints(a,b,layerId) {
  const pa = getVisualPosition(a,layerId);
  const pb = getVisualPosition(b,layerId);
  pa.y = pb.y = layerMap[layerId].y + .031;

  const dx = pb.x-pa.x;
  const dz = pb.z-pa.z;
  const len = Math.max(.001,Math.hypot(dx,dz));
  const nx = dx/len;
  const nz = dz/len;

  const aOffset = distanceToCardBoundary(nx,nz,getCardSize(a,layerId)) + .05;
  const bOffset = distanceToCardBoundary(nx,nz,getCardSize(b,layerId)) + .05;

  return {
    start:new THREE.Vector3(pa.x+nx*aOffset,pa.y,pa.z+nz*aOffset),
    end:new THREE.Vector3(pb.x-nx*bOffset,pb.y,pb.z-nz*bOffset)
  };
}

function createDirectionArrow(start,end,color) {
  const dir = end.clone().sub(start);
  const len = Math.max(.001,Math.hypot(dir.x,dir.z));
  const nx = dir.x/len;
  const nz = dir.z/len;

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute(
    'position',
    new THREE.Float32BufferAttribute([
      0,0,0,
      -.18,0,.085,
      -.18,0,-.085
    ],3)
  );
  geometry.computeVertexNormals();

  const arrow = new THREE.Mesh(
    geometry,
    new THREE.MeshBasicMaterial({
      color,
      transparent:true,
      opacity:.82,
      side:THREE.DoubleSide,
      depthWrite:false
    })
  );
  arrow.position.copy(end).add(new THREE.Vector3(-nx*.035,.018,-nz*.035));
  arrow.rotation.y = Math.atan2(-nz,nx);
  arrow.renderOrder = 7;
  return arrow;
}

function relationTextWidth(text) {
  return Math.min(2.15, Math.max(.82, .48 + text.length * .18));
}

function createEdgeLine(start,end,color,opacity,kind) {
  const geometry = new LineGeometry();
  geometry.setPositions([
    start.x,start.y,start.z,
    end.x,end.y,end.z
  ]);

  const material = new LineMaterial({
    color,
    transparent:true,
    opacity,
    linewidth:kind === 'bridge' ? 1.15 : 1.45,
    depthWrite:false
  });
  material.resolution.set(
    Math.max(1,root.clientWidth || window.innerWidth),
    Math.max(1,root.clientHeight || window.innerHeight)
  );

  const line = new Line2(geometry,material);
  line.computeLineDistances();
  return line;
}

function syncRelationArrows(record) {
  for (const arrow of record.arrows || []) {
    arrow.visible = false;
    world.remove(arrow);
    arrow.geometry?.dispose?.();
    arrow.material?.dispose?.();
  }

  const {start,end} = clippedEdgePoints(
    record.data.a,
    record.data.b,
    record.data.layerId
  );
  const color = relationTypeColors[record.data.type] || layerMap[record.data.layerId].color;
  const arrows = [];

  const appendArrow = (a,b) => {
    const arrow = createDirectionArrow(a,b,color);
    arrow.userData = {kind:'relation',edgeId:record.id};
    world.add(arrow);
    clickables.push(arrow);
    arrows.push(arrow);
  };

  if (record.data.type === '反馈' || record.data.type === '交换') {
    appendArrow(start,end);
    appendArrow(end,start);
  } else if (record.data.type !== '相关') {
    appendArrow(start,end);
  }

  record.arrows = arrows;
}

function addEdge({a,b,activeFrom,kind='local',layerId,type='因果',mechanismName='作用机制',mechanismDetail=''}) {
  const id = edgeIdFor(a,b,layerId);
  const override = relationOverrides[id] || {};
  const relation = {
    id,a,b,activeFrom,kind,layerId,
    type: override.type || type,
    mechanismName: override.mechanismName || mechanismName,
    mechanismDetail: override.mechanismDetail || mechanismDetail
  };

  const {start,end} = clippedEdgePoints(a,b,layerId);
  const line = createEdgeLine(
    start,
    end,
    layerMap[layerId].color,
    kind==='bridge' ? .36 : .31,
    kind
  );
  line.userData = {kind:'relation',edgeId:id};
  world.add(line);

  const centerA = getVisualPosition(a,layerId);
  const centerB = getVisualPosition(b,layerId);
  const mid = centerA.clone().lerp(centerB,.5);
  const tagWidth = relationTextWidth(relation.mechanismName);
  const tagHeight = .27;
  const tag = makeFlatText(
    relation.mechanismName,
    '#d8e6ef',
    relationTypeColors[relation.type] || '#758b9b',
    tagWidth,
    tagHeight,
    'rgba(4,10,16,.90)'
  );
  tag.position.copy(mid);
  tag.position.y = layerMap[layerId].y + .055;
  tag.userData = {
    kind:'relation',
    edgeId:id,
    tagWidth,
    tagHeight
  };
  world.add(tag);

  const record = {id,line,tag,data:relation,arrows:[]};
  edgeObjects.push(record);
  edgeMap.set(id,record);
  clickables.push(line,tag);
  syncRelationArrows(record);
}

for (const [a,b,activeFrom,type,mechanismName,mechanismDetail] of edgeSeed) {
  addEdge({
    a,b,activeFrom,type,mechanismName,mechanismDetail,
    kind:'local',
    layerId:nodeMap[a].layer
  });
}

for (const b of bridgeData) {
  for (const targetId of b.links) {
    const target = nodeMap[targetId];
    addEdge({
      a:b.id,
      b:targetId,
      activeFrom:Math.max(b.activeFrom,target.activeFrom),
      kind:'bridge',
      layerId:target.layer,
      type:'跨网',
      mechanismName:'跨层连接',
      mechanismDetail:`${b.label}在${layerMap[target.layer].title}中通过“${target.label}”承担这一层的具体作用。`
    });
  }
}

function rectAt(x,z,w,h,pad=.08) {
  return {
    minX:x-w/2-pad,
    maxX:x+w/2+pad,
    minZ:z-h/2-pad,
    maxZ:z+h/2+pad
  };
}

function overlapArea(a,b) {
  const w = Math.max(0,Math.min(a.maxX,b.maxX)-Math.max(a.minX,b.minX));
  const h = Math.max(0,Math.min(a.maxZ,b.maxZ)-Math.max(a.minZ,b.minZ));
  return w*h;
}

function collectLayerObstacles(layerId) {
  const occupied = [];

  for (const [id,v] of visualNodes) {
    if (nodeMap[id] && v.data.layer === layerId) {
      const p = v.group.position;
      occupied.push(rectAt(
        p.x,
        p.z,
        v.card.userData.cardWidth || 1.2,
        v.card.userData.cardHeight || .60,
        .12
      ));
    }

    if (bridgeMap[id] && v.layerMeshes.has(layerId)) {
      const meshSet = v.layerMeshes.get(layerId);
      const p = meshSet.group.position;
      occupied.push(rectAt(
        p.x,
        p.z,
        meshSet.card.userData.cardWidth || 1.2,
        meshSet.card.userData.cardHeight || .64,
        .14
      ));
    }
  }

  return occupied;
}

function layoutRelationTags() {
  for (const layer of layers) {
    const occupied = collectLayerObstacles(layer.id);
    const records = edgeObjects.filter(r => r.data.layerId === layer.id);

    // 先放较长的标签，让长文本优先占据较宽松的位置。
    records.sort((a,b)=>(b.tag.userData.tagWidth || 1)-(a.tag.userData.tagWidth || 1));

    for (const record of records) {
      const e = record.data;
      const pa = getVisualPosition(e.a,e.layerId);
      const pb = getVisualPosition(e.b,e.layerId);
      const dx = pb.x-pa.x;
      const dz = pb.z-pa.z;
      const len = Math.max(.001,Math.hypot(dx,dz));
      const nx = -dz/len;
      const nz = dx/len;
      const w = record.tag.userData.tagWidth || relationTextWidth(e.mechanismName);
      const h = record.tag.userData.tagHeight || .27;

      const tCandidates = [.50,.36,.64,.25,.75,.18,.82];
      const nCandidates = [0,.34,-.34,.58,-.58,.82,-.82];

      let best = null;
      for (const t of tCandidates) {
        const bx = pa.x + dx*t;
        const bz = pa.z + dz*t;

        for (const n of nCandidates) {
          const x = bx + nx*n;
          const z = bz + nz*n;
          const rect = rectAt(x,z,w,h,.10);

          let collision = 0;
          for (const other of occupied) collision += overlapArea(rect,other);

          const edgePenalty = Math.max(0,Math.abs(x)-5.85)*5 + Math.max(0,Math.abs(z)-2.78)*5;
          const movePenalty = Math.abs(t-.5)*.10 + Math.abs(n)*.025;
          const score = collision*140 + edgePenalty + movePenalty;

          if (!best || score < best.score) best = {x,z,rect,score};
        }
      }

      record.tag.position.set(best.x,layer.y+.055,best.z);
      occupied.push(best.rect);
    }
  }
}

layoutRelationTags();

function refreshEdgeVisuals() {
  for (const record of edgeObjects) {
    const {start,end} = clippedEdgePoints(
      record.data.a,
      record.data.b,
      record.data.layerId
    );
    record.line.geometry.setPositions([
      start.x,start.y,start.z,
      end.x,end.y,end.z
    ]);
    record.line.computeLineDistances();
    syncRelationArrows(record);
  }
  layoutRelationTags();
}

function rerunConstrainedLayout() {
  // Use the current visual positions as the next simulation's starting state.
  for (const node of nodeData) {
    const visual = visualNodes.get(node.id);
    if (visual) node.pos = [visual.group.position.x, visual.group.position.z];
  }

  const next = computeConstrainedLayerLayouts({
    layers,
    nodeData,
    bridgeData,
    edgeSeed,
    warmupTicks: 220
  });

  for (const node of nodeData) {
    const p = next[node.id];
    if (!p) continue;
    node.pos = [p.x,p.z];
    const visual = visualNodes.get(node.id);
    if (visual) {
      visual.group.position.x = p.x;
      visual.group.position.z = p.z;
    }
  }

  refreshEdgeVisuals();
  updateVisibility();
}

let currentStep = 3;
let activeLayer = 'all';
let selectedId = null;
let selectedLayerContext = null;
let selectedEdgeId = null;
let autoRotate = false;
let focusMode = false;
let playing = false;
let playTimer = null;

function nodeActive(data) {
  return data.activeFrom <= currentStep;
}

function isRelated(a,b) {
  if (a===b) return true;
  return edgeObjects.some(record => {
    const e = record.data;
    return (e.a===a && e.b===b) || (e.a===b && e.b===a);
  });
}

function updateVisibility() {
  for (const [id,v] of visualNodes) {
    const data = v.data;
    const timeVisible = nodeActive(data);

    if (nodeMap[id]) {
      const layerVisible = activeLayer === 'all' || data.layer === activeLayer;
      const focusVisible = !focusMode || !selectedId || id===selectedId || isRelated(selectedId,id);
      v.group.visible = timeVisible && layerVisible && focusVisible;
      const selected = selectedId === id;
      v.group.scale.setScalar(selected ? 1.18 : 1);
      v.card.material.opacity = selected ? 1 : .97;
    } else {
      for (const [layerId,meshSet] of v.layerMeshes) {
        const layerVisible = activeLayer === 'all' || layerId === activeLayer;
        const focusVisible = !focusMode || !selectedId || id===selectedId || isRelated(selectedId,id);
        meshSet.group.visible = timeVisible && layerVisible && focusVisible;
        const selected = selectedId === id && (!selectedLayerContext || selectedLayerContext===layerId);
        meshSet.group.scale.setScalar(selected ? 1.18 : 1);
      }
      v.pillar.visible = timeVisible && activeLayer === 'all' && document.getElementById('pillarToggle').checked;
    }
  }

  for (const record of edgeObjects) {
    const e = record.data;
    const aData = nodeMap[e.a] || bridgeMap[e.a];
    const bData = nodeMap[e.b] || bridgeMap[e.b];
    const timeVisible = e.activeFrom <= currentStep && nodeActive(aData) && nodeActive(bData);
    const layerVisible = activeLayer === 'all' || e.layerId === activeLayer;
    const focusVisible = !focusMode || !selectedId || e.a===selectedId || e.b===selectedId;
    const visible = timeVisible && layerVisible && focusVisible;

    record.line.visible = visible;
    const edgeSelected = selectedEdgeId===e.id;
    const edgeRelated = e.a===selectedId || e.b===selectedId;
    record.line.material.opacity = edgeSelected ? .96 : (edgeRelated ? .72 : (e.kind==='bridge' ? .38 : .31));
    record.line.material.linewidth = edgeSelected ? 2.35 : (edgeRelated ? 1.85 : (e.kind==='bridge' ? 1.15 : 1.45));

    for (const arrow of record.arrows || []) {
      arrow.visible = visible;
      arrow.material.opacity = edgeSelected ? 1 : (edgeRelated ? .92 : .78);
      arrow.scale.setScalar(edgeSelected ? 1.18 : 1);
    }

    // 机制标签在单层视图中全部显示；总览只显示当前选中的机制，避免信息过载。
    record.tag.visible = visible && (activeLayer !== 'all' || selectedEdgeId===e.id);
    record.tag.scale.setScalar(selectedEdgeId===e.id ? 1.12 : 1);
  }

  for (const [id,o] of layerObjects) {
    o.group.visible = activeLayer === 'all' || activeLayer === id;
  }

  document.getElementById('stageBadgeText').textContent = phases[currentStep].title;
  document.getElementById('timeTitle').textContent = phases[currentStep].title;
  document.getElementById('timeDescription').textContent = phases[currentStep].desc;
  document.querySelectorAll('.time-tick').forEach((el,i)=>el.classList.toggle('active',i<=currentStep));
}

function setLayer(id) {
  activeLayer = id;
  document.querySelectorAll('.layer-btn').forEach(btn => btn.classList.toggle('active', btn.dataset.layer === id));
  updateVisibility();
}

function getFocusObject(id,layerContext=null) {
  const v = visualNodes.get(id);
  if (!v) return null;
  if (nodeMap[id]) return v.group;
  const layerId = layerContext && v.layerMeshes.has(layerContext) ? layerContext : bridgeMap[id].layers[0];
  return v.layerMeshes.get(layerId).group;
}

function focusOnNode(id,layerContext=null,distance=7.5) {
  const obj = getFocusObject(id,layerContext);
  if (!obj) return;
  const target = obj.getWorldPosition(new THREE.Vector3());
  const dir = camera.position.clone().sub(controls.target).normalize();
  dir.y = Math.max(.28,dir.y);
  dir.normalize();
  controls.target.copy(target);
  camera.position.copy(target.clone().add(dir.multiplyScalar(distance)));
}

function showNodeInspector() {
  document.getElementById('inspectorEmpty').classList.add('hidden');
  document.getElementById('relationInspector').classList.add('hidden');
  document.getElementById('inspector').classList.remove('hidden');
}

function showRelationInspector() {
  document.getElementById('inspectorEmpty').classList.add('hidden');
  document.getElementById('inspector').classList.add('hidden');
  document.getElementById('relationInspector').classList.remove('hidden');
}

function selectNode(id,layerContext=null) {
  const data = nodeMap[id] || bridgeMap[id];
  if (!data || !nodeActive(data)) return;

  selectedEdgeId = null;
  selectedId = id;
  selectedLayerContext = layerContext || data.layer || null;

  // 单击网格节点后立即只展示该节点所在的网络层。
  if (data.layer) {
    setLayer(data.layer);
    selectedLayerContext = data.layer;
  } else if (layerContext) {
    setLayer(layerContext);
  }

  renderInspector(data);
  focusOnNode(id,selectedLayerContext,data.layers ? 7.8 : 6.9);
  updateVisibility();
}

function renderInspector(data) {
  showNodeInspector();

  const isBridge = !!data.layers;
  document.getElementById('nodeType').textContent = isBridge ? 'CROSS-LAYER NODE' : 'LOCAL NODE';
  document.getElementById('nodeTitle').textContent = data.label;
  document.getElementById('nodeSubtitle').textContent = isBridge
    ? `跨层节点 · 当前查看：${selectedLayerContext ? layerMap[selectedLayerContext].title : '多层'}`
    : layerMap[data.layer].title;
  document.getElementById('nodeSymbol').textContent = data.symbol || '●';
  document.getElementById('nodeDescription').textContent = data.desc;

  const networks = isBridge ? data.layers : [data.layer];
  document.getElementById('networkTags').innerHTML = networks.map(id =>
    `<span class="network-tag" style="color:${layerMap[id].color}">${layerMap[id].title}</span>`
  ).join('');

  const relations = edgeObjects
    .filter(r => r.data.a===data.id || r.data.b===data.id)
    .filter(r => r.data.activeFrom <= currentStep)
    .filter(r => activeLayer==='all' || r.data.layerId===activeLayer);

  document.getElementById('relationList').innerHTML = relations.length
    ? relations.map(r => {
        const e = r.data;
        const otherId = e.a===data.id ? e.b : e.a;
        const other = nodeMap[otherId] || bridgeMap[otherId];
        return `<button class="relation-item relation-edit-btn" data-edge-id="${e.id}">
          <span><strong>${e.mechanismName}</strong><small>${data.label} ↔ ${other.label}</small></span>
          <span>编辑 ›</span>
        </button>`;
      }).join('')
    : '<div class="relation-item"><span>当前层暂无显式关系</span><span>—</span></div>';

  const cross = isBridge
    ? data.layers
    : bridgeData.filter(b => b.links.includes(data.id)).map(b=>b.id);

  let crossHtml = '';
  if (isBridge) {
    crossHtml = data.layers.map(layerId =>
      `<button class="cross-link" data-layer-jump="${layerId}">进入 ${layerMap[layerId].title}</button>`
    ).join('');
  } else {
    crossHtml = cross.map(bridgeId =>
      `<button class="cross-link" data-node-jump="${bridgeId}">沿“${bridgeMap[bridgeId].label}”跨网</button>`
    ).join('');
  }
  document.getElementById('crossLinks').innerHTML = crossHtml || '<span style="color:#6f889d;font-size:10px">暂未形成跨网连接</span>';
  document.getElementById('nodeTime').textContent = `${phases[data.activeFrom].title} 进入模型`;

  document.querySelectorAll('.relation-edit-btn').forEach(btn => {
    btn.addEventListener('click', () => selectRelation(btn.dataset.edgeId));
  });

  document.querySelectorAll('[data-layer-jump]').forEach(btn => {
    btn.addEventListener('click', () => {
      const layerId = btn.dataset.layerJump;
      selectedLayerContext = layerId;
      setLayer(layerId);
      focusOnNode(data.id,layerId,7.8);
      renderInspector(data);
    });
  });

  document.querySelectorAll('[data-node-jump]').forEach(btn => {
    btn.addEventListener('click', () => {
      const bridgeId = btn.dataset.nodeJump;
      const bridge = bridgeMap[bridgeId];
      if (bridge.activeFrom > currentStep) {
        currentStep = bridge.activeFrom;
        document.getElementById('timeSlider').value = currentStep;
      }
      const context = bridge.layers.includes(activeLayer) ? activeLayer : bridge.layers[0];
      selectNode(bridgeId,context);
    });
  });
}

function selectRelation(edgeId) {
  const record = edgeMap.get(edgeId);
  if (!record) return;
  selectedEdgeId = edgeId;
  const e = record.data;
  selectedId = null;
  selectedLayerContext = e.layerId;
  setLayer(e.layerId);
  showRelationInspector();

  const a = nodeMap[e.a] || bridgeMap[e.a];
  const b = nodeMap[e.b] || bridgeMap[e.b];

  document.getElementById('relationTitle').textContent = `${a.label} ↔ ${b.label}`;
  document.getElementById('relationSubtitle').textContent = `${layerMap[e.layerId].title} · T${e.activeFrom} 起出现`;
  document.getElementById('relationType').value = e.type;
  document.getElementById('mechanismName').value = e.mechanismName;
  document.getElementById('mechanismDetail').value = e.mechanismDetail;
  document.getElementById('relationSaveState').textContent = '';

  const mid = getVisualPosition(e.a,e.layerId).lerp(getVisualPosition(e.b,e.layerId),.5);
  controls.target.copy(mid);
  updateVisibility();
}

function saveCurrentRelation() {
  if (!selectedEdgeId) return;
  const record = edgeMap.get(selectedEdgeId);
  if (!record) return;

  record.data.type = document.getElementById('relationType').value;
  record.data.mechanismName = document.getElementById('mechanismName').value.trim() || '作用机制';
  record.data.mechanismDetail = document.getElementById('mechanismDetail').value.trim();

  relationOverrides[selectedEdgeId] = {
    type:record.data.type,
    mechanismName:record.data.mechanismName,
    mechanismDetail:record.data.mechanismDetail
  };
  localStorage.setItem('world-model-3d:relations',JSON.stringify(relationOverrides));

  const newWidth = relationTextWidth(record.data.mechanismName);
  record.tag.geometry.dispose();
  record.tag.geometry = new THREE.PlaneGeometry(newWidth, .27);
  record.tag.userData.tagWidth = newWidth;
  record.tag.userData.tagHeight = .27;
  updateFlatText(
    record.tag,
    record.data.mechanismName,
    '#d8e6ef',
    relationTypeColors[record.data.type] || '#758b9b',
    'rgba(4,10,16,.90)'
  );

  syncRelationArrows(record);
  layoutRelationTags();

  document.getElementById('relationSaveState').textContent = '已保存到本地，并重新排布标签';
  updateVisibility();
}

function resetInspector() {
  selectedId = null;
  selectedLayerContext = null;
  selectedEdgeId = null;
  document.getElementById('inspectorEmpty').classList.remove('hidden');
  document.getElementById('inspector').classList.add('hidden');
  document.getElementById('relationInspector').classList.add('hidden');
  updateVisibility();
}

function buildLayerUI() {
  const list = document.getElementById('layerList');
  list.innerHTML = `
    <button class="layer-btn active" data-layer="all">
      <span class="layer-color" style="background:#d6f2ff;color:#d6f2ff"></span>
      <span class="layer-meta"><strong>全部网络</strong><span>网络之网整体</span></span>
    </button>
  ` + layers.map(l => `
    <button class="layer-btn" data-layer="${l.id}" style="--layer-color:${l.color}">
      <span class="layer-color" style="background:${l.color};color:${l.color}"></span>
      <span class="layer-meta"><strong>${l.title}</strong><span>${l.subtitle}</span></span>
    </button>
  `).join('');

  document.querySelectorAll('.layer-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      selectedEdgeId = null;
      setLayer(btn.dataset.layer);
      if (selectedId) {
        const data = nodeMap[selectedId] || bridgeMap[selectedId];
        if (data.layers && btn.dataset.layer !== 'all' && data.layers.includes(btn.dataset.layer)) {
          selectedLayerContext = btn.dataset.layer;
          renderInspector(data);
          focusOnNode(selectedId,selectedLayerContext,7.8);
        }
      }
    });
  });
}

function buildTimeUI() {
  document.getElementById('timeTicks').innerHTML = phases.map(p =>
    `<div class="time-tick">${p.title.split(' · ')[0]}</div>`
  ).join('');

  document.getElementById('timeSlider').addEventListener('input', e => {
    currentStep = Number(e.target.value);
    if (selectedId) {
      const d = nodeMap[selectedId] || bridgeMap[selectedId];
      if (d.activeFrom > currentStep) resetInspector();
    }
    if (selectedEdgeId) {
      const edge = edgeMap.get(selectedEdgeId);
      if (edge && edge.data.activeFrom > currentStep) resetInspector();
    }
    updateVisibility();
  });
}

const raycaster = new THREE.Raycaster();
raycaster.params.Line.threshold = .16;
const mouse = new THREE.Vector2();
let downPos = null;

renderer.domElement.addEventListener('pointerdown', e => {
  downPos = [e.clientX,e.clientY];
});

renderer.domElement.addEventListener('pointerup', e => {
  if (!downPos || Math.hypot(e.clientX-downPos[0], e.clientY-downPos[1]) > 5) return;

  const rect = renderer.domElement.getBoundingClientRect();
  mouse.x = ((e.clientX-rect.left)/rect.width)*2-1;
  mouse.y = -((e.clientY-rect.top)/rect.height)*2+1;
  raycaster.setFromCamera(mouse,camera);

  const candidates = clickables.filter(o=>o.visible);
  const hit = raycaster.intersectObjects(candidates,false)[0];

  if (!hit) {
    resetInspector();
    return;
  }

  const ud = hit.object.userData || {};
  if (ud.kind === 'relation' && ud.edgeId) {
    selectRelation(ud.edgeId);
  } else if (ud.nodeId) {
    selectNode(ud.nodeId,ud.layerId || null);
  }
});

document.getElementById('saveRelationBtn').addEventListener('click', saveCurrentRelation);
document.getElementById('cancelRelationBtn').addEventListener('click', () => {
  if (!selectedEdgeId) return resetInspector();
  const record = edgeMap.get(selectedEdgeId);
  const e = record.data;
  const contextNode = nodeMap[e.a] ? e.a : (nodeMap[e.b] ? e.b : null);
  if (contextNode) selectNode(contextNode,e.layerId);
  else resetInspector();
});

document.getElementById('resetBtn').addEventListener('click', () => {
  camera.position.set(11.5,12,14.5);
  controls.target.set(0,0,0);
  activeLayer='all';
  setLayer('all');
  resetInspector();
});

document.getElementById('rotateBtn').addEventListener('click', e => {
  autoRotate = !autoRotate;
  e.currentTarget.classList.toggle('active', autoRotate);
  e.currentTarget.textContent = autoRotate ? '停止旋转' : '自动旋转';
});

document.getElementById('reflowBtn').addEventListener('click', e => {
  rerunConstrainedLayout();
  e.currentTarget.textContent = '已重排';
  e.currentTarget.classList.add('active');
  window.setTimeout(() => {
    e.currentTarget.textContent = '重排布局';
    e.currentTarget.classList.remove('active');
  }, 850);
});

document.getElementById('focusToggle').addEventListener('change', e => {
  focusMode = e.target.checked;
  updateVisibility();
});

document.getElementById('pillarToggle').addEventListener('change', updateVisibility);

document.getElementById('playBtn').addEventListener('click', e => {
  playing = !playing;
  e.currentTarget.textContent = playing ? 'Ⅱ' : '▶';
  if (playing) {
    clearInterval(playTimer);
    playTimer = setInterval(() => {
      currentStep = (currentStep + 1) % phases.length;
      document.getElementById('timeSlider').value = currentStep;
      updateVisibility();
    },1450);
  } else {
    clearInterval(playTimer);
  }
});

document.getElementById('searchInput').addEventListener('keydown', e => {
  if (e.key !== 'Enter') return;
  const q = e.currentTarget.value.trim().toLowerCase();
  if (!q) return;

  const all = [...nodeData,...bridgeData];
  const found = all.find(n =>
    n.label.toLowerCase().includes(q) || n.desc.toLowerCase().includes(q)
  );

  if (!found) {
    e.currentTarget.animate(
      [{transform:'translateX(0)'},{transform:'translateX(-5px)'},{transform:'translateX(5px)'},{transform:'translateX(0)'}],
      {duration:260}
    );
    return;
  }

  if (found.activeFrom > currentStep) {
    currentStep = found.activeFrom;
    document.getElementById('timeSlider').value = currentStep;
  }

  const context = found.layer || (activeLayer!=='all' && found.layers?.includes(activeLayer) ? activeLayer : found.layers?.[0]);
  selectNode(found.id,context);
});

function resize() {
  const w = root.clientWidth;
  const h = root.clientHeight;
  camera.aspect = w/h;
  camera.updateProjectionMatrix();
  renderer.setSize(w,h,false);
  for (const record of edgeObjects) {
    record.line.material.resolution?.set(Math.max(1,w),Math.max(1,h));
  }
}
window.addEventListener('resize',resize);

buildLayerUI();
buildTimeUI();
resize();
updateVisibility();

function dynamicScaleForPosition(position, min=.90, max=1.16) {
  const d = camera.position.distanceTo(position);
  const layerFactor = activeLayer === 'all' ? .92 : 1.04;
  return THREE.MathUtils.clamp((.76 + d*.022) * layerFactor, min, max);
}

function updateDynamicScales() {
  for (const [id,v] of visualNodes) {
    if (nodeMap[id]) {
      if (!v.group.visible) continue;
      const base = dynamicScaleForPosition(v.group.position);
      const selectedBoost = selectedId === id ? 1.12 : 1;
      v.group.scale.setScalar(base * selectedBoost);
    } else {
      for (const [layerId,meshSet] of v.layerMeshes) {
        if (!meshSet.group.visible) continue;
        const base = dynamicScaleForPosition(meshSet.group.position);
        const selectedBoost = selectedId === id && (!selectedLayerContext || selectedLayerContext===layerId) ? 1.12 : 1;
        meshSet.group.scale.setScalar(base * selectedBoost);
      }
    }
  }

  for (const record of edgeObjects) {
    if (!record.tag.visible) continue;
    const base = dynamicScaleForPosition(record.tag.position,.92,1.22);
    record.tag.scale.setScalar(base * (selectedEdgeId===record.id ? 1.10 : 1));
  }
}

function animate() {
  requestAnimationFrame(animate);
  if (autoRotate && activeLayer === 'all') world.rotation.y += .00135;
  stars.rotation.y -= .00012;
  controls.update();
  updateDynamicScales();
  renderer.render(scene,camera);
}
animate();
