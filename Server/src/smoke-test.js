import WebSocket from 'ws';

const url = process.env.TEST_SERVER_URL || 'ws://127.0.0.1:8080';
const first = new WebSocket(url);
let second;
let room;
let firstPlayerX;
let sawTwoPlayers = false;
let sawMovement = false;
let sawBullet = false;
let sequence = 0;
let secondSequence = 1;
let finished = false;
let firstCommandSent = false;
let stopCommandSent = false;
let lastRound = 0;

const timeout = setTimeout(() => finish(new Error('Полный сетевой матч не завершился за 25 секунд')), 25000);

first.on('open', () => first.send(JSON.stringify({ type: 'create', name: 'Alpha', tankType: 0 })));
first.on('message', raw => handle(first, JSON.parse(raw.toString())));
first.on('error', finish);

function handle(socket, message) {
  if (finished) return;
  if (message.type === 'error') return finish(new Error(message.error));
  if (message.type === 'welcome' && socket === first) {
    room = message.room;
    if (!/^[A-Z2-9]{6}$/.test(room)) return finish(new Error('Неверный код комнаты'));
    if (!Array.isArray(message.walls) || message.walls.length < 40) return finish(new Error('Уровень не сгенерирован'));
    second = new WebSocket(url);
    second.on('open', () => second.send(JSON.stringify({ type: 'join', room, name: 'Bravo', tankType: 2 })));
    second.on('message', raw => handle(second, JSON.parse(raw.toString())));
    second.on('error', finish);
    return;
  }

  if (message.type === 'welcome' && socket === second) {
    second.send(JSON.stringify({ type: 'input', sequence: 1, move: 0, turn: 0, fire: true }));
    return;
  }

  if (socket !== first || message.type !== 'snapshot' || message.players?.length !== 2) return;
  if (message.phase === 'finished') {
    if (Math.max(message.scoreA, message.scoreB) !== 3) return finish(new Error('Матч завершён не на трёх очках'));
    if (![0, 1].includes(message.winner)) return finish(new Error('Сервер не назначил победителя'));
    if (message.statistics?.length !== 2 || message.statistics.some(value => value.shots < 1))
      return finish(new Error('Итоговая статистика матча неполна'));
    return finish();
  }
  if (message.phase !== 'playing') return;

  sawTwoPlayers = true;
  const player = message.players.find(value => value.id === 0);
  if (firstPlayerX === undefined) firstPlayerX = player.x;
  if (Math.abs(player.x - firstPlayerX) > 0.08) sawMovement = true;
  if (message.bullets?.length > 0) sawBullet = true;

  if (!firstCommandSent) {
    firstCommandSent = true;
    first.send(JSON.stringify({ type: 'input', sequence: ++sequence, move: 1, turn: 0, fire: true }));
  } else if (sawMovement && !stopCommandSent) {
    stopCommandSent = true;
    first.send(JSON.stringify({ type: 'input', sequence: ++sequence, move: 0, turn: 0, fire: true }));
  }

  if (message.round !== lastRound) {
    lastRound = message.round;
    if (lastRound > 1) {
      first.send(JSON.stringify({ type: 'input', sequence: ++sequence, move: 0, turn: 0, fire: true }));
      second.send(JSON.stringify({ type: 'input', sequence: ++secondSequence, move: 0, turn: 0, fire: true }));
    }
  }
}

function finish(error) {
  if (finished) return;
  finished = true;
  clearTimeout(timeout);
  if (first.readyState < WebSocket.CLOSING) first.close();
  if (second && second.readyState < WebSocket.CLOSING) second.close();
  if (error) {
    console.error(error.message || error);
    process.exitCode = 1;
  } else {
    console.log(`OK: комната ${room}, полный матч до 3 очков, движение, выстрелы и статистика синхронизированы`);
  }
}
