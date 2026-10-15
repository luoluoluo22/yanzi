# 上游项目源码研究与移植记录

## 研究对象

### Arena3D
- 仓库：Pavlopoulos-Lab/Arena3D
- 技术栈：Vite + TypeScript + Three.js；后端 FastAPI + python-igraph
- 许可证：AGPL-3.0
- 本项目策略：只参考架构/交互思想，不复制其源码实现。

值得参考的实现：
1. Layer / Node / Edge 分离，层内关系与跨层关系分开管理。
2. Layer 作为图形容器，节点与层内边随层一起变换。
3. Edge 使用 Three.js Line2 / LineMaterial，避免普通 WebGL Line 始终只有 1px 的问题。
4. 关系可带方向箭头、权重映射到线宽/透明度。
5. 多种图布局算法通过统一接口切换。
6. minimap 使用世界坐标投影实现全局导航。
7. 选择性 bloom：节点强调，边与层保持清晰。

### 3d-force-graph
- 仓库：vasturiano/3d-force-graph
- 许可证：MIT
- 本项目重点借鉴：可组合的 d3-force-3d 力系统。

值得参考的实现：
1. link + charge + collision + center/position forces 可组合。
2. forceCollide 解决节点重叠。
3. link.distance 根据关系类型动态设置。
4. warmup / tick 静态预计算，避免首屏抖动。
5. 图结构或参数变化后可 reheat / 重新布局。
6. 自定义 Three.js node object，而不是固定球形节点。

## 已移植到 world-model-3d 的部分

### 1. 受约束的多层力导向布局
新增 `src/layout.js`，使用 `d3-force-3d`。

原则：
- 每个语义网络独立运行二维力模拟。
- 节点只允许在自己的 X/Z 平面移动。
- 跨层节点使用 fx/fy 固定为锚点，保证纵向支柱始终对齐。
- 普通节点使用 charge 排斥、link 拉力、collide 碰撞、x/y 弱中心力。
- collision 半径按节点卡片对角线计算，因此节点名称越长，自动占用越大空间。
- 增加 bounds force，防止节点逃出层平面。

### 2. 重新布局 / reheat 思路
顶部增加“重排布局”按钮。
- 图结构或关系变化后可以重新运行力布局。
- 当前视觉坐标会作为下一轮模拟的初始位置。

### 3. 稳定线宽
普通 THREE.Line 替换为 Line2 + LineMaterial。
- 关系线不再受 WebGL 1px 限制。
- 选中关系时线宽和透明度提高。
- resize 时同步 LineMaterial resolution。

### 4. 方向表达
根据关系类型显示箭头：
- 因果 / 约束 / 代理 / 跨网：单向箭头。
- 反馈 / 交换：双向箭头。
- 相关：无方向箭头。

### 5. 线段裁剪
关系线不再从节点中心穿入节点。
- 根据节点卡片的矩形边界计算交点。
- 连线从节点边缘开始，到另一个节点边缘结束。

## 下一阶段建议

1. 把 Layer 真正改成父容器：层内节点、层内关系作为 LayerGroup 子对象。
2. 增加 minimap / navigator，复杂网络时快速定位。
3. 增加节点拖拽，拖动后固定该节点并只 reheat 当前层。
4. 把关系强度、证据可信度映射到线宽 / 透明度。
5. 时间轴改为真实事件流：关系出现、增强、减弱、失效。
6. 大规模图时将静态 force tick 放进 Web Worker。
