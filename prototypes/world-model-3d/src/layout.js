import {
  forceSimulation,
  forceLink,
  forceManyBody,
  forceCollide,
  forceX,
  forceY
} from 'd3-force-3d';

function localCardSize(label) {
  return {
    width: Math.max(1.18, 0.58 + label.length * 0.46),
    height: 0.60
  };
}

function bridgeCardSize(label) {
  return {
    width: Math.max(1.20, 0.62 + label.length * 0.48),
    height: 0.64
  };
}

function collisionRadius(size) {
  // d3's collide force is circular. Use the half diagonal plus breathing room
  // so rectangular concept cards do not visually touch.
  return Math.hypot(size.width / 2, size.height / 2) + 0.20;
}

function boundsForce(nodes, xLimit = 5.35, yLimit = 2.45) {
  let boundNodes = nodes;

  function force() {
    for (const node of boundNodes) {
      if (node.fx != null || node.fy != null) continue;

      const r = node.radius || 0.5;
      const xMin = -xLimit + r;
      const xMax = xLimit - r;
      const yMin = -yLimit + r;
      const yMax = yLimit - r;

      if (node.x < xMin) {
        node.x = xMin;
        node.vx = Math.abs(node.vx || 0) * 0.25;
      } else if (node.x > xMax) {
        node.x = xMax;
        node.vx = -Math.abs(node.vx || 0) * 0.25;
      }

      if (node.y < yMin) {
        node.y = yMin;
        node.vy = Math.abs(node.vy || 0) * 0.25;
      } else if (node.y > yMax) {
        node.y = yMax;
        node.vy = -Math.abs(node.vy || 0) * 0.25;
      }
    }
  }

  force.initialize = nextNodes => {
    boundNodes = nextNodes;
  };

  return force;
}

function buildLayerLinks(layerId, nodeData, bridgeData, edgeSeed) {
  const localNodeMap = Object.fromEntries(nodeData.map(n => [n.id, n]));
  const links = [];

  for (const [a, b] of edgeSeed) {
    const source = localNodeMap[a];
    if (source?.layer === layerId) links.push({ source: a, target: b, kind: 'local' });
  }

  for (const bridge of bridgeData) {
    if (!bridge.layers.includes(layerId)) continue;
    for (const targetId of bridge.links) {
      const target = localNodeMap[targetId];
      if (target?.layer === layerId) {
        links.push({ source: bridge.id, target: targetId, kind: 'bridge' });
      }
    }
  }

  return links;
}

/**
 * Constrained multilayer layout.
 *
 * Inspired by the pluggable force composition used by 3d-force-graph, but
 * intentionally keeps each semantic network on its own X/Z plane.
 * Cross-layer nodes are fixed anchors so their vertical pillars remain aligned.
 */
export function computeConstrainedLayerLayouts({
  layers,
  nodeData,
  bridgeData,
  edgeSeed,
  warmupTicks = 260
}) {
  const result = {};

  for (const layer of layers) {
    const locals = nodeData.filter(n => n.layer === layer.id);
    const bridges = bridgeData.filter(b => b.layers.includes(layer.id));

    const simNodes = [
      ...locals.map(n => {
        const size = localCardSize(n.label);
        return {
          id: n.id,
          x: n.pos[0],
          y: n.pos[1],
          radius: collisionRadius(size),
          fixed: false
        };
      }),
      ...bridges.map(b => {
        const size = bridgeCardSize(b.label);
        return {
          id: b.id,
          x: b.x,
          y: b.z,
          fx: b.x,
          fy: b.z,
          radius: collisionRadius(size),
          fixed: true
        };
      })
    ];

    if (!simNodes.length) continue;

    const links = buildLayerLinks(layer.id, nodeData, bridgeData, edgeSeed);
    const simulation = forceSimulation(simNodes, 2)
      .stop()
      .alpha(1)
      .alphaDecay(0.025)
      .velocityDecay(0.34)
      .force(
        'link',
        forceLink(links)
          .id(d => d.id)
          .distance(link => {
            const s = typeof link.source === 'object' ? link.source : null;
            const t = typeof link.target === 'object' ? link.target : null;
            const padding = (s?.radius || 0.7) + (t?.radius || 0.7);
            return padding + (link.kind === 'bridge' ? 1.05 : 1.45);
          })
          .strength(link => link.kind === 'bridge' ? 0.22 : 0.34)
      )
      .force(
        'charge',
        forceManyBody()
          .strength(d => d.fixed ? -18 : -95)
          .distanceMax(7.5)
      )
      .force(
        'collide',
        forceCollide()
          .radius(d => d.radius)
          .strength(0.92)
          .iterations(4)
      )
      .force('x', forceX(0).strength(d => d.fixed ? 0 : 0.035))
      .force('y', forceY(0).strength(d => d.fixed ? 0 : 0.045))
      .force('bounds', boundsForce(simNodes));

    simulation.tick(warmupTicks);

    for (const node of simNodes) {
      if (!node.fixed) {
        result[node.id] = { x: node.x, z: node.y, layerId: layer.id };
      }
    }
  }

  return result;
}
