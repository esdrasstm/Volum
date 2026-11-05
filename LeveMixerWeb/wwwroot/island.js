/* =========================================================
   Volum — ilha (island.html). Textos e formatação vêm do shared.js (window.Volum).
   Fala com o C# (IslandForm.cs) por mensagens JSON:
     C# -> JS : settings | mode | media | water | battery | notice
     JS -> C# : ready | height | hover | playPause | next | prev | openMixer | waterAdd | noticeAction | noticeClose
   ========================================================= */
(() => {
  'use strict';

  const V = window.Volum;
  const $ = (id) => document.getElementById(id);
  const post = (msg) => window.chrome.webview.postMessage(msg);
  const island = $('island');

  let lang = 'pt';
  const t = (key) => V.t(lang, key);
  let mode = 'closed';
  let media = null;
  let water = { enabled: false, goalMl: 2000, cupMl: 250, todayMl: 0 };
  let battery = { present: false, percent: 100, charging: false };
  let soundOn = false;   // tem som saindo agora (o C# lê o medidor do Windows)

  /* ---------------- Hora e data (atualiza só na virada do minuto) ---------------- */
  // Data curta sem pontos, como "qua 4 nov 2026"
  function shortDate(d) {
    const parts = new Intl.DateTimeFormat(V.langTag(lang), { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })
      .formatToParts(d).filter((p) => p.type !== 'literal').map((p) => p.value.replace('.', ''));
    return parts.join(' ');
  }

  let clockTimer = 0;
  function tickClock() {
    clearTimeout(clockTimer);
    const now = new Date();
    const time = now.toLocaleTimeString(V.langTag(lang), { hour: '2-digit', minute: '2-digit' });
    $('clock').textContent = time;
    $('time').textContent = time;
    $('date').textContent = shortDate(now);
    clockTimer = setTimeout(tickClock, (60 - now.getSeconds()) * 1000 + 50);
  }

  /* ---------------- Bateria ---------------- */
  function batterySvg() {
    const low = battery.percent <= 20 && !battery.charging;
    const color = low ? 'var(--battery-low)' : 'var(--battery)';
    const c = 2 * Math.PI * 8;
    const bolt = battery.charging
      ? '<path d="M10.8 5.5 7.6 10.6h2.6l-1 3.9 3.2-5.1h-2.6z" fill="' + color + '"/>'
      : '<text x="10" y="13" text-anchor="middle" font-size="7" font-weight="700" fill="' + color + '">' + battery.percent + '</text>';
    return '<svg viewBox="0 0 20 20"><circle cx="10" cy="10" r="8" fill="none" stroke="rgba(255,255,255,.18)" stroke-width="2"/>' +
      '<circle cx="10" cy="10" r="8" fill="none" stroke="' + color + '" stroke-width="2" stroke-linecap="round" ' +
      'stroke-dasharray="' + (c * battery.percent / 100) + ' ' + c + '" transform="rotate(-90 10 10)"/>' + bolt + '</svg>';
  }

  function showBattery() {
    const svg = batterySvg();   // feito aqui só com números: nada de fora entra no HTML
    for (const el of [$('battery'), $('miniBattery')]) el.innerHTML = svg;
    $('battery').hidden = !battery.present;
    showMini();
  }

  // Fechada, à esquerda: o anel do Pomodoro (rodando), a capa (com música), o mascote (ilha ociosa) ou a bateria
  function showMini() {
    const ring = pomoActive();
    const pet = !ring && idle;   // ociosa: só o mascote (a capa seria de uma música parada)
    const cover = !ring && !pet && !!(media && media.cover);
    $('miniRing').toggleAttribute('hidden', !ring);   // é um <svg>: a propriedade .hidden não vale nele
    $('miniCover').hidden = !cover;
    $('miniMascot').hidden = !pet;
    $('miniBattery').hidden = ring || cover || pet || !battery.present;
  }

  /* ---------------- Mascote ----------------
     O desenho do Esdras (rosto branco com os olhos em degradê, sem os cílios). Pisca sozinho (CSS);
     ao dar oi quando o PC liga, também pula e balança. */
  const MASCOT =
    '<svg class="mascot" viewBox="8 92 441 272" aria-hidden="true">' +
    '<rect class="m-face" x="12.26" y="97.78" width="432.3" height="261.24" rx="35.2" fill="#f9f9f9"/>' +
    '<g class="m-eyes">' +
      // Olhos normais (piscam) e olhos felizes em arquinho (^ ^): o CSS alterna entre os dois
      '<g class="m-normal">' +
      '<rect class="m-eye" x="80.94" y="149.37" width="49.87" height="111.03" rx="13.53" fill="url(#mEyeL)"/>' +
      '<rect class="m-eye" x="318.05" y="149.37" width="49.87" height="111.03" rx="13.53" fill="url(#mEyeR)"/></g>' +
      '<g class="m-happy"><path d="M78 222Q105.9 166 133.8 222"/><path d="M315 222Q343 166 371 222"/></g>' +
    '</g></svg>';
  /* ---------------- Pixel art (estilo 16 bits) ----------------
     Os desenhos são mapas de caracteres: cada letra é um pixel de uma cor da paleta e "." é vazio.
     Viram <rect> num SVG com bordas retas (crispEdges); a animação troca quadros inteiros (steps),
     como nos jogos antigos. PIXEL_ART = false volta para o mascote vetorial acima. */
  const PIXEL_ART = true;

  // Junta pixels vizinhos da mesma cor numa linha só (menos <rect> na página)
  function pixels(rows, pal, ox = 0, oy = 0) {
    let out = '';
    rows.forEach((row, y) => {
      for (let x = 0; x < row.length;) {
        const ch = row[x];
        if (ch === '.' || !pal[ch]) { x++; continue; }
        let w = 1;
        while (row[x + w] === ch) w++;
        out += `<rect x="${ox + x}" y="${oy + y}" width="${w}" height="1" fill="${pal[ch]}"/>`;
        x += w;
      }
    });
    return out;
  }

  // Mascote: rosto branco (sombra embaixo) e os olhos em "degradê" de 4 tons, do azul-escuro ao claro
  const M_PAL = { W: '#f9f9f9', S: '#c9ced6', a: '#002e5f', b: '#0d4077', c: '#3d6a99', d: '#a9c0d9' };
  const M_FACE = [
    '..WWWWWWWWWWWW..',
    '.WWWWWWWWWWWWWW.',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'WWWWWWWWWWWWWWWW',
    'SWWWWWWWWWWWWWWS',
    '.SSSSSSSSSSSSSS.',
  ];
  // Só os olhos mudam de um quadro para outro (linhas 2 a 5 do rosto)
  const M_EYES = {
    open:  ['...aa......aa...', '...bb......bb...', '...cc......cc...', '...dd......dd...'],
    half:  ['................', '...bb......bb...', '...cc......cc...', '...dd......dd...'],   // pálpebra descendo
    blink: ['................', '................', '................', '...bb......bb...'],
    happy: ['................', '...aa......aa...', '..a..a....a..a..', '................'],
    left:  ['..aa......aa....', '..bb......bb....', '..cc......cc....', '..dd......dd....'],
    right: ['....aa......aa..', '....bb......bb..', '....cc......cc..', '....dd......dd..'],
  };
  const MASCOT_PX =
    '<svg class="mascot px" viewBox="0 -1 16 11" aria-hidden="true">' +
    '<g class="px-body">' + pixels(M_FACE, M_PAL) +
    Object.entries(M_EYES).map(([k, rows]) => `<g class="pe pe-${k}">${pixels(rows, M_PAL, 0, 2)}</g>`).join('') +
    '</g></svg>';

  // Cena do aviso de levantar: mesa com monitor, cadeira e o bonequinho (cabelo, pele, camiseta, calça, tênis)
  const W_PAL = { H: '#5a3a28', S: '#f1c58f', E: '#1d1d1d', T: '#4f86c6', t: '#3a6aa3', P: '#3b3f58', K: '#8d8d99',
                  M: '#9aa3ad', B: '#5fa8e8', D: '#a0703f', d: '#7a5230', C: '#6c6c78', Z: '#ffffff',
                  Y: '#ffd34d', y: '#f2a93b', G: '#4caf50', g: '#2e7d32' };
  const HEAD_R = ['...HHHH...', '..HHHHHH..', '..HSSSSH..', '..SSSSES..', '..SSSSSS..', '...SSSS...'];   // olhando para a direita
  const HEAD_L = ['...HHHH...', '..HHHHHH..', '..HSSSSH..', '..SESSSS..', '..SSSSSS..', '...SSSS...'];   // para a esquerda
  const HEAD_Y = ['...HHHH...', '..HHHHHH..', '..HSSSSH..', '..SEESSE..', '..SSEESS..', '...SSSS...'];   // bocejando
  const W_FIG = {
    // Sentado olhando a mesa (à esquerda), mãos no teclado (dois quadros: digitando)
    sit1: [...HEAD_L, '..TTTTTT..', 'STTTTTTT..', '.tTTTTTT..', '..TTTTTT..', '..TTTTTT..', 'PPPPPPPP..', 'PPPPPPPP..', 'PP........', 'PP........', 'KKK.......'],
    sit2: [...HEAD_L, '..TTTTTT..', '.tTTTTTT..', 'STTTTTTT..', '..TTTTTT..', '..TTTTTT..', 'PPPPPPPP..', 'PPPPPPPP..', 'PP........', 'PP........', 'KKK.......'],
    // Levantando: ainda agachado, joelhos dobrados
    rise: ['..........', '..........', ...HEAD_R, '..TTTTTT..', '.TTTTTTTT.', '.STTTTTTS.', '..TTTTTT..', '..PPPPPP..', '.PP....PP.', '.PP....PP.', 'KKK....KKK'],
    stand: [...HEAD_R, '..TTTTTT..', '.TTTTTTTT.', '.tTTTTTTt.', '.STTTTTTS.', '..TTTTTT..', '..PPPPPP..', '..PP..PP..', '..PP..PP..', '..PP..PP..', '.KKK..KKK.'],
    // Espreguiçando: braços abrindo (up0), lá em cima (up1) e bocejando (up2)
    up0: [...HEAD_R, 'SttTTTTttS', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..PPPPPP..', '..PP..PP..', '..PP..PP..', '..PP..PP..', '.KKK..KKK.'],
    up1: ['.S.HHHH.S.', '.tHHHHHHt.', '.THSSSSHT.', '.TSSSSEST.', '..SSSSSS..', '...SSSS...', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..PPPPPP..', '..PP..PP..', '..PP..PP..', '..PP..PP..', '.KKK..KKK.'],
    up2: ['S..HHHH..S', 't.HHHHHH.t', '.THSSSSHT.', '.TSEESSET.', '..SSEESS..', '...SSSS...', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..TTTTTT..', '..PPPPPP..', '..PP..PP..', '..PP..PP..', '..PP..PP..', '.KKK..KKK.'],
    // Andando: passada aberta, junta, aberta trocada, junta (a cabeça desce 1 pixel na junta)
    w1: [...HEAD_R, '..TTTTTT..', '.TTTTTTTT.', 'StTTTTTT..', '..TTTTTTS.', '..TTTTTT..', '..PPPPPP..', '.PPP..PPP.', 'PP......PP', 'PP......PP', 'KK......KK'],
    w2: ['..........', ...HEAD_R, '..TTTTTT..', '.TTTTTTT..', '.tTTTTTS..', '..TTTTTT..', '..PPPPPP..', '...PPPP...', '...PPPP...', '...PPPP...', '...KKKKK..'],
    w3: [...HEAD_R, '..TTTTTT..', '.TTTTTTTT.', '..TTTTTTtS', '.STTTTTT..', '..TTTTTT..', '..PPPPPP..', '.PPP..PPP.', 'PP......PP', 'PP......PP', 'KK......KK'],
  };
  const W_DESK = [
    '...MMMMMMMMM....', '...MBBBBBBBM....', '...MBBZBBBBM....', '...MBBBBBBBM....', '...MMMMMMMMM....', '.......MM.......',
    'DDDDDDDDDDDDDDDD', 'd..............d', 'd..............d', 'd..............d',
  ];
  // Sol (para onde ele vai andar, "tomar um sol") e um pouco de grama no chão
  const W_SUN = ['....y....', '.y..Y..y.', '...YYY...', '..YYYYY..', 'yYYYYYYYy', '..YYYYY..', '...YYY...', '.y..Y..y.', '....y....'];
  const W_GRASS = ['.G...G.....G..G...G....', 'gGG.gGG..GgG.GGg.gGG.Gg'];
  const W_CHAIR = ['.....C', '.....C', '.....C', '.....C', 'CCCCCC', 'C....C', 'C....C', 'C....C', 'C....C', 'C....C'];
  const FIG_PX = Object.fromEntries(Object.entries(W_FIG).map(([k, rows]) => [k, pixels(rows, W_PAL)]));
  // Escala 2: cada pixel vira 2×2 na ilha (54 px de altura = 27 pixels de cena; chão na linha 25)
  const WALK_PX =
    '<svg class="walk-scene px" id="walkScene" viewBox="0 0 200 27" aria-hidden="true">' +
    `<g class="w-props">${pixels(W_DESK, W_PAL, 4, 15)}${pixels(W_CHAIR, W_PAL, 22, 15)}` +
    `${pixels(W_SUN, W_PAL, 110, 2)}${pixels(W_GRASS, W_PAL, 88, 23)}</g>` +
    '<g id="wFig"></g></svg>';

  $('miniMascot').innerHTML = PIXEL_ART ? MASCOT_PX : MASCOT;   // desenhos fixos daqui: nada de fora entra no HTML
  if (PIXEL_ART) $('walkScene').outerHTML = WALK_PX;

  /* Roteiro da cena de levantar, tocado quadro a quadro (só enquanto o aviso está na tela):
     cada trecho tem quanto dura, onde ele está (x) e quais quadros mostra, em quantos quadros por segundo.
     Um só quadro na tela por vez (nada de duas poses juntas) e ele anda 1 pixel por vez. */
  const WALK_SCRIPT = [
    { ms: 1400, x: 17, frames: ['sit1', 'sit2'], fps: 4 },                                       // digitando
    { ms: 260, x: 18, frames: ['rise'] },                                                         // levantando
    { ms: 320, x: 18, frames: ['stand'] },
    { ms: 1700, x: 18, seq: [['up0', 160], ['up1', 300], ['up2', 560], ['up1', 280], ['up0', 160], ['stand', 240]] },   // espreguiça
    { ms: 4000, x: 18, to: 98, frames: ['w1', 'w2', 'w3', 'w2'], fps: 8 },                       // anda devagar até o sol
    { ms: 2000, x: 98, seq: [['stand', 420], ['up0', 120], ['up1', 700], ['up0', 120], ['stand', 640]] },               // curte o sol
  ];
  let walkRaf = 0;
  function playWalk() {
    cancelAnimationFrame(walkRaf);
    const bar = $('notice'), figEl = $('wFig');
    bar.classList.remove('scene-done');
    const t0 = performance.now();
    let last = '';
    const step = (now) => {
      if (bar.dataset.kind !== 'walk' || bar.hidden) return;   // o aviso fechou: para
      let t = now - t0, seg = null;
      for (const s of WALK_SCRIPT) { if (t < s.ms) { seg = s; break; } t -= s.ms; }
      if (!seg) { bar.classList.add('scene-done'); return; }    // acabou: some a cena e entram o texto e o botão
      let frame;
      if (seg.seq) {
        let u = t;
        frame = seg.seq[seg.seq.length - 1][0];
        for (const [k, d] of seg.seq) { if (u < d) { frame = k; break; } u -= d; }
      } else {
        frame = seg.frames[Math.floor(t / (1000 / (seg.fps || 1))) % seg.frames.length];
      }
      const x = seg.to ? Math.round(seg.x + (seg.to - seg.x) * (t / seg.ms)) : seg.x;
      const key = frame + '@' + x;
      if (key !== last) {   // só redesenha quando muda o quadro ou a posição
        last = key;
        figEl.setAttribute('transform', `translate(${x} 9)`);
        figEl.innerHTML = FIG_PX[frame];
      }
      walkRaf = requestAnimationFrame(step);
    };
    walkRaf = requestAnimationFrame(step);
  }

  // Ilha ociosa: 2 min sem o mouse em cima, sem aviso, sem som e sem Pomodoro → o mascote aparece
  const IDLE_MS = 2 * 60 * 1000;
  let idle = false, idleTimer = 0;
  // Entra no modo ocioso agora (só o mascote; a ilha desce um pouco)
  function rest() {
    clearTimeout(idleTimer);
    if (idle || pomoActive() || soundOn) return;
    idle = true;
    showMini();
    post({ type: 'idle', on: true });
  }

  function wake() {
    clearTimeout(idleTimer);
    if (idle) { idle = false; showMini(); post({ type: 'idle', on: false }); }
    idleTimer = setTimeout(() => {
      if (mode !== 'closed' || soundOn || pomoActive()) { wake(); return; }   // ainda tem algo acontecendo: espera mais
      idle = true;
      showMini();
      post({ type: 'idle', on: true });   // o C# cresce a ilha um pouco para baixo
    }, IDLE_MS);
  }

  /* ---------------- Onda de áudio: só aparece (e anda) com som saindo de verdade ---------------- */
  function showWave() {
    for (const w of [$('wave'), $('miniWave')]) {
      w.hidden = !soundOn;
      w.classList.toggle('playing', soundOn);
    }
    if (soundOn) wake();   // som tocando: a ilha não está ociosa
  }

  /* ---------------- Música ---------------- */
  const fmtTime = (s) => {
    s = Math.max(0, Math.floor(s));
    const m = Math.floor(s / 60), r = String(s % 60).padStart(2, '0');
    return m >= 60 ? `${Math.floor(m / 60)}:${String(m % 60).padStart(2, '0')}:${r}` : `${String(m).padStart(2, '0')}:${r}`;
  };

  // Posição agora: a que o player informou + o tempo que passou desde então (se está tocando)
  const position = () => {
    if (!media) return 0;
    const p = media.position + (media.playing ? (Date.now() - media.updatedAt) / 1000 : 0);
    return media.duration > 0 ? Math.min(p, media.duration) : p;
  };

  function showProgress() {
    const has = media && media.duration > 0;
    $('progress').hidden = !has;
    if (!has) return;
    const p = position();
    $('elapsed').textContent = fmtTime(p);
    $('remaining').textContent = '-' + fmtTime(media.duration - p);
    $('barFill').style.width = (p / media.duration) * 100 + '%';
  }

  // O tempo só anda com a ilha aberta e a música tocando
  let progressTimer = 0;
  function syncProgressTimer() {
    clearInterval(progressTimer);
    if (mode === 'open' && media && media.playing && media.duration > 0) progressTimer = setInterval(showProgress, 1000);
  }

  function showMedia() {
    const has = !!media;
    showWave();
    for (const img of [$('cover'), $('miniCover')]) {
      img.hidden = !(has && media.cover);
      if (has && media.cover && img.src !== media.cover) img.src = media.cover;
    }
    $('title').textContent = has ? media.title : t('islandNothing');
    $('subtitle').textContent = has ? [media.artist, media.album].filter(Boolean).join(' - ') || media.app : '';
    $('play').classList.toggle('playing', has && media.playing);
    $('play').disabled = !has;
    $('prev').disabled = !has || !media.canPrev;
    $('next').disabled = !has || !media.canNext;
    showMini();
    showProgress();
    syncProgressTimer();
    reportHeight();
  }

  $('play').addEventListener('click', () => post({ type: 'playPause' }));
  $('next').addEventListener('click', () => post({ type: 'next' }));
  $('prev').addEventListener('click', () => post({ type: 'prev' }));
  $('openMixer').addEventListener('click', () => post({ type: 'openMixer' }));

  /* ---------------- Água ---------------- */
  function showWater() {
    const done = water.todayMl >= water.goalMl;
    $('waterAmount').textContent = done
      ? t('waterDone').replace('{0}', V.fmtMl(water.todayMl, lang))
      : t('waterOf').replace('{0}', V.fmtMl(water.todayMl, lang)).replace('{1}', V.fmtMl(water.goalMl, lang));
    $('waterFill').style.width = Math.min(100, (water.todayMl / water.goalMl) * 100) + '%';
    $('waterPlus').textContent = '+ ' + V.fmtMl(water.cupMl, lang);
    $('waterMinus').disabled = water.todayMl <= 0;
    $('waterQuick').hidden = !water.enabled;
    $('waterQuick').title = '+ ' + V.fmtMl(water.cupMl, lang);
    showPages();
  }

  const addWater = (ml) => {
    water.todayMl = Math.max(0, water.todayMl + ml);
    showWater();
    post({ type: 'waterAdd', ml });
  };
  $('waterPlus').addEventListener('click', () => addWater(water.cupMl));
  $('waterQuick').addEventListener('click', () => addWater(water.cupMl));
  $('waterMinus').addEventListener('click', () => addWater(-Math.min(water.cupMl, water.todayMl)));

  /* ---------------- Pomodoro ----------------
     O C# manda o estado só quando muda (começou, pausou, trocou de fase). O tempo que falta é contado
     aqui: o anel enche pelo CSS (uma transição até o fim) e o número só anda com a ilha aberta. */
  let pomoOn = true;   // ligado nas configurações
  let pomo = { phase: 'idle', paused: false, leftMs: 0, totalMs: 25 * 60000, done: 0, every: 4, focusMin: 25 };
  let pomoEnds = 0;    // quando a fase acaba (relógio daqui)
  const pomoActive = () => pomoOn && pomo.phase !== 'idle';
  const pomoLeft = () => (pomo.phase === 'idle' ? pomo.totalMs : pomo.paused ? pomo.leftMs : Math.max(0, pomoEnds - Date.now()));
  const fmtClock = (ms) => {
    const s = Math.ceil(ms / 1000);
    return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
  };

  // Anel: começa onde está agora e, rodando, enche até o fim no tempo que falta
  function setRing(el, r) {
    const c = 2 * Math.PI * r;
    const left = pomoLeft(), total = pomo.totalMs || 1;
    el.style.strokeDasharray = c;
    el.style.transition = 'none';
    el.style.strokeDashoffset = pomo.phase === 'idle' ? c : c * (left / total);
    if (pomo.phase === 'idle' || pomo.paused || !left) return;
    void el.getBoundingClientRect();   // aplica a posição atual antes de animar
    el.style.transition = `stroke-dashoffset ${left}ms linear`;
    el.style.strokeDashoffset = 0;
  }

  function showFocusTime() {
    $('focusTime').textContent = fmtClock(pomoLeft());
  }

  let focusTimer = 0;
  function syncFocusTimer() {
    clearInterval(focusTimer);
    if (mode === 'open' && pomoActive() && !pomo.paused) focusTimer = setInterval(showFocusTime, 1000);
  }

  function showPomodoro() {
    const brk = pomo.phase === 'short' || pomo.phase === 'long';
    for (const el of [$('miniRing'), document.querySelector('.focus-ring')]) el.classList.toggle('break', brk);
    setRing($('miniRingFg'), 7.5);
    setRing($('focusRingFg'), 20);
    const phaseText = { idle: t('focusReady'), focus: t('focus'), short: t('focusShort'), long: t('focusLong') }[pomo.phase];
    $('focusPhase').textContent = pomo.paused ? `${phaseText} · ${t('focusPaused')}` : phaseText;
    $('focusCycle').textContent = pomo.phase === 'idle'
      ? t('focusReadyText').replace('{0}', pomo.focusMin)
      : t('focusCycle').replace('{0}', Math.min(pomo.done + (pomo.phase === 'focus' ? 1 : 0), pomo.every) || pomo.every).replace('{1}', pomo.every);
    const running = pomoActive() && !pomo.paused;
    const main = $('focusMain');
    main.classList.toggle('running', running);
    main.title = t(pomo.phase === 'idle' ? 'focusStart' : running ? 'focusPause' : 'focusResume');
    $('focusStop').disabled = pomo.phase === 'idle';
    $('focusSkip').disabled = pomo.phase === 'idle';
    showFocusTime();
    syncFocusTimer();
    showMini();
  }

  $('focusMain').addEventListener('click', () => {
    if (pomo.phase === 'idle') post({ type: 'pomoStart' });
    else post({ type: pomo.paused ? 'pomoResume' : 'pomoPause' });
  });
  $('focusStop').addEventListener('click', () => post({ type: 'pomoStop' }));
  $('focusSkip').addEventListener('click', () => post({ type: 'pomoSkip' }));

  function onPomodoro(m) {
    const started = pomo.phase === 'idle' && m.phase !== 'idle';
    pomo = m;
    pomoEnds = Date.now() + m.leftMs;
    if (started) goToPage('focus');   // começou um foco: abrir a ilha já mostra o Pomodoro
    showPomodoro();
  }

  /* ---------------- Páginas (setas ‹ › e bolinhas) ---------------- */
  let page = 0;
  const pages = () => [...document.querySelectorAll('.page')].filter((p) =>
    (p.dataset.page !== 'water' || water.enabled) && (p.dataset.page !== 'focus' || pomoOn));
  function goToPage(name) {
    const i = pages().findIndex((p) => p.dataset.page === name);
    if (i >= 0) { page = i; showPages(); }
  }

  function showPages() {
    const list = pages();
    page = Math.min(page, list.length - 1);
    // Todas as páginas ficam empilhadas no mesmo lugar (só a atual aparece): a ilha aberta tem sempre
    // a altura da maior, e não muda de tamanho ao trocar de player para o Pomodoro ou para a água
    document.querySelectorAll('.page').forEach((p) => {
      p.hidden = !list.includes(p);          // desligada nas configurações: fora da conta
      p.classList.toggle('off', p !== list[page]);
    });
    const many = list.length > 1;
    $('pagePrev').hidden = !many;
    $('pageNext').hidden = !many;
    $('dots').replaceChildren(...(many ? list.map((_, i) => {
      const dot = document.createElement('i');
      dot.className = i === page ? 'on' : '';
      dot.addEventListener('click', () => { page = i; showPages(); });
      return dot;
    }) : []));
    reportHeight();
  }

  const turn = (step) => { const n = pages().length; page = (page + step + n) % n; showPages(); };
  $('pagePrev').addEventListener('click', () => turn(-1));
  $('pageNext').addEventListener('click', () => turn(1));

  /* ---------------- Avisos do Volum ---------------- */
  const NOTICE_ICONS = {
    drop: '<svg viewBox="0 0 24 24"><path d="M12 2.8c3.6 4.4 6.2 7.8 6.2 11a6.2 6.2 0 0 1-12.4 0c0-3.2 2.6-6.6 6.2-11z" fill="#38bdf8"/></svg>',
    keyboard: '<svg viewBox="0 0 16 16"><g fill="none" stroke="#ffffff" stroke-width="1.4" stroke-linecap="round"><rect x="1.75" y="4" width="12.5" height="8" rx="1.5"/><path d="M4.5 6.75h.01M7 6.75h.01M9.5 6.75h.01M12 6.75h.01M5.5 9.5h5"/></g></svg>',
    timer: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="13.5" r="7.5"/><path d="M12 9.5v4l2.5 2M9.5 2.5h5M12 2.5V6"/></g></svg>',
    walk: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><circle cx="13" cy="4" r="1.8"/><path d="M9 21l3-6 2.5 2.5V21M12 15l-1.5-5 3.5-2 2.5 3 2.5 1M10.5 10l-3 1.5-1 3"/></g></svg>',
    sparkle: '<svg viewBox="0 0 24 24"><g fill="none" stroke="#ffffff" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M10 3c.8 4.5 2.5 6.2 7 7-4.5.8-6.2 2.5-7 7-.8-4.5-2.5-6.2-7-7 4.5-.8 6.2-2.5 7-7z"/><path d="M19 15v6M16 18h6"/></g></svg>',
    bell: '<svg viewBox="0 0 16 16"><path d="M4 11V7a4 4 0 0 1 8 0v4l1 1.5H3zM6.5 14h3" fill="none" stroke="#ffffff" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  };

  let lastNotice = '';
  function showNotice(n) {
    const on = !!n.icon;
    if (!on && lastNotice === 'greet') setTimeout(rest, 0);   // acabou o "bom dia": a ilha fica só com o mascote
    lastNotice = n.icon || '';
    island.classList.toggle('greeting', n.icon === 'greet');   // "bom dia": do tamanho da ilha com o mascote
    island.classList.toggle('walking', n.icon === 'walk');     // levantar: ilha mais alta, com a cena do bonequinho
    if (n.icon === 'walk') requestAnimationFrame(playWalk);   // a cena começa do início (depois de o aviso aparecer)
    $('notice').hidden = !on;
    if (!on) return;
    $('notice').dataset.kind = n.icon;   // água: botão azul; o resto, branco; greet: só a frase do "bom dia"
    $('noticeIcon').innerHTML = n.icon === 'greet' ? '' : NOTICE_ICONS[n.icon] || NOTICE_ICONS.bell;   // ícones fixos daqui; o "bom dia" é só a frase
    $('noticeTitle').textContent = n.title || '';
    $('noticeBody').textContent = n.text || '';
    $('noticeAction').hidden = !n.action;
    $('noticeAction').textContent = n.action || '';
  }

  $('noticeAction').addEventListener('click', () => post({ type: 'noticeAction' }));
  $('noticeClose').addEventListener('click', () => post({ type: 'noticeClose' }));

  /* ---------------- Abrir e fechar ---------------- */
  // O C# cresce a janela e manda "mode"; aqui a pílula anima (CSS) até o tamanho novo.
  document.documentElement.addEventListener('mouseenter', () => post({ type: 'hover', on: true }));
  document.documentElement.addEventListener('mouseleave', () => post({ type: 'hover', on: false }));

  function setMode(m) {
    mode = m;
    island.classList.remove('tucked', 'closed', 'idle', 'alerting', 'open');
    island.classList.add(m === 'notice' ? 'alerting' : m);   // "notice" vira "alerting": a classe .notice já tem outro uso
    if (m === 'open') { showProgress(); showFocusTime(); }
    if (m !== 'closed' && m !== 'idle') wake();   // abriu, recolheu ou chegou aviso: zera o tempo ocioso
    syncProgressTimer();
    syncFocusTimer();
  }

  // Altura da ilha aberta (muda com a página e com a água): o C# usa para o tamanho da janela
  let lastH = 0;
  function reportHeight() {
    const h = Math.ceil($('full').offsetHeight);
    island.style.setProperty('--open-h', h + 'px');
    if (h && h !== lastH) { lastH = h; post({ type: 'height', h }); }
  }

  /* ---------------- Mensagens do C# ---------------- */
  window.chrome.webview.addEventListener('message', (e) => {
    const m = e.data;
    switch (m.type) {
      case 'settings':
        lang = V.I18N[m.language] ? m.language : 'pt';
        document.documentElement.lang = V.langTag(lang);
        document.documentElement.classList.toggle('lite', !!m.lite);
        pomoOn = m.pomodoro !== false;
        V.applyI18n(lang);
        tickClock();
        showMedia();
        showWater();
        showPomodoro();
        break;
      case 'pomodoro': onPomodoro(m); break;
      case 'mode':    setMode(m.mode); break;
      case 'media':   media = m.media; showMedia(); break;
      case 'water':   water = m.water; showWater(); break;
      case 'battery': battery = m; showBattery(); break;
      case 'sound':   soundOn = m.on; showWave(); break;
      case 'notice':  showNotice(m); break;
    }
  });

  document.addEventListener('contextmenu', (e) => e.preventDefault());
  wake();   // começa a contar o tempo ocioso
  post({ type: 'ready' });
})();
