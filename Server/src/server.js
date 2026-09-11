import crypto from 'node:crypto';
import http from 'node:http';
import { WebSocketServer, WebSocket } from 'ws';

const PORT = Number(process.env.PORT || 8080);
const TICK_RATE = 60;
const SNAPSHOT_RATE = 20;
const MAX_MESSAGE_BYTES = 4096;
const BULLET_MUZZLE_OFFSET = 1.65;
const ROOM_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const allowedOrigins = (process.env.ALLOWED_ORIGINS || '')
  .split(',').map(value => value.trim()).filter(Boolean);

const tankSpecs = [
  { speed: 3.6, turn: 95, damage: 55, bullet: 9, reload: 0.95, health: 150 },
  { speed: 4.8, turn: 120, damage: 40, bullet: 12, reload: 0.70, health: 110 },
  { speed: 6.3, turn: 145, damage: 25, bullet: 16, reload: 0.45, health: 80 }
];

const rooms = new Map();
let nextBulletId = 1;

const server = http.createServer((request, response) => {
  response.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
  response.end(JSON.stringify({ service: 'Tank Warfare', rooms: rooms.size, status: 'ok' }));
});

const sockets = new WebSocketServer({
  server,
  maxPayload: MAX_MESSAGE_BYTES,
  verifyClient: ({ origin }) => allowedOrigins.length === 0 || !origin || allowedOrigins.includes(origin)
});

sockets.on('connection', socket => {
  socket.meta = { room: null, playerId: -1, messages: 0, windowAt: Date.now() };
  socket.on('message', raw => handleMessage(socket, raw));
  socket.on('close', () => removeSocket(socket));
  socket.on('error', () => removeSocket(socket));
});

function handleMessage(socket, raw) {
  if (!rateLimit(socket)) return closeWithError(socket, 'Слишком много сообщений');

  let message;
  try { message = JSON.parse(raw.toString()); }
  catch { return sendError(socket, 'Некорректный JSON'); }

  if (!message || typeof message.type !== 'string') return;
  if (message.type === 'create') return createRoom(socket, message);
  if (message.type === 'join') return joinRoom(socket, message);
  if (message.type === 'input') return applyInput(socket, message);
}

function createRoom(socket, message) {
  if (socket.meta.room) return;
  let code;
  do { code = randomCode(); } while (rooms.has(code));

  const seed = crypto.randomInt(1, 0x7fffffff);
  const room = {
    code,
    seed,
    matchId: crypto.randomUUID(),
    phase: 'waiting',
    winner: -1,
    score: [0, 0],
    round: 1,
    resetAt: 0,
    players: [],
    bullets: [],
    walls: generateLevel(seed),
    snapshotAccumulator: 0,
    lastUpdate: performance.now()
  };
  rooms.set(code, room);
  addPlayer(room, socket, message);
}

function joinRoom(socket, message) {
  if (socket.meta.room) return;
  const code = String(message.room || '').trim().toUpperCase();
  const room = rooms.get(code);
  if (!room) return sendError(socket, 'Комната не найдена');
  if (room.players.length >= 2 || room.phase !== 'waiting') return sendError(socket, 'Комната уже заполнена');

  addPlayer(room, socket, message);
  room.phase = 'playing';
  respawnPlayers(room);
  broadcastSnapshot(room);
}

function addPlayer(room, socket, message) {
  const id = room.players.length;
  const tankType = clampInteger(message.tankType, 0, 2, 1);
  const spec = tankSpecs[tankType];
  const spawn = spawnFor(id);
  const player = {
    id,
    socket,
    name: sanitizeName(message.name),
    tankType,
    x: spawn.x,
    z: spawn.z,
    yaw: spawn.yaw,
    health: spec.health,
    alive: true,
    input: { move: 0, turn: 0, fire: false, sequence: 0 },
    nextShotAt: 0,
    stats: { playerId: id, shots: 0, meters: 0, walls: 0 }
  };
  room.players.push(player);
  socket.meta.room = room.code;
  socket.meta.playerId = id;
  send(socket, {
    type: 'welcome', playerId: id, room: room.code, seed: room.seed,
    matchId: room.matchId, walls: publicWalls(room.walls)
  });
  broadcastSnapshot(room);
}

function applyInput(socket, message) {
  const room = rooms.get(socket.meta.room);
  if (!room || room.phase !== 'playing') return;
  const player = room.players[socket.meta.playerId];
  if (!player || player.socket !== socket) return;

  const sequence = clampInteger(message.sequence, 0, 0x7fffffff, 0);
  if (sequence <= player.input.sequence) return;
  player.input = {
    sequence,
    move: clampNumber(message.move, -1, 1, 0),
    turn: clampNumber(message.turn, -1, 1, 0),
    fire: message.fire === true
  };
}

function tick() {
  const now = performance.now();
  for (const room of rooms.values()) {
    const delta = Math.min(0.05, Math.max(0, (now - room.lastUpdate) / 1000));
    room.lastUpdate = now;
    if (room.phase === 'playing') simulateRoom(room, delta, now / 1000);
    room.snapshotAccumulator += delta;
    if (room.snapshotAccumulator >= 1 / SNAPSHOT_RATE) {
      room.snapshotAccumulator = 0;
      broadcastSnapshot(room);
    }
  }
}

function simulateRoom(room, delta, nowSeconds) {
  if (room.resetAt > 0) {
    if (nowSeconds >= room.resetAt) {
      room.resetAt = 0;
      room.round++;
      respawnPlayers(room);
    }
    return;
  }

  for (const player of room.players) {
    if (!player.alive) continue;
    const spec = tankSpecs[player.tankType];
    player.yaw = normalizeAngle(player.yaw + player.input.turn * spec.turn * delta);
    const radians = player.yaw * Math.PI / 180;
    const distance = player.input.move * spec.speed * delta;
    const previousX = player.x;
    const previousZ = player.z;
    const targetX = player.x + Math.sin(radians) * distance;
    const targetZ = player.z + Math.cos(radians) * distance;
    if (canTankOccupy(room, player, targetX, player.z)) player.x = targetX;
    if (canTankOccupy(room, player, player.x, targetZ)) player.z = targetZ;
    player.stats.meters += Math.hypot(player.x - previousX, player.z - previousZ);

    if (player.input.fire && nowSeconds >= player.nextShotAt) {
      player.nextShotAt = nowSeconds + spec.reload;
      player.stats.shots++;
      room.bullets.push({
        id: nextBulletId++, owner: player.id,
        x: player.x + Math.sin(radians) * BULLET_MUZZLE_OFFSET,
        z: player.z + Math.cos(radians) * BULLET_MUZZLE_OFFSET,
        yaw: player.yaw, damage: spec.damage, speed: spec.bullet, life: 3
      });
    }
  }

  for (let index = room.bullets.length - 1; index >= 0; index--) {
    const bullet = room.bullets[index];
    const radians = bullet.yaw * Math.PI / 180;
    bullet.x += Math.sin(radians) * bullet.speed * delta;
    bullet.z += Math.cos(radians) * bullet.speed * delta;
    bullet.life -= delta;
    let remove = bullet.life <= 0 || Math.abs(bullet.x) > 14 || Math.abs(bullet.z) > 10;

    if (!remove) {
      const wall = room.walls.find(value => value.health > 0 && pointInWall(bullet.x, bullet.z, value));
      if (wall) {
        remove = true;
        if (wall.destructible) {
          wall.health = Math.max(0, wall.health - bullet.damage);
          if (wall.health === 0) room.players[bullet.owner].stats.walls++;
        }
      }
    }

    if (!remove) {
      const victim = room.players.find(player => player.id !== bullet.owner && player.alive &&
        squaredDistance(player.x, player.z, bullet.x, bullet.z) <= 0.72 * 0.72);
      if (victim) {
        victim.health = Math.max(0, victim.health - bullet.damage);
        remove = true;
        if (victim.health === 0) finishRound(room, bullet.owner, nowSeconds);
      }
    }

    if (remove) room.bullets.splice(index, 1);
  }
}

function finishRound(room, scorerId, nowSeconds) {
  const victim = room.players[1 - scorerId];
  victim.alive = false;
  room.bullets.length = 0;
  room.score[scorerId]++;
  if (room.score[scorerId] >= 3) {
    room.phase = 'finished';
    room.winner = scorerId;
    broadcastSnapshot(room);
  } else {
    room.resetAt = nowSeconds + 1.5;
  }
}

function respawnPlayers(room) {
  room.bullets.length = 0;
  for (const player of room.players) {
    const spawn = spawnFor(player.id);
    const spec = tankSpecs[player.tankType];
    Object.assign(player, { x: spawn.x, z: spawn.z, yaw: spawn.yaw, health: spec.health, alive: true });
    player.input = { move: 0, turn: 0, fire: false, sequence: player.input.sequence };
    player.nextShotAt = 0;
  }
}

function canTankOccupy(room, moving, x, z) {
  if (Math.abs(x) > 11.9 || Math.abs(z) > 7.9) return false;
  for (const wall of room.walls) {
    if (wall.health <= 0) continue;
    if (circleIntersectsBox(x, z, 0.68, wall.x, wall.z, 0.675)) return false;
  }
  return !room.players.some(other => other !== moving && other.alive &&
    squaredDistance(x, z, other.x, other.z) < 1.45 * 1.45);
}

function generateLevel(seed) {
  const random = mulberry32(seed);
  const walls = [];
  let id = 1;
  const add = (x, z, destructible) => walls.push({ id: id++, x, z, health: destructible ? 100 : 99999, destructible });

  for (let x = -12; x <= 12; x += 1.5) {
    add(x, -8.7, false);
    add(x, 8.7, false);
  }
  for (let z = -7.5; z <= 7.5; z += 1.5) {
    add(-12.75, z, false);
    add(12.75, z, false);
  }

  const occupied = new Set();
  for (let attempt = 0; attempt < 24; attempt++) {
    const x = Math.round((1.8 + random() * 7.8) / 1.5) * 1.5;
    const z = Math.round((-6 + random() * 12) / 1.5) * 1.5;
    if (Math.abs(z) < 2.1 && x > 7.5) continue;
    // The central firing lane must never be permanently blocked by an indestructible cube.
    const destructible = Math.abs(z) < 1.1 || random() > 0.20;
    for (const mirroredX of [x, -x]) {
      const key = `${mirroredX}:${z}`;
      if (!occupied.has(key)) {
        occupied.add(key);
        add(mirroredX, z, destructible);
      }
    }
  }
  add(0, -3, true); add(0, 0, true); add(0, 3, true);
  return walls;
}

function broadcastSnapshot(room) {
  const message = {
    type: 'snapshot', phase: room.phase, matchId: room.matchId,
    winner: room.winner, scoreA: room.score[0], scoreB: room.score[1], round: room.round,
    players: room.players.map(player => ({
      id: player.id, name: player.name, tankType: player.tankType,
      x: round(player.x), z: round(player.z), yaw: round(player.yaw),
      health: player.health, maxHealth: tankSpecs[player.tankType].health, alive: player.alive
    })),
    bullets: room.bullets.map(bullet => ({
      id: bullet.id, owner: bullet.owner, x: round(bullet.x), z: round(bullet.z),
      yaw: round(bullet.yaw), speed: bullet.speed
    })),
    walls: publicWalls(room.walls),
    statistics: room.phase === 'finished' ? room.players.map(player => ({ ...player.stats, meters: round(player.stats.meters) })) : []
  };
  for (const player of room.players) send(player.socket, message);
}

function removeSocket(socket) {
  const code = socket.meta?.room;
  if (!code) return;
  const room = rooms.get(code);
  if (!room) return;
  rooms.delete(code);
  for (const player of room.players) {
    if (player.socket !== socket && player.socket.readyState === WebSocket.OPEN) {
      sendError(player.socket, 'Второй игрок отключился');
      player.socket.close(1000, 'opponent left');
    }
    player.socket.meta.room = null;
  }
}

function send(socket, value) {
  if (socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(value));
}
function sendError(socket, error) { send(socket, { type: 'error', error }); }
function closeWithError(socket, error) { sendError(socket, error); socket.close(1008, error); }
function publicWalls(walls) { return walls.map(({ id, x, z, health, destructible }) => ({ id, x, z, health, destructible })); }
function spawnFor(id) { return id === 0 ? { x: -9.6, z: 0, yaw: 90 } : { x: 9.6, z: 0, yaw: 270 }; }
function pointInWall(x, z, wall) { return Math.abs(x - wall.x) <= 0.72 && Math.abs(z - wall.z) <= 0.72; }
function circleIntersectsBox(cx, cz, radius, bx, bz, half) {
  const nearestX = Math.max(bx - half, Math.min(cx, bx + half));
  const nearestZ = Math.max(bz - half, Math.min(cz, bz + half));
  return squaredDistance(cx, cz, nearestX, nearestZ) < radius * radius;
}
function squaredDistance(ax, az, bx, bz) { return (ax - bx) ** 2 + (az - bz) ** 2; }
function normalizeAngle(value) { return ((value % 360) + 360) % 360; }
function round(value) { return Math.round(value * 1000) / 1000; }
function clampNumber(value, min, max, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? Math.max(min, Math.min(max, number)) : fallback;
}
function clampInteger(value, min, max, fallback) { return Math.trunc(clampNumber(value, min, max, fallback)); }
function sanitizeName(value) {
  const clean = String(value || '').replace(/[<>\u0000-\u001f]/g, '').trim().slice(0, 18);
  return clean || 'Игрок';
}
function randomCode() {
  let result = '';
  for (let index = 0; index < 6; index++) result += ROOM_ALPHABET[crypto.randomInt(ROOM_ALPHABET.length)];
  return result;
}
function rateLimit(socket) {
  const now = Date.now();
  if (now - socket.meta.windowAt >= 1000) {
    socket.meta.windowAt = now;
    socket.meta.messages = 0;
  }
  socket.meta.messages++;
  return socket.meta.messages <= 35;
}
function mulberry32(seed) {
  return function () {
    seed |= 0; seed = seed + 0x6D2B79F5 | 0;
    let value = Math.imul(seed ^ seed >>> 15, 1 | seed);
    value = value + Math.imul(value ^ value >>> 7, 61 | value) ^ value;
    return ((value ^ value >>> 14) >>> 0) / 4294967296;
  };
}

setInterval(tick, 1000 / TICK_RATE);
server.listen(PORT, '0.0.0.0', () => console.log(`Tank Warfare server listening on :${PORT}`));
