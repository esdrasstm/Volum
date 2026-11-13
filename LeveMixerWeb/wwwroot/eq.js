/* =========================================================
   Volum — equalizador (eq.html)
   A curva é calculada aqui com a mesma conta do motor (native/eq_engine.cpp: SVF de Simper,
   resposta idêntica ao RBJ). O C# grava o eq.bin e o efeito do Windows aplica.
   ========================================================= */
(() => {
  'use strict';

  /* ---------------- Textos ---------------- */
  const I18N = {
    pt: {
      title: 'Equalizador', device: 'Dispositivo', devDefault: 'Padrão', devEq: 'EQ ativo', tools: 'Ferramentas', range: 'Escala', bands: 'Bandas',
      preset: 'Preset', edited: 'editado', undo: 'Desfazer (Ctrl+Z)', redo: 'Refazer (Ctrl+Y)',
      import: 'Importar', export: 'Exportar', spectrum: 'Espectro', preamp: 'Preamp', auto: 'Auto',
      autoTip: 'Preamp automático: baixa o volume na medida exata do maior reforço da curva, para o som nunca estourar.',
      emptyHint: 'Dê dois cliques no gráfico para criar uma banda', addBand: 'Nova banda',
      type: 'Tipo', slope: 'Inclinação', dbOct: 'dB/oit', bandOn: 'Ligada', delete: 'Excluir',
      freq: 'Frequência', gain: 'Ganho', q: 'Q', band: 'Banda',
      on: 'Ligado', off: 'Desligado', bypassed: 'Equalizador desligado: o som passa sem alteração',
      types: { 1: 'Sino', 2: 'Shelf de graves', 3: 'Shelf de agudos', 4: 'Corte de graves', 5: 'Corte de agudos', 6: 'Notch' },
      plot: 'Gráfico do equalizador. Arraste um ponto para mudar frequência e ganho; a roda do mouse muda o Q. Setas movem a banda escolhida, Delete exclui.',
      builtIn: 'Do Volum', mine: 'Seus presets', none: 'Nenhum ainda', savePh: 'Nome do preset', save: 'Salvar', custom: 'Personalizado',
      findHeadphone: 'Achar a curva do seu fone no AutoEq', opensSite: 'abre o site',
      presets: {
        flat: ['Plano', '10 bandas zeradas'],
        footsteps: ['Passos', 'FPS: passos e recarga à frente'],
        competitive: ['Competitivo', 'Mais detalhe, sem cansar'],
        voice: ['Voz clara', 'Calls e streams'],
        bass: ['Graves', 'Mais peso no grave'],
        cinema: ['Cinema', 'Grave e brilho, imersivo'],
        night: ['Noite', 'Menos estrondo, fala clara'],
        bright: ['Brilho', 'Fones abafados'],
      },
      liveOff: 'Não ativado', liveRunning: 'Ativo, {rate} kHz', liveWaiting: 'Esperando som', liveWarn: 'Precisa de atenção', liveStore: 'Só na versão do site',
      sysOffTitle: 'Ainda não está valendo nos jogos',
      sysOffText: 'Ative no Windows para o equalizador valer em tudo que toca neste dispositivo: jogos, música, chamadas. Sem atraso nenhum.',
      sysRunningTitle: 'Ativo no Windows',
      sysRunningText: 'Processando {rate} kHz em {ch} canais, dentro do motor de áudio do Windows. Latência extra: zero.',
      sysChained: 'O efeito do fabricante continua ativo, antes do Volum.',
      sysClips: 'O som passou do limite {n} vezes no último segundo. Ligue o preamp Auto ou baixe o preamp.',
      sysWaitingTitle: 'Ativado, esperando som',
      sysWaitingText: 'Toque qualquer coisa (ou o ruído de teste) para confirmar que o Windows está passando o áudio pelo equalizador.',
      sysUpdateTitle: 'Tem uma versão nova do motor',
      sysUpdateText: 'O Volum foi atualizado. Atualize o motor no Windows para usar as melhorias (pede permissão de administrador).',
      sysEnhTitle: 'As melhorias de áudio estão desligadas',
      sysEnhText: 'Com elas desligadas nas configurações de som do Windows, nenhum efeito roda neste dispositivo, nem o equalizador.',
      sysFormatTitle: 'O Windows mandou um formato que o equalizador não processa',
      sysFormatText: 'O som passa intacto. Isso acontece com alguns drivers em modo de compatibilidade; reiniciar o PC costuma resolver.',
      sysStoreTitle: 'Disponível na versão do site',
      sysStoreText: 'Na versão da Microsoft Store o Windows não deixa instalar efeitos de áudio do sistema. Baixe o Volum pelo site para usar o equalizador nos jogos.',
      sysNoNativeTitle: 'O motor do equalizador não carregou',
      sysNoNativeText: 'Falta o VolumEqApo.dll ao lado do Volum. Reinstale o Volum.',
      activate: 'Ativar no Windows', removeSys: 'Remover do Windows', update: 'Atualizar motor', fix: 'Corrigir', soundSettings: 'Som do Windows',
      testNoise: 'Ruído de teste',
      installTitle: 'Ativar o equalizador no Windows?',
      installBody: '<p>O equalizador entra no motor de áudio do Windows, depois da mistura de todos os apps, e vale para tudo que toca neste dispositivo.</p><ul>'
        + '<li><b>Sem atraso:</b> o filtro processa o som no mesmo buffer, latência extra zero.</li>'
        + '<li><b>Fora do jogo:</b> roda no serviço de áudio do Windows, não dentro do jogo. Não injeta nada no processo do jogo.</li>'
        + '<li><b>O som some por 2 a 5 segundos</b> enquanto o serviço de áudio reinicia. O Windows pede permissão de administrador.</li>'
        + '<li>O Windows passa a aceitar efeitos de áudio sem assinatura do fabricante (como faz o Equalizer APO). Alguns players com DRM podem ficar mudos.</li>'
        + '<li>Apps em modo exclusivo (WASAPI exclusivo, ASIO) não passam pelo equalizador.</li></ul>'
        + '<p>Dá para desfazer tudo em “Remover do Windows”.</p>',
      updateTitle: 'Atualizar o motor do equalizador?',
      updateBody: '<p>O Windows pede permissão de administrador e o som some por alguns segundos enquanto o serviço de áudio reinicia. A sua curva continua igual.</p>',
      fixTitle: 'Ligar as melhorias de áudio?',
      fixBody: '<p>O Volum liga as melhorias de áudio deste dispositivo e reinicia o serviço de áudio (o som some por alguns segundos). Os efeitos do fabricante, se houver, voltam a valer também.</p>',
      removeTitle: 'Remover o equalizador deste dispositivo?',
      removeBody: '<p>O que havia antes no dispositivo volta exatamente como estava. O som some por alguns segundos enquanto o serviço de áudio reinicia.</p><p>A sua curva fica salva aqui na janela.</p>',
      cancel: 'Cancelar', remove: 'Remover',
      busyInstall: 'Ativando no Windows…', busyRemove: 'Removendo do Windows…',
      busyText: 'O som some por alguns segundos enquanto o Windows reinicia o serviço de áudio.',
      n: {
        installed: 'Pronto: o equalizador está valendo em todo o Windows neste dispositivo.',
        removed: 'Removido. O dispositivo voltou ao que era antes.',
        cancelled: 'Nada mudou: a permissão de administrador não foi dada.',
        'audio-down': 'O serviço de áudio do Windows não voltou. Reinicie o computador.',
        'no-device': 'Esse dispositivo não está mais no Windows. Escolha outro.',
        error: 'Não deu para concluir: {text}',
        store: 'Na versão da Microsoft Store o Windows não deixa instalar efeitos de áudio do sistema.',
        'import-empty': 'Esse arquivo não tem filtros. Use o ParametricEQ.txt do AutoEq ou uma configuração do Equalizer APO.',
        'import-graphic': 'Esse arquivo é um GraphicEQ. No AutoEq, baixe a versão paramétrica (ParametricEQ.txt).',
        imported: 'Importado: {n} bandas.', importTrim: 'O arquivo tinha mais de 32 bandas; ficaram as que mais mudam o som.',
        exported: 'Curva exportada.', 'export-failed': 'Não deu para salvar o arquivo: {text}',
        chained: 'O efeito do fabricante continua, antes do Volum.', enhancements: 'As melhorias de áudio do dispositivo foram ligadas.',
        verified: 'Conferido: o Windows carregou o equalizador e ele já está processando o som.',
        'loaded-silent': 'O Windows carregou o efeito, mas ele ainda não respondeu. Toque algum som e veja o status aqui embaixo.',
        'rolled-back': 'O Windows não tocou som com o equalizador ativo, então o Volum desfez tudo na hora e o áudio voltou ao normal. Nada ficou alterado.',
        'not-loaded': 'Atenção: o Windows não carregou o efeito. Reinicie o PC e abra o equalizador de novo. Os detalhes ficam no install.log, em Arquivos de Programas › Volum › Eq.',
        saved: 'Preset “{name}” salvo.', full: 'Cabem até 32 bandas.',
        exclusive: 'Outro app está usando esse dispositivo em modo exclusivo. Feche-o para tocar o ruído de teste.',
        'device-busy': 'Não deu para tocar nesse dispositivo agora.', format: 'Esse dispositivo não aceitou o formato do ruído de teste.',
        'device-missing': 'Esse dispositivo não está disponível.', 'audio-enumerator': 'O serviço de áudio do Windows não respondeu.',
      },
    },
    en: {
      title: 'Equalizer', device: 'Device', devDefault: 'Default', devEq: 'EQ on', tools: 'Tools', range: 'Scale', bands: 'Bands',
      preset: 'Preset', edited: 'edited', undo: 'Undo (Ctrl+Z)', redo: 'Redo (Ctrl+Y)',
      import: 'Import', export: 'Export', spectrum: 'Spectrum', preamp: 'Preamp', auto: 'Auto',
      autoTip: 'Auto preamp: lowers the level by exactly the curve’s biggest boost, so the sound never clips.',
      emptyHint: 'Double-click the graph to create a band', addBand: 'New band',
      type: 'Type', slope: 'Slope', dbOct: 'dB/oct', bandOn: 'On', delete: 'Delete',
      freq: 'Frequency', gain: 'Gain', q: 'Q', band: 'Band',
      on: 'On', off: 'Off', bypassed: 'Equalizer off: sound passes through unchanged',
      types: { 1: 'Bell', 2: 'Low shelf', 3: 'High shelf', 4: 'Low cut', 5: 'High cut', 6: 'Notch' },
      plot: 'Equalizer graph. Drag a point to change frequency and gain; the mouse wheel changes Q. Arrow keys move the selected band, Delete removes it.',
      builtIn: 'From Volum', mine: 'Your presets', none: 'None yet', savePh: 'Preset name', save: 'Save', custom: 'Custom',
      findHeadphone: 'Find your headphone’s curve on AutoEq', opensSite: 'opens the site',
      presets: {
        flat: ['Flat', '10 bands at zero'],
        footsteps: ['Footsteps', 'FPS: steps and reloads up front'],
        competitive: ['Competitive', 'More detail, less fatigue'],
        voice: ['Clear voice', 'Calls and streams'],
        bass: ['Bass', 'More weight down low'],
        cinema: ['Cinema', 'Low end and sparkle'],
        night: ['Night', 'Fewer booms, clear dialog'],
        bright: ['Bright', 'Muffled headphones'],
      },
      liveOff: 'Not enabled', liveRunning: 'Active, {rate} kHz', liveWaiting: 'Waiting for sound', liveWarn: 'Needs attention', liveStore: 'Website version only',
      sysOffTitle: 'Not working in games yet',
      sysOffText: 'Enable it in Windows so the equalizer applies to everything this device plays: games, music, calls. With no delay at all.',
      sysRunningTitle: 'Active in Windows',
      sysRunningText: 'Processing {rate} kHz on {ch} channels inside the Windows audio engine. Added latency: zero.',
      sysChained: 'The manufacturer effect is still on, before Volum.',
      sysClips: 'The sound went over the limit {n} times in the last second. Turn on Auto preamp or lower the preamp.',
      sysWaitingTitle: 'Enabled, waiting for sound',
      sysWaitingText: 'Play anything (or the test noise) to confirm Windows is routing audio through the equalizer.',
      sysUpdateTitle: 'A new engine version is available',
      sysUpdateText: 'Volum was updated. Update the engine in Windows to get the improvements (asks for administrator permission).',
      sysEnhTitle: 'Audio enhancements are off',
      sysEnhText: 'With them off in Windows sound settings, no effect runs on this device, including the equalizer.',
      sysFormatTitle: 'Windows sent a format the equalizer does not process',
      sysFormatText: 'Sound passes through untouched. Some drivers do this in compatibility mode; restarting the PC usually fixes it.',
      sysStoreTitle: 'Available in the website version',
      sysStoreText: 'In the Microsoft Store version, Windows does not allow installing system audio effects. Get Volum from the website to use the equalizer in games.',
      sysNoNativeTitle: 'The equalizer engine did not load',
      sysNoNativeText: 'VolumEqApo.dll is missing next to Volum. Reinstall Volum.',
      activate: 'Enable in Windows', removeSys: 'Remove from Windows', update: 'Update engine', fix: 'Fix it', soundSettings: 'Windows sound',
      testNoise: 'Test noise',
      installTitle: 'Enable the equalizer in Windows?',
      installBody: '<p>The equalizer joins the Windows audio engine after every app is mixed, and applies to everything this device plays.</p><ul>'
        + '<li><b>No delay:</b> the filter works in the same buffer, zero added latency.</li>'
        + '<li><b>Outside the game:</b> it runs in the Windows audio service, not inside the game. Nothing is injected into the game process.</li>'
        + '<li><b>Sound drops for 2 to 5 seconds</b> while the audio service restarts. Windows asks for administrator permission.</li>'
        + '<li>Windows starts accepting audio effects without a manufacturer signature (as Equalizer APO does). Some DRM players may go silent.</li>'
        + '<li>Apps in exclusive mode (WASAPI exclusive, ASIO) skip the equalizer.</li></ul>'
        + '<p>You can undo everything with “Remove from Windows”.</p>',
      updateTitle: 'Update the equalizer engine?',
      updateBody: '<p>Windows asks for administrator permission and sound drops for a few seconds while the audio service restarts. Your curve stays the same.</p>',
      fixTitle: 'Turn audio enhancements on?',
      fixBody: '<p>Volum turns on this device’s audio enhancements and restarts the audio service (sound drops for a few seconds). Manufacturer effects, if any, apply again too.</p>',
      removeTitle: 'Remove the equalizer from this device?',
      removeBody: '<p>Whatever the device had before comes back exactly as it was. Sound drops for a few seconds while the audio service restarts.</p><p>Your curve stays saved in this window.</p>',
      cancel: 'Cancel', remove: 'Remove',
      busyInstall: 'Enabling in Windows…', busyRemove: 'Removing from Windows…',
      busyText: 'Sound drops for a few seconds while Windows restarts the audio service.',
      n: {
        installed: 'Done: the equalizer now applies to all of Windows on this device.',
        removed: 'Removed. The device is back to how it was.',
        cancelled: 'Nothing changed: administrator permission was not given.',
        'audio-down': 'The Windows audio service did not come back. Restart the computer.',
        'no-device': 'That device is no longer in Windows. Pick another one.',
        error: 'Could not finish: {text}',
        store: 'In the Microsoft Store version, Windows does not allow installing system audio effects.',
        'import-empty': 'That file has no filters. Use AutoEq’s ParametricEQ.txt or an Equalizer APO config.',
        'import-graphic': 'That file is a GraphicEQ. On AutoEq, download the parametric version (ParametricEQ.txt).',
        imported: 'Imported: {n} bands.', importTrim: 'The file had more than 32 bands; the ones that change the sound most were kept.',
        exported: 'Curve exported.', 'export-failed': 'Could not save the file: {text}',
        chained: 'The manufacturer effect stays on, before Volum.', enhancements: 'The device’s audio enhancements were turned on.',
        verified: 'Checked: Windows loaded the equalizer and it is already processing sound.',
        'loaded-silent': 'Windows loaded the effect, but it has not answered yet. Play something and watch the status below.',
        'rolled-back': 'Windows could not play sound with the equalizer on, so Volum undid everything right away and audio is back to normal. Nothing was left changed.',
        'not-loaded': 'Heads up: Windows did not load the effect. Restart the PC and open the equalizer again. Details are in install.log, under Program Files › Volum › Eq.',
        saved: 'Preset “{name}” saved.', full: 'Up to 32 bands fit.',
        exclusive: 'Another app is using this device in exclusive mode. Close it to play the test noise.',
        'device-busy': 'Could not play on that device right now.', format: 'That device did not accept the test noise format.',
        'device-missing': 'That device is not available.', 'audio-enumerator': 'The Windows audio service did not respond.',
      },
    },
    es: {
      title: 'Ecualizador', device: 'Dispositivo', devDefault: 'Predeterminado', devEq: 'EQ activo', tools: 'Herramientas', range: 'Escala', bands: 'Bandas',
      preset: 'Preset', edited: 'editado', undo: 'Deshacer (Ctrl+Z)', redo: 'Rehacer (Ctrl+Y)',
      import: 'Importar', export: 'Exportar', spectrum: 'Espectro', preamp: 'Preamp', auto: 'Auto',
      autoTip: 'Preamp automático: baja el volumen justo lo que sube la curva en su punto más alto, para que el sonido nunca sature.',
      emptyHint: 'Haz doble clic en el gráfico para crear una banda', addBand: 'Nueva banda',
      type: 'Tipo', slope: 'Pendiente', dbOct: 'dB/oct', bandOn: 'Activa', delete: 'Eliminar',
      freq: 'Frecuencia', gain: 'Ganancia', q: 'Q', band: 'Banda',
      on: 'Encendido', off: 'Apagado', bypassed: 'Ecualizador apagado: el sonido pasa sin cambios',
      types: { 1: 'Campana', 2: 'Shelf de graves', 3: 'Shelf de agudos', 4: 'Corte de graves', 5: 'Corte de agudos', 6: 'Notch' },
      plot: 'Gráfico del ecualizador. Arrastra un punto para cambiar frecuencia y ganancia; la rueda cambia el Q. Las flechas mueven la banda elegida y Supr la elimina.',
      builtIn: 'De Volum', mine: 'Tus presets', none: 'Ninguno todavía', savePh: 'Nombre del preset', save: 'Guardar', custom: 'Personalizado',
      findHeadphone: 'Busca la curva de tus auriculares en AutoEq', opensSite: 'abre el sitio',
      presets: {
        flat: ['Plano', '10 bandas en cero'],
        footsteps: ['Pasos', 'FPS: pasos y recargas al frente'],
        competitive: ['Competitivo', 'Más detalle, sin cansar'],
        voice: ['Voz clara', 'Llamadas y streams'],
        bass: ['Graves', 'Más peso abajo'],
        cinema: ['Cine', 'Graves y brillo'],
        night: ['Noche', 'Menos estruendo, diálogo claro'],
        bright: ['Brillo', 'Auriculares apagados'],
      },
      liveOff: 'No activado', liveRunning: 'Activo, {rate} kHz', liveWaiting: 'Esperando sonido', liveWarn: 'Requiere atención', liveStore: 'Solo en la versión del sitio',
      sysOffTitle: 'Todavía no funciona en los juegos',
      sysOffText: 'Actívalo en Windows para que el ecualizador valga en todo lo que suena en este dispositivo: juegos, música, llamadas. Sin retraso.',
      sysRunningTitle: 'Activo en Windows',
      sysRunningText: 'Procesando {rate} kHz en {ch} canales dentro del motor de audio de Windows. Latencia extra: cero.',
      sysChained: 'El efecto del fabricante sigue activo, antes de Volum.',
      sysClips: 'El sonido pasó del límite {n} veces en el último segundo. Activa el preamp Auto o bájalo.',
      sysWaitingTitle: 'Activado, esperando sonido',
      sysWaitingText: 'Reproduce cualquier cosa (o el ruido de prueba) para confirmar que Windows pasa el audio por el ecualizador.',
      sysUpdateTitle: 'Hay una versión nueva del motor',
      sysUpdateText: 'Volum se actualizó. Actualiza el motor en Windows para usar las mejoras (pide permiso de administrador).',
      sysEnhTitle: 'Las mejoras de audio están apagadas',
      sysEnhText: 'Con ellas apagadas en la configuración de sonido de Windows, ningún efecto funciona en este dispositivo, tampoco el ecualizador.',
      sysFormatTitle: 'Windows envió un formato que el ecualizador no procesa',
      sysFormatText: 'El sonido pasa intacto. Pasa con algunos drivers en modo de compatibilidad; reiniciar el PC suele resolverlo.',
      sysStoreTitle: 'Disponible en la versión del sitio',
      sysStoreText: 'En la versión de Microsoft Store, Windows no permite instalar efectos de audio del sistema. Descarga Volum desde el sitio para usar el ecualizador en los juegos.',
      sysNoNativeTitle: 'El motor del ecualizador no cargó',
      sysNoNativeText: 'Falta VolumEqApo.dll junto a Volum. Reinstala Volum.',
      activate: 'Activar en Windows', removeSys: 'Quitar de Windows', update: 'Actualizar motor', fix: 'Corregir', soundSettings: 'Sonido de Windows',
      testNoise: 'Ruido de prueba',
      installTitle: '¿Activar el ecualizador en Windows?',
      installBody: '<p>El ecualizador entra en el motor de audio de Windows, después de mezclar todas las apps, y vale para todo lo que suena en este dispositivo.</p><ul>'
        + '<li><b>Sin retraso:</b> el filtro trabaja en el mismo buffer, latencia extra cero.</li>'
        + '<li><b>Fuera del juego:</b> funciona en el servicio de audio de Windows, no dentro del juego. No inyecta nada en el proceso del juego.</li>'
        + '<li><b>El sonido se corta de 2 a 5 segundos</b> mientras se reinicia el servicio de audio. Windows pide permiso de administrador.</li>'
        + '<li>Windows pasa a aceptar efectos de audio sin firma del fabricante (como hace Equalizer APO). Algunos reproductores con DRM pueden quedar mudos.</li>'
        + '<li>Las apps en modo exclusivo (WASAPI exclusivo, ASIO) no pasan por el ecualizador.</li></ul>'
        + '<p>Puedes deshacer todo con “Quitar de Windows”.</p>',
      updateTitle: '¿Actualizar el motor del ecualizador?',
      updateBody: '<p>Windows pide permiso de administrador y el sonido se corta unos segundos mientras se reinicia el servicio de audio. Tu curva sigue igual.</p>',
      fixTitle: '¿Activar las mejoras de audio?',
      fixBody: '<p>Volum activa las mejoras de audio de este dispositivo y reinicia el servicio de audio (el sonido se corta unos segundos). Los efectos del fabricante, si hay, vuelven a funcionar también.</p>',
      removeTitle: '¿Quitar el ecualizador de este dispositivo?',
      removeBody: '<p>Lo que tenía el dispositivo vuelve tal como estaba. El sonido se corta unos segundos mientras se reinicia el servicio de audio.</p><p>Tu curva queda guardada en esta ventana.</p>',
      cancel: 'Cancelar', remove: 'Quitar',
      busyInstall: 'Activando en Windows…', busyRemove: 'Quitando de Windows…',
      busyText: 'El sonido se corta unos segundos mientras Windows reinicia el servicio de audio.',
      n: {
        installed: 'Listo: el ecualizador vale en todo Windows en este dispositivo.',
        removed: 'Quitado. El dispositivo volvió a como estaba.',
        cancelled: 'Nada cambió: no se dio el permiso de administrador.',
        'audio-down': 'El servicio de audio de Windows no volvió. Reinicia el equipo.',
        'no-device': 'Ese dispositivo ya no está en Windows. Elige otro.',
        error: 'No se pudo terminar: {text}',
        store: 'En la versión de Microsoft Store, Windows no permite instalar efectos de audio del sistema.',
        'import-empty': 'Ese archivo no tiene filtros. Usa el ParametricEQ.txt de AutoEq o una configuración de Equalizer APO.',
        'import-graphic': 'Ese archivo es un GraphicEQ. En AutoEq, descarga la versión paramétrica (ParametricEQ.txt).',
        imported: 'Importado: {n} bandas.', importTrim: 'El archivo tenía más de 32 bandas; quedaron las que más cambian el sonido.',
        exported: 'Curva exportada.', 'export-failed': 'No se pudo guardar el archivo: {text}',
        chained: 'El efecto del fabricante sigue, antes de Volum.', enhancements: 'Se activaron las mejoras de audio del dispositivo.',
        verified: 'Comprobado: Windows cargó el ecualizador y ya está procesando el sonido.',
        'loaded-silent': 'Windows cargó el efecto, pero todavía no respondió. Reproduce algo y mira el estado abajo.',
        'rolled-back': 'Windows no pudo reproducir sonido con el ecualizador activo, así que Volum deshizo todo al instante y el audio volvió a la normalidad. No quedó nada cambiado.',
        'not-loaded': 'Atención: Windows no cargó el efecto. Reinicia el PC y abre el ecualizador de nuevo. Los detalles están en install.log, en Archivos de programa › Volum › Eq.',
        saved: 'Preset “{name}” guardado.', full: 'Caben hasta 32 bandas.',
        exclusive: 'Otra app usa este dispositivo en modo exclusivo. Ciérrala para reproducir el ruido de prueba.',
        'device-busy': 'No se pudo reproducir en ese dispositivo ahora.', format: 'Ese dispositivo no aceptó el formato del ruido de prueba.',
        'device-missing': 'Ese dispositivo no está disponible.', 'audio-enumerator': 'El servicio de audio de Windows no respondió.',
      },
    },
  };

  const $ = (id) => document.getElementById(id);
  const bridge = window.chrome && window.chrome.webview;
  const post = (msg) => { if (bridge) bridge.postMessage(msg); };
  let lang = 'pt';
  const t = (key) => {
    const parts = key.split('.');
    let v = I18N[lang], f = I18N.pt;
    for (const p of parts) { v = v && v[p]; f = f && f[p]; }
    return v !== undefined ? v : (f !== undefined ? f : key);
  };
  const fill = (s, vars) => String(s).replace(/\{(\w+)\}/g, (_, k) => (vars && vars[k] !== undefined ? vars[k] : ''));
  const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
  const store = {
    get(k, d) { try { const v = localStorage.getItem('eq.' + k); return v === null ? d : JSON.parse(v); } catch { return d; } },
    set(k, v) { try { localStorage.setItem('eq.' + k, JSON.stringify(v)); } catch { /* sem armazenamento */ } },
  };

  /* ---------------- Números ---------------- */
  let nf = {};
  function setFormats() {
    const loc = lang === 'en' ? 'en-US' : lang === 'es' ? 'es-ES' : 'pt-BR';
    nf = {};
    for (const d of [0, 1, 2]) nf[d] = new Intl.NumberFormat(loc, { minimumFractionDigits: d, maximumFractionDigits: d });
  }
  const num = (v, d) => nf[d].format(v);
  const fmtFreq = (f) => (f < 1000 ? num(f, f < 100 ? 1 : 0) + ' Hz' : num(f / 1000, f < 10000 ? 2 : 1) + ' kHz');
  const fmtGain = (g) => (g > 0.04 ? '+' : g < -0.04 ? '−' : '') + num(Math.abs(g), 1) + ' dB';
  const fmtQ = (q) => num(q, q < 10 ? 2 : 1);
  const parseNum = (s) => {
    const m = String(s).replace(/\s/g, '').replace('−', '-').replace(',', '.').match(/^([+-]?\d*\.?\d+)(k)?/i);
    if (!m) return NaN;
    return parseFloat(m[1]) * (m[2] ? 1000 : 1);
  };

  /* ---------------- Filtros (mesma conta do motor) ---------------- */
  const OFF = 0, BELL = 1, LS = 2, HS = 3, HP = 4, LP = 5, NOTCH = 6;
  const isGain = (type) => type === BELL || type === LS || type === HS;
  const isCut = (type) => type === HP || type === LP;
  const FS = 48000;
  const MAX_BANDS = 32;

  function design(b) {
    if (!b.enabled || b.type === OFF) return null;
    const f = clamp(b.freq, 10, Math.min(22000, FS * 0.49));
    const q = clamp(b.q, 0.1, 24);
    const g = clamp(b.gain, -24, 24);
    const w = Math.tan(Math.PI * f / FS);
    if (isGain(b.type)) {
      if (Math.abs(g) < 1e-9) return null;
      const A = Math.pow(10, g / 40);
      if (b.type === BELL) { const k = 1 / (q * A); return [{ g: w, k, m0: 1, m1: k * (A * A - 1), m2: 0 }]; }
      if (b.type === LS) { const k = 1 / q; return [{ g: w / Math.sqrt(A), k, m0: 1, m1: k * (A - 1), m2: A * A - 1 }]; }
      const k = 1 / q;
      return [{ g: w * Math.sqrt(A), k, m0: A * A, m1: k * (1 - A) * A, m2: 1 - A * A }];
    }
    if (b.type === NOTCH) { const k = 1 / q; return [{ g: w, k, m0: 1, m1: -k, m2: 0 }]; }
    const n = clamp(b.slope || 1, 1, 4), out = [];
    for (let i = 0; i < n; i++) {
      let qs = 1 / (2 * Math.sin(Math.PI * (2 * (i + 1) - 1) / (4 * n)));
      if (i === 0) qs *= q / Math.SQRT1_2;
      const k = 1 / qs;
      out.push(b.type === HP ? { g: w, k, m0: 1, m1: -k, m2: -1 } : { g: w, k, m0: 0, m1: 0, m2: 1 });
    }
    return out;
  }

  function stagesDb(st, f) {
    if (!st) return 0;
    const tt = Math.tan(Math.PI * Math.min(f, FS * 0.4999) / FS);
    let db = 0;
    for (const s of st) {
      const W = tt / s.g, dr = 1 - W * W, di = s.k * W;
      const nr = s.m0 * dr + s.m2, ni = s.m0 * di + s.m1 * W;
      const den = dr * dr + di * di, nm = nr * nr + ni * ni;
      db += nm > 1e-30 ? 10 * Math.log10(nm / den) : -300;
    }
    return db;
  }

  /* ---------------- Estado ---------------- */
  const COLORS = ['#ff8a4c', '#f5c04a', '#a6d65e', '#4fd1a5', '#52c3ea', '#8f9dff', '#c690f2', '#f37ba8'];
  const color = (i) => COLORS[i % COLORS.length];

  let S = {
    native: false, store: false, devices: [], activeId: '',
    curve: { bypass: false, preampDb: 0, autoPreamp: true, preset: 'b:flat', bands: [] },
    presets: [], system: {}, live: { state: 'off' }, busy: false,
  };
  let selected = 0;
  let range = store.get('range', 12);
  let spectrumOn = store.get('spectrum', true);
  let loaded = false;

  const cloneCurve = (c) => ({ bypass: !!c.bypass, preampDb: c.preampDb || 0, autoPreamp: c.autoPreamp !== false, preset: c.preset || '', bands: c.bands.map((b) => ({ ...b })) });

  /* ---------------- Presets do Volum ---------------- */
  const bell = (freq, gain, q) => ({ type: BELL, enabled: true, freq, gain, q, slope: 1 });
  const shelf = (type, freq, gain, q = 0.71) => ({ type, enabled: true, freq, gain, q, slope: 1 });
  const cut = (type, freq, slope = 2, q = 0.71) => ({ type, enabled: true, freq, gain: 0, q, slope });
  const flatBands = () => [32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000].map((f) => bell(f, 0, 1.41));
  const BUILTIN = [
    { id: 'flat', bands: flatBands },
    { id: 'footsteps', bands: () => [cut(HP, 25, 2), shelf(LS, 150, -5, 0.7), bell(400, -2, 1), bell(2200, 3, 1.1), bell(4600, 4.5, 1.4), shelf(HS, 10000, -1.5)] },
    { id: 'competitive', bands: () => [shelf(LS, 110, -3, 0.7), bell(300, -1.5, 1), bell(3200, 3, 1), bell(6000, 1.5, 1.5)] },
    { id: 'voice', bands: () => [cut(HP, 90, 2), bell(250, -2.5, 1), bell(3000, 3, 1), shelf(HS, 9000, -2)] },
    { id: 'bass', bands: () => [shelf(LS, 90, 6, 0.7), bell(300, -1.5, 1)] },
    { id: 'cinema', bands: () => [shelf(LS, 60, 4, 0.7), bell(2500, -1, 0.8), shelf(HS, 11000, 3)] },
    { id: 'night', bands: () => [shelf(LS, 160, -7, 0.6), bell(2800, 2, 0.9), shelf(HS, 9000, -2)] },
    { id: 'bright', bands: () => [bell(300, -1.5, 1), shelf(HS, 6000, 4, 0.7)] },
  ];

  function presetSource(id) {
    if (!id) return null;
    if (id.startsWith('b:')) {
      const p = BUILTIN.find((x) => x.id === id.slice(2));
      return p ? { bands: p.bands(), autoPreamp: true, preampDb: 0 } : null;
    }
    if (id.startsWith('user:')) {
      const p = S.presets.find((x) => x.name === id.slice(5));
      return p ? p.curve : null;
    }
    return null;
  }
  function presetLabel(id) {
    if (id && id.startsWith('b:')) return t('presets.' + id.slice(2))[0];
    if (id && id.startsWith('user:')) return id.slice(5);
    return t('custom');
  }
  const sig = (c) => JSON.stringify(c.bands.map((b) => [b.type, b.enabled, +b.freq.toFixed(2), +b.gain.toFixed(2), +b.q.toFixed(3), b.slope || 1]))
    + '|' + (c.autoPreamp ? 'a' : (+c.preampDb).toFixed(2));
  function isEdited() {
    const src = presetSource(S.curve.preset);
    return !!src && sig({ ...src, autoPreamp: src.autoPreamp !== false }) !== sig(S.curve);
  }

  /* ---------------- Desfazer / refazer ---------------- */
  // Cada banda nova herda o id visual da banda na mesma posição: o ponto desliza até o lugar novo.
  function adoptIds(from, to) {
    to.forEach((b, i) => {
      if (!from[i] || !from[i]._id) return;
      if (!Object.prototype.hasOwnProperty.call(b, '_id')) Object.defineProperty(b, '_id', { value: 0, enumerable: false, writable: true });
      b._id = from[i]._id;
    });
  }
  let undoStack = [], redoStack = [], lastKeyEdit = 0;
  function checkpoint() {
    const snap = JSON.stringify(S.curve);
    if (undoStack[undoStack.length - 1] === snap) return;
    undoStack.push(snap);
    if (undoStack.length > 120) undoStack.shift();
    redoStack = [];
    syncUndo();
  }
  function undo() {
    if (!undoStack.length) return;
    redoStack.push(JSON.stringify(S.curve));
    const before = S.curve.bands;
    S.curve = JSON.parse(undoStack.pop());
    adoptIds(before, S.curve.bands);
    afterJump();
  }
  function redo() {
    if (!redoStack.length) return;
    undoStack.push(JSON.stringify(S.curve));
    const before = S.curve.bands;
    S.curve = JSON.parse(redoStack.pop());
    adoptIds(before, S.curve.bands);
    afterJump();
  }
  function afterJump() {
    selected = clamp(selected, 0, Math.max(0, S.curve.bands.length - 1));
    changed();
    renderAll();
  }
  function syncUndo() {
    $('undo').disabled = !undoStack.length;
    $('redo').disabled = !redoStack.length;
  }

  /* ---------------- Envio ao C# ---------------- */
  let sendQueued = false;
  function changed() {
    invalidateCurves();
    requestDraw();
    if (!sendQueued) {
      sendQueued = true;
      requestAnimationFrame(() => {
        sendQueued = false;
        post({ type: 'curve', curve: S.curve });
      });
    }
    updatePresetButton();
    updatePreamp();
  }

  /* ---------------- Curvas calculadas ---------------- */
  const POINTS = 420;
  const FREQS = Array.from({ length: POINTS }, (_, i) => 20 * Math.pow(1000, i / (POINTS - 1)));
  let bandCurves = [], total = new Float64Array(POINTS), curvesValid = false, maxBoost = 0;
  function invalidateCurves() { curvesValid = false; }
  function computeCurves() {
    if (curvesValid) return;
    total = new Float64Array(POINTS);
    bandCurves = S.curve.bands.map((b) => {
      const st = design(b);
      const arr = new Float64Array(POINTS);
      if (st) for (let i = 0; i < POINTS; i++) { arr[i] = stagesDb(st, FREQS[i]); total[i] += arr[i]; }
      return arr;
    });
    let mx = -Infinity;
    for (let i = 0; i < POINTS; i++) mx = Math.max(mx, total[i]);
    for (const b of S.curve.bands) {
      if (!b.enabled || !isGain(b.type)) continue;
      const f = clamp(b.freq, 20, 20000);
      let s = 0;
      for (const o of S.curve.bands) s += stagesDb(design(o), f);
      mx = Math.max(mx, s);
    }
    maxBoost = Number.isFinite(mx) ? Math.round(mx * 100) / 100 : 0;
    curvesValid = true;
  }
  const effectivePreamp = () => { computeCurves(); return S.curve.autoPreamp ? -Math.max(0, maxBoost) : S.curve.preampDb; };

  /* ---------------- Visor ---------------- */
  const canvas = $('plot');
  const ctx = canvas.getContext('2d');
  let G = null, dpr = 1, drawQueued = false, nodes = [], qHandles = null, hover = { node: -1, q: 0, x: -1, y: -1 };
  let spec = null, specPeak = null, prevRange = range;

  function css(name, fallback) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback; }

  function resize() {
    const r = canvas.getBoundingClientRect();
    dpr = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.round(r.width * dpr));
    canvas.height = Math.max(1, Math.round(r.height * dpr));
    requestDraw();
  }
  function geom() {
    const w = canvas.width / dpr, h = canvas.height / dpr;
    return { w, h, l: 44, r: 14, t: 16, b: 26, iw: w - 58, ih: h - 42 };
  }
  const xOf = (f, g = G) => g.l + (Math.log(clamp(f, 20, 20000) / 20) / Math.log(1000)) * g.iw;
  const fOf = (x, g = G) => 20 * Math.pow(1000, clamp((x - g.l) / g.iw, 0, 1));
  const yOf = (db, g = G) => g.t + (1 - (db + vis.range) / (2 * vis.range)) * g.ih;
  const dbOf = (y, g = G) => ((1 - (y - g.t) / g.ih) * 2 - 1) * vis.range;

  function requestDraw() {
    if (drawQueued) return;
    drawQueued = true;
    requestAnimationFrame((now) => { drawQueued = false; draw(now); });
  }

  function nodeY(b) {
    if (isGain(b.type)) return yOf(clamp(b.gain, -vis.range, vis.range));
    if (b.type === NOTCH) return yOf(0);
    const st = design({ ...b, enabled: true });
    return yOf(clamp(stagesDb(st, b.freq), -vis.range, vis.range));
  }

  /* ---------------- Camada de animação do visor ----------------
     O modelo (S.curve) muda na hora; o que aparece na tela persegue o modelo com uma constante de tempo
     curta. Assim um preset, um desfazer ou uma troca de escala viram um movimento contínuo, e arrastar
     continua colado no mouse (o que está sendo arrastado não tem atraso). Com "reduzir movimento"
     ligado no Windows, tudo encaixa direto. */
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)');
  let lastFrame = 0, idSeq = 0, lastSelectedId = 0;
  const vis = {
    range, bypass: 0, intro: 0, total: null,
    bands: new Map(),     // id → curva mostrada da banda
    nodes: new Map(),     // id → { x, y, s (escala), a (opacidade) }
    ghosts: [],           // pontos que acabaram de sair, encolhendo
    ripples: [],          // onda ao escolher ou criar um ponto
    hoverMix: 0,
  };
  const ease = (dt, tau) => (reduceMotion.matches ? 1 : 1 - Math.exp(-dt / tau));
  const easeOutCubic = (x) => 1 - Math.pow(1 - x, 3);
  function bandId(b) {
    if (!b._id) Object.defineProperty(b, '_id', { value: ++idSeq, enumerable: false, writable: true });
    return b._id;
  }
  function ripple(x, y, col) {
    if (reduceMotion.matches) return;
    vis.ripples.push({ x, y, col, t0: performance.now() });
  }
  function startIntro() { vis.intro = reduceMotion.matches ? 1 : 0; }

  function chase(shown, target, k, snap) {
    let moving = false;
    for (let i = 0; i < target.length; i++) {
      const d = target[i] - shown[i];
      if (snap || Math.abs(d) < 0.004) shown[i] = target[i];
      else { shown[i] += d * k; moving = true; }
    }
    return moving;
  }

  function tracePath(c, arr, lim) {
    c.beginPath();
    for (let k = 0; k < POINTS; k++) {
      const x = xOf(FREQS[k]), y = yOf(clamp(arr[k], -lim, lim));
      k ? c.lineTo(x, y) : c.moveTo(x, y);
    }
  }

  function draw(now = performance.now()) {
    G = geom();
    computeCurves();
    const dt = lastFrame ? clamp(now - lastFrame, 1, 64) : 16;
    lastFrame = now;
    let animating = false;
    const dragging = !!drag;

    // Valores mostrados perseguindo o modelo
    const kFast = ease(dt, 70), kMid = ease(dt, 110);
    if (Math.abs(vis.range - range) > 0.01) { vis.range += (range - vis.range) * kMid; animating = true; } else vis.range = range;
    const bypassGoal = S.curve.bypass ? 1 : 0;
    if (Math.abs(vis.bypass - bypassGoal) > 0.01) { vis.bypass += (bypassGoal - vis.bypass) * ease(dt, 90); animating = true; } else vis.bypass = bypassGoal;
    if (vis.intro < 1) { vis.intro = Math.min(1, vis.intro + dt / 720); animating = true; }
    const hoverGoal = hover.x >= 0 && !dragging && hover.node < 0 ? 1 : 0;
    if (Math.abs(vis.hoverMix - hoverGoal) > 0.01) { vis.hoverMix += (hoverGoal - vis.hoverMix) * ease(dt, 60); animating = true; } else vis.hoverMix = hoverGoal;

    if (!vis.total || vis.total.length !== total.length) vis.total = Float64Array.from(total);
    else animating = chase(vis.total, total, kFast, dragging) || animating;
    const liveIds = new Set();
    S.curve.bands.forEach((b, i) => {
      const id = bandId(b);
      liveIds.add(id);
      const target = bandCurves[i];
      let shown = vis.bands.get(id);
      if (!shown) { shown = new Float64Array(POINTS); vis.bands.set(id, shown); }   // nasce do 0 dB
      animating = chase(shown, target, kFast, dragging && drag.i === i) || animating;
    });
    for (const id of [...vis.bands.keys()]) if (!liveIds.has(id)) vis.bands.delete(id);

    const c = ctx;
    c.setTransform(dpr, 0, 0, dpr, 0, 0);
    c.clearRect(0, 0, G.w, G.h);
    const grid = css('--eq-grid', 'rgba(255,255,255,.05)'), strong = css('--eq-grid-strong', 'rgba(255,255,255,.12)');
    const label = css('--eq-label', 'rgba(255,255,255,.4)'), curveC = css('--eq-curve', '#ffffff'), glow = css('--eq-curve-glow', 'rgba(255,255,255,.35)');
    const by = vis.bypass;

    // Grade de frequência
    c.font = '500 10.5px Sora, "Segoe UI", sans-serif';
    c.textBaseline = 'alphabetic';
    const majors = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000];
    c.lineWidth = 1;
    for (let d = 1; d <= 4; d++) for (let m = 1; m <= 9; m++) {
      const f = m * Math.pow(10, d);
      if (f < 20 || f > 20000) continue;
      const x = Math.round(xOf(f)) + 0.5;
      c.strokeStyle = majors.includes(f) ? strong : grid;
      c.beginPath(); c.moveTo(x, G.t); c.lineTo(x, G.t + G.ih); c.stroke();
    }
    c.fillStyle = label;
    c.textAlign = 'center';
    for (const f of majors) c.fillText(f >= 1000 ? (f / 1000) + 'k' : String(f), clamp(xOf(f), G.l + 8, G.l + G.iw - 8), G.h - 8);

    // Grade de ganho: durante a troca de escala, as linhas da escala nova entram e as da antiga saem
    c.textAlign = 'right';
    const stepFor = (r) => (r <= 6.5 ? 2 : r <= 13 ? 3 : 6);
    const drawGain = (r, alpha) => {
      if (alpha <= 0.01) return;
      const step = stepFor(r);
      c.globalAlpha = alpha;
      for (let db = -r; db <= r + 0.001; db += step) {
        const y = Math.round(yOf(db)) + 0.5;
        if (y < G.t - 1 || y > G.t + G.ih + 1) continue;
        c.strokeStyle = db === 0 ? strong : grid;
        c.beginPath(); c.moveTo(G.l, y); c.lineTo(G.l + G.iw, y); c.stroke();
        c.fillStyle = label;
        c.fillText((db > 0 ? '+' : db < 0 ? '−' : '') + Math.abs(db), G.l - 8, y + 3.5);
      }
      c.globalAlpha = 1;
    };
    if (vis.range === range) drawGain(range, 1);
    else {
      const t = clamp(Math.abs(vis.range - prevRange) / Math.max(0.001, Math.abs(range - prevRange)), 0, 1);
      drawGain(prevRange, 1 - t);
      drawGain(range, t);
    }

    // Espectro: área do que toca agora e a linha de pico, que desce devagar
    if (spectrumOn && spec) {
      const n = spec.length, lo = -90, hi = -6;
      const yS = (v, f) => G.t + G.ih - clamp((v + 3 * Math.log2(f / 1000) - lo) / (hi - lo), 0, 1) * G.ih;
      c.beginPath();
      c.moveTo(G.l, G.t + G.ih);
      for (let i = 0; i < n; i++) { const f = 20 * Math.pow(1000, (i + 0.5) / n); c.lineTo(xOf(f), yS(spec[i], f)); }
      c.lineTo(G.l + G.iw, G.t + G.ih);
      c.closePath();
      const grad = c.createLinearGradient(0, G.t, 0, G.t + G.ih);
      grad.addColorStop(0, css('--eq-spectrum-line', 'rgba(255,255,255,.15)'));
      grad.addColorStop(1, 'rgba(0,0,0,0)');
      c.fillStyle = grad;
      c.fill();
      if (specPeak) {
        c.beginPath();
        for (let i = 0; i < n; i++) { const f = 20 * Math.pow(1000, (i + 0.5) / n); const x = xOf(f), y = yS(specPeak[i], f); i ? c.lineTo(x, y) : c.moveTo(x, y); }
        c.strokeStyle = css('--eq-spectrum-line', 'rgba(255,255,255,.15)');
        c.lineWidth = 1;
        c.stroke();
      }
    }

    // Tudo da curva entra da esquerda para a direita quando a janela abre
    c.save();
    const reveal = easeOutCubic(vis.intro);
    c.beginPath(); c.rect(0, 0, G.l + G.iw * reveal + 2, G.h); c.clip();
    const curveAlpha = 1 - 0.62 * by;
    const zero = yOf(0), lim = Math.max(range, vis.range) * 1.6;

    // Curva de cada banda (preenchimento suave; a escolhida e a do mouse com contorno)
    S.curve.bands.forEach((b, i) => {
      if (b.type === OFF) return;
      const shown = vis.bands.get(bandId(b));
      if (!shown) return;
      const sel = i === selected, hov = i === hover.node;
      tracePath(c, shown, lim);
      if (sel || hov) {
        c.strokeStyle = color(i);
        c.globalAlpha = curveAlpha * (b.enabled ? 0.75 : 0.3);
        c.lineWidth = 1.25;
        c.stroke();
      }
      c.lineTo(xOf(20000), zero);
      c.lineTo(xOf(20), zero);
      c.closePath();
      c.fillStyle = color(i);
      c.globalAlpha = curveAlpha * (b.enabled ? (sel ? 0.2 : hov ? 0.14 : 0.06) : 0.03);
      c.fill();
    });
    c.globalAlpha = 1;

    // Curva final: preenchimento até o 0 dB, brilho e linha
    tracePath(c, vis.total, lim);
    c.lineTo(xOf(20000), zero);
    c.lineTo(xOf(20), zero);
    c.closePath();
    const fillGrad = c.createLinearGradient(0, G.t, 0, G.t + G.ih);
    fillGrad.addColorStop(0, curveC);
    fillGrad.addColorStop(0.5, 'rgba(0,0,0,0)');
    fillGrad.addColorStop(1, curveC);
    c.fillStyle = fillGrad;
    c.globalAlpha = 0.07 * curveAlpha;
    c.fill();
    c.globalAlpha = curveAlpha;
    tracePath(c, vis.total, lim);
    c.lineJoin = 'round';
    c.lineCap = 'round';
    c.shadowColor = glow;
    c.shadowBlur = 16 * (1 - by);
    c.strokeStyle = by > 0.5 ? label : curveC;
    c.lineWidth = 2.4;
    c.stroke();
    c.shadowBlur = 0;
    c.globalAlpha = 1;
    c.restore();

    // Guia que segue o mouse: linha vertical e o ponto da curva naquela frequência
    if (vis.hoverMix > 0.01 && hover.x >= G.l && hover.x <= G.l + G.iw && hover.y >= G.t && hover.y <= G.t + G.ih) {
      const f = fOf(hover.x);
      let db = 0;
      for (const b of S.curve.bands) db += stagesDb(design(b), f);
      const y = yOf(clamp(db, -vis.range, vis.range));
      c.globalAlpha = vis.hoverMix;
      c.strokeStyle = strong;
      c.setLineDash([2, 4]);
      c.beginPath(); c.moveTo(Math.round(hover.x) + 0.5, G.t); c.lineTo(Math.round(hover.x) + 0.5, G.t + G.ih); c.stroke();
      c.setLineDash([]);
      c.beginPath(); c.arc(hover.x, y, 3.5, 0, Math.PI * 2);
      c.fillStyle = curveC; c.fill();
      c.globalAlpha = 1;
    }

    // Q da banda escolhida (colchete arrastável)
    qHandles = null;
    const sb = S.curve.bands[selected];
    if (sb && sb.enabled && (sb.type === BELL || sb.type === NOTCH)) {
      const bw = 2 / Math.LN2 * Math.asinh(1 / (2 * sb.q));
      const nv = vis.nodes.get(bandId(sb));
      const y = nv ? nv.y : nodeY(sb);
      const x1 = xOf(sb.freq / Math.pow(2, bw / 2)), x2 = xOf(sb.freq * Math.pow(2, bw / 2));
      c.strokeStyle = color(selected);
      c.globalAlpha = 0.85 * reveal;
      c.lineWidth = 1.2;
      c.setLineDash([3, 3]);
      c.beginPath(); c.moveTo(x1, y); c.lineTo(x2, y); c.stroke();
      c.setLineDash([]);
      c.lineWidth = hover.q ? 2.8 : 2;
      for (const x of [x1, x2]) { c.beginPath(); c.moveTo(x, y - (hover.q ? 8 : 6)); c.lineTo(x, y + (hover.q ? 8 : 6)); c.stroke(); }
      c.globalAlpha = 1;
      qHandles = { x1, x2, y };
    }

    // Ondas (seleção e criação)
    vis.ripples = vis.ripples.filter((r) => {
      const t = (now - r.t0) / 460;
      if (t >= 1) return false;
      const e = easeOutCubic(t);
      c.beginPath(); c.arc(r.x, r.y, 10 + 20 * e, 0, Math.PI * 2);
      c.strokeStyle = r.col; c.globalAlpha = 0.45 * (1 - t); c.lineWidth = 2 * (1 - t) + 0.5; c.stroke();
      c.globalAlpha = 1;
      animating = true;
      return true;
    });

    // Pontos: perseguem a posição do modelo, crescem no mouse, nascem com mola e somem encolhendo
    const nodeIds = new Set();
    nodes = [];
    c.textAlign = 'center';
    c.textBaseline = 'middle';
    c.font = '700 10px Sora, "Segoe UI", sans-serif';
    const kNode = ease(dt, 55), kScale = ease(dt, 70);
    const drawNode = (x, y, r, col, enabled, labelText, alpha, sel) => {
      c.globalAlpha = alpha;
      if (sel) {
        c.beginPath(); c.arc(x, y, r + 5.5, 0, Math.PI * 2);
        c.fillStyle = col; c.globalAlpha = 0.2 * alpha; c.fill(); c.globalAlpha = alpha;
      }
      c.beginPath(); c.arc(x, y, r, 0, Math.PI * 2);
      if (enabled) {
        c.shadowColor = col; c.shadowBlur = sel ? 14 : 6;
        c.fillStyle = col; c.fill();
        c.shadowBlur = 0;
        c.fillStyle = '#120d0a';
      } else {
        c.fillStyle = css('--eq-display', '#0c0c0c'); c.fill();
        c.strokeStyle = col; c.lineWidth = 1.5; c.stroke();
        c.fillStyle = col;
      }
      if (r > 5) c.fillText(labelText, x, y + 0.5);
      c.globalAlpha = 1;
    };
    S.curve.bands.forEach((b, i) => {
      if (b.type === OFF) return;
      const id = bandId(b);
      nodeIds.add(id);
      const tx = xOf(b.freq), ty = nodeY(b);
      const sel = i === selected, hov = i === hover.node, held = dragging && drag.kind === 'node' && drag.i === i;
      let nv = vis.nodes.get(id);
      if (!nv) { nv = { x: tx, y: ty, s: 0, a: 1 }; vis.nodes.set(id, nv); if (loaded && vis.intro >= 1) ripple(tx, ty, color(i)); }
      if (held || reduceMotion.matches) { nv.x = tx; nv.y = ty; }
      else {
        const dx = tx - nv.x, dy = ty - nv.y;
        if (Math.abs(dx) + Math.abs(dy) > 0.15) { nv.x += dx * kNode; nv.y += dy * kNode; animating = true; } else { nv.x = tx; nv.y = ty; }
      }
      const sGoal = held ? 1.14 : sel ? 1.08 : hov ? 1.12 : 1;
      // Mola com um pouco de passagem: o ponto "salta" ao nascer e ao ser escolhido
      const ds = sGoal - nv.s;
      if (Math.abs(ds) > 0.002 || Math.abs(nv.v || 0) > 0.002) {
        nv.v = ((nv.v || 0) + ds * (reduceMotion.matches ? 1 : 0.32)) * 0.62;
        nv.s += reduceMotion.matches ? ds : nv.v;
        animating = true;
      } else { nv.s = sGoal; nv.v = 0; }
      nodes.push({ i, x: nv.x, y: nv.y });
      drawNode(nv.x, nv.y, 8.5 * Math.max(0, nv.s), color(i), b.enabled, String(i + 1), reveal, sel);
      if (sel && id !== lastSelectedId) { lastSelectedId = id; if (vis.intro >= 1) ripple(tx, ty, color(i)); }
    });
    for (const [id, nv] of vis.nodes) {
      if (nodeIds.has(id)) continue;
      vis.nodes.delete(id);
      if (!reduceMotion.matches) vis.ghosts.push({ ...nv, t0: now, col: nv.col || label });
    }
    vis.ghosts = vis.ghosts.filter((g) => {
      const t = (now - g.t0) / 180;
      if (t >= 1) return false;
      drawNode(g.x, g.y, 8.5 * g.s * (1 - t), g.col || label, true, '', 1 - t, false);
      animating = true;
      return true;
    });
    // Guarda a cor de cada ponto para o "fantasma" que encolhe quando ele sai
    S.curve.bands.forEach((b, i) => { const nv = vis.nodes.get(bandId(b)); if (nv) nv.col = color(i); });

    $('emptyHint').hidden = S.curve.bands.some((b) => b.type !== OFF);
    if (animating) requestDraw();
  }

  /* ---------------- Interação no visor ---------------- */
  let drag = null;

  function pointer(ev) {
    const r = canvas.getBoundingClientRect();
    return { x: ev.clientX - r.left, y: ev.clientY - r.top };
  }
  function hitNode(p) {
    let best = -1, dist = 14;
    for (const n of nodes) {
      const d = Math.hypot(n.x - p.x, n.y - p.y);
      if (d <= dist) { dist = d; best = n.i; }
    }
    return best;
  }
  function hitQ(p) {
    if (!qHandles) return 0;
    if (Math.abs(p.y - qHandles.y) > 9) return 0;
    if (Math.abs(p.x - qHandles.x1) < 6) return -1;
    if (Math.abs(p.x - qHandles.x2) < 6) return 1;
    return 0;
  }

  canvas.addEventListener('pointerdown', (ev) => {
    if (ev.button !== 0) return;
    G = geom();
    const p = pointer(ev);
    const q = hitQ(p);
    if (q) {
      checkpoint();
      drag = { kind: 'q', side: q, id: ev.pointerId };
      canvas.setPointerCapture(ev.pointerId);
      return;
    }
    const i = hitNode(p);
    if (i < 0) return;
    select(i);
    checkpoint();
    const b = S.curve.bands[i];
    drag = { kind: 'node', i, id: ev.pointerId, x0: p.x, y0: p.y, f0: b.freq, g0: b.gain, moved: false };
    canvas.setPointerCapture(ev.pointerId);
    canvas.classList.add('grabbing');
    showTip(i);
  });

  canvas.addEventListener('pointermove', (ev) => {
    G = geom();
    const p = pointer(ev);
    hover.x = p.x; hover.y = p.y;
    if (drag && drag.id === ev.pointerId) {
      const b = S.curve.bands[drag.kind === 'q' ? selected : drag.i];
      if (!b) return;
      if (drag.kind === 'q') {
        const oct = Math.abs(Math.log2(fOf(p.x) / b.freq)) * 2;
        const bw = clamp(oct, 0.05, 6), r = Math.pow(2, bw);
        b.q = clamp(Math.sqrt(r) / (r - 1), 0.1, 24);
        showTip(selected);
      } else {
        const fine = ev.shiftKey ? 0.15 : 1;
        const x = drag.x0 + (p.x - drag.x0) * fine, y = drag.y0 + (p.y - drag.y0) * fine;
        const ax = ev.altKey ? 'y' : ev.ctrlKey ? 'x' : '';
        if (ax !== 'y') b.freq = round(clamp(fOf(x), 10, 22000), 'freq');
        if (isGain(b.type) && ax !== 'x') b.gain = Math.round(clamp(dbOf(y), -24, 24) * 10) / 10;
        drag.moved = true;
        showTip(drag.i);
      }
      changed();
      syncInspector();
      syncRail();
      return;
    }
    const q = hitQ(p);
    const i = q ? -1 : hitNode(p);
    if (i !== hover.node || q !== hover.q) { hover.node = i; hover.q = q; requestDraw(); }
    canvas.classList.toggle('grab', i >= 0);
    canvas.style.cursor = q ? 'ew-resize' : '';
    if (i >= 0) showTip(i); else hideTip();
    readout(p);
  });

  function endDrag(ev) {
    if (!drag || drag.id !== ev.pointerId) return;
    drag = null;
    canvas.classList.remove('grabbing');
    hideTip();
  }
  canvas.addEventListener('pointerup', endDrag);
  canvas.addEventListener('pointercancel', endDrag);
  canvas.addEventListener('pointerleave', () => {
    if (drag) return;
    hover.node = -1; hover.q = 0; hover.x = -1;
    hideTip();
    $('readout').textContent = '';
    requestDraw();
  });

  canvas.addEventListener('dblclick', (ev) => {
    G = geom();
    const p = pointer(ev);
    const i = hitNode(p);
    if (i >= 0) {
      const b = S.curve.bands[i];
      if (!isGain(b.type)) return;
      checkpoint();
      b.gain = 0;
      changed(); renderAll();
      return;
    }
    addBand(fOf(p.x), clamp(dbOf(p.y), -24, 24));
  });

  canvas.addEventListener('wheel', (ev) => {
    G = geom();
    const i = hover.node >= 0 ? hover.node : selected;
    const b = S.curve.bands[i];
    if (!b || b.type === OFF) return;
    ev.preventDefault();
    if (i !== selected) select(i);
    keyCheckpoint();
    const k = ev.shiftKey ? 1.02 : 1.08;
    b.q = clamp(round(b.q * (ev.deltaY < 0 ? k : 1 / k), 'q'), 0.1, 24);
    changed(); syncInspector(); showTip(i);
  }, { passive: false });

  canvas.addEventListener('contextmenu', (ev) => {
    ev.preventDefault();
    G = geom();
    const i = hitNode(pointer(ev));
    if (i < 0) return;
    select(i);
    openCtx(i, ev.clientX, ev.clientY);
  });

  canvas.addEventListener('keydown', (ev) => {
    const b = S.curve.bands[selected];
    if (!b) return;
    const fine = ev.shiftKey;
    let handled = true;
    if (ev.key === 'ArrowLeft' || ev.key === 'ArrowRight') {
      keyCheckpoint();
      const s = Math.pow(2, (fine ? 1 / 96 : 1 / 24) * (ev.key === 'ArrowLeft' ? -1 : 1));
      b.freq = round(clamp(b.freq * s, 10, 22000), 'freq');
    } else if ((ev.key === 'ArrowUp' || ev.key === 'ArrowDown') && isGain(b.type)) {
      keyCheckpoint();
      b.gain = clamp(Math.round((b.gain + (fine ? 0.1 : 0.5) * (ev.key === 'ArrowUp' ? 1 : -1)) * 10) / 10, -24, 24);
    } else if (ev.key === '[' || ev.key === ']' || ev.key === 'PageDown' || ev.key === 'PageUp') {
      keyCheckpoint();
      const up = ev.key === ']' || ev.key === 'PageUp';
      b.q = clamp(round(b.q * (up ? 1.1 : 1 / 1.1), 'q'), 0.1, 24);
    } else if (ev.key === 'Delete' || ev.key === 'Backspace') {
      deleteBand(selected);
    } else handled = false;
    if (!handled) return;
    ev.preventDefault();
    changed(); syncInspector(); syncRail();
  });

  function keyCheckpoint() {
    const now = performance.now();
    if (now - lastKeyEdit > 700) checkpoint();
    lastKeyEdit = now;
  }

  function round(v, param) {
    if (param === 'freq') return v < 100 ? Math.round(v * 10) / 10 : v < 1000 ? Math.round(v) : Math.round(v / 10) * 10;
    if (param === 'q') return Math.round(v * 1000) / 1000;
    return Math.round(v * 10) / 10;
  }

  function showTip(i) {
    const b = S.curve.bands[i];
    const n = nodes.find((x) => x.i === i);
    if (!b || !n) return hideTip();
    const tip = $('tip');
    const gain = isGain(b.type) ? ' <b>' + fmtGain(b.gain) + '</b>' : '';
    tip.innerHTML = '<span class="swatch" style="background:' + color(i) + '"></span><b>' + fmtFreq(b.freq) + '</b>' + gain
      + ' <span class="dim">Q ' + fmtQ(b.q) + (b.enabled ? '' : ' · ' + t('off')) + '</span>';
    tip.hidden = false;
    const w = tip.offsetWidth, h = tip.offsetHeight;
    let x = n.x - w / 2, y = n.y - h - 18;
    if (y < 6) y = n.y + 18;
    x = clamp(x, 6, G.w - w - 6);
    tip.style.transform = 'translate(' + Math.round(x) + 'px,' + Math.round(y) + 'px)';
    tip.style.left = '0'; tip.style.top = '0';
  }
  function hideTip() { $('tip').hidden = true; }

  function readout(p) {
    if (p.x < G.l || p.x > G.l + G.iw || p.y < G.t || p.y > G.t + G.ih) { $('readout').textContent = ''; return; }
    const f = fOf(p.x);
    let db = 0;
    for (const b of S.curve.bands) db += stagesDb(design(b), f);
    $('readout').textContent = fmtFreq(f) + '   ' + fmtGain(db);
  }

  /* ---------------- Bandas ---------------- */
  function select(i) {
    selected = clamp(i, 0, Math.max(0, S.curve.bands.length - 1));
    syncRail(); syncInspector(); requestDraw();
  }

  function addBand(freq, gain) {
    if (S.curve.bands.length >= MAX_BANDS) { toast(t('n.full'), 'info'); return; }
    checkpoint();
    if (freq === undefined) {
      // No maior vão (em oitavas) entre as bandas que já existem.
      const fs = [20, ...S.curve.bands.map((b) => b.freq).sort((a, b) => a - b), 20000];
      let best = 1000, gap = 0;
      for (let k = 1; k < fs.length; k++) {
        const g = Math.log2(fs[k] / fs[k - 1]);
        if (g > gap) { gap = g; best = Math.sqrt(fs[k] * fs[k - 1]); }
      }
      freq = best; gain = 0;
    }
    S.curve.bands.push({ type: BELL, enabled: true, freq: round(clamp(freq, 20, 20000), 'freq'), gain: Math.round(gain * 10) / 10, q: 1.41, slope: 1 });
    S.curve.bands.sort((a, b) => a.freq - b.freq);
    const idx = S.curve.bands.findIndex((b) => b.freq === round(clamp(freq, 20, 20000), 'freq'));
    selected = idx >= 0 ? idx : S.curve.bands.length - 1;
    changed(); renderAll();
  }

  function deleteBand(i) {
    if (!S.curve.bands[i]) return;
    checkpoint();
    S.curve.bands.splice(i, 1);
    selected = clamp(i - 1 < 0 ? 0 : i - 1, 0, Math.max(0, S.curve.bands.length - 1));
    changed(); renderAll();
  }

  function setType(i, type) {
    const b = S.curve.bands[i];
    if (!b || b.type === type) return;
    checkpoint();
    const wasCut = isCut(b.type);
    b.type = type;
    if (isCut(type) && !wasCut) { b.q = 0.71; b.slope = b.slope || 2; }
    else if (type === NOTCH) b.q = Math.max(b.q, 4);
    else if (type === BELL && (wasCut || b.q > 10)) b.q = 1.41;
    else if ((type === LS || type === HS) && b.q > 2) b.q = 0.71;
    if (!isGain(type)) b.gain = 0;
    changed(); renderAll();
  }

  const TYPE_ORDER = [HP, LS, BELL, NOTCH, HS, LP];
  const TYPE_ICON = {
    [BELL]: 'M2 13h5c2.2 0 3.2-9 6-9s3.8 9 6 9h5',
    [LS]: 'M2 5h5c3.2 0 4.8 8 8 8h9',
    [HS]: 'M2 13h9c3.2 0 4.8-8 8-8h5',
    [HP]: 'M3 16c1.8-6 3.8-11 9-11h12',
    [LP]: 'M2 5h12c5.2 0 7.2 5 9 11',
    [NOTCH]: 'M2 6h8.4c1 0 1.6 10 2.6 10s1.6-10 2.6-10H24',
  };
  const typeSvg = (type) => '<svg viewBox="0 0 26 18" aria-hidden="true"><path d="' + TYPE_ICON[type] + '"/></svg>';

  function syncRail() {
    const rail = $('rail');
    const bands = S.curve.bands;
    while (rail.children.length > bands.length) rail.lastChild.remove();
    while (rail.children.length < bands.length) {
      const chip = document.createElement('button');
      chip.type = 'button';
      chip.className = 'chip';
      chip.setAttribute('role', 'tab');
      chip.innerHTML = '<span class="n"></span><span class="l"></span>';
      const idx = rail.children.length;
      if (loaded) {
        chip.classList.add('is-new');
        chip.addEventListener('animationend', () => chip.classList.remove('is-new'), { once: true });
      }
      chip.addEventListener('click', () => select(idx));
      chip.addEventListener('dblclick', () => { checkpoint(); S.curve.bands[idx].enabled = !S.curve.bands[idx].enabled; changed(); renderAll(); });
      chip.addEventListener('contextmenu', (e) => { e.preventDefault(); select(idx); openCtx(idx, e.clientX, e.clientY); });
      rail.appendChild(chip);
    }
    bands.forEach((b, i) => {
      const chip = rail.children[i];
      chip.style.setProperty('--c', color(i));
      chip.querySelector('.n').textContent = String(i + 1);
      chip.querySelector('.l').textContent = fmtFreq(b.freq) + (isGain(b.type) && Math.abs(b.gain) >= 0.05 ? '  ' + fmtGain(b.gain) : '');
      chip.setAttribute('aria-selected', String(i === selected));
      chip.classList.toggle('off', !b.enabled);
      chip.title = t('types')[b.type] || '';
    });
    $('addBand').disabled = bands.length >= MAX_BANDS;
    const sel = rail.children[selected];
    if (sel && selected !== scrolledTo) { scrolledTo = selected; sel.scrollIntoView({ block: 'nearest', inline: 'nearest' }); }
  }
  let scrolledTo = -1;

  function buildTypes() {
    const row = $('types');
    row.innerHTML = '';
    for (const type of TYPE_ORDER) {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'type-btn';
      btn.setAttribute('role', 'radio');
      btn.dataset.type = type;
      btn.innerHTML = typeSvg(type);
      btn.addEventListener('click', () => setType(selected, type));
      row.appendChild(btn);
    }
  }

  /* ---------------- Botões giratórios ---------------- */
  const KNOBS = {
    freq: { min: 20, max: 20000, log: true, def: 1000, label: 'freq', fmt: (v) => fmtFreq(v), parse: (s) => parseNum(s) },
    gain: { min: -24, max: 24, log: false, def: 0, label: 'gain', bipolar: true, fmt: (v) => fmtGain(v), parse: (s) => parseNum(s) },
    q: { min: 0.1, max: 24, log: true, def: 1.41, label: 'q', fmt: (v) => fmtQ(v), parse: (s) => parseNum(s) },
  };
  const knobEls = {};
  const toNorm = (k, v) => (k.log ? Math.log(v / k.min) / Math.log(k.max / k.min) : (v - k.min) / (k.max - k.min));
  const fromNorm = (k, n) => (k.log ? k.min * Math.pow(k.max / k.min, clamp(n, 0, 1)) : k.min + clamp(n, 0, 1) * (k.max - k.min));
  const ARC = 270, R = 22;
  const polar = (deg) => { const a = (deg - 90) * Math.PI / 180; return [27 + R * Math.cos(a), 27 + R * Math.sin(a)]; };
  function arcPath(a0, a1) {
    if (Math.abs(a1 - a0) < 0.5) return '';
    const [x0, y0] = polar(a0), [x1, y1] = polar(a1);
    const large = Math.abs(a1 - a0) > 180 ? 1 : 0, sweep = a1 > a0 ? 1 : 0;
    return 'M' + x0.toFixed(2) + ' ' + y0.toFixed(2) + 'A' + R + ' ' + R + ' 0 ' + large + ' ' + sweep + ' ' + x1.toFixed(2) + ' ' + y1.toFixed(2);
  }

  function buildKnobs() {
    document.querySelectorAll('.knob').forEach((el) => {
      const param = el.dataset.param, k = KNOBS[param];
      el.innerHTML = '<div class="dial" tabindex="-1"><svg viewBox="0 0 54 54" aria-hidden="true">'
        + '<path class="ring" d="' + arcPath(-135, 135) + '"/><path class="arc" d=""/>'
        + '<circle class="cap" cx="27" cy="27" r="15"/><path class="tick" d=""/></svg></div>'
        + '<input type="text" spellcheck="false" autocomplete="off"><span class="knob-label"></span>';
      const dial = el.querySelector('.dial'), input = el.querySelector('input');
      knobEls[param] = { el, arc: el.querySelector('.arc'), tick: el.querySelector('.tick'), input, label: el.querySelector('.knob-label') };
      let d = null;
      dial.addEventListener('pointerdown', (ev) => {
        const b = S.curve.bands[selected];
        if (!b) return;
        checkpoint();
        d = { y: ev.clientY, n: toNorm(k, b[param]), id: ev.pointerId };
        knobEls[param].dragging = true;
        el.classList.add('dragging');
        dial.setPointerCapture(ev.pointerId);
        ev.preventDefault();
      });
      dial.addEventListener('pointermove', (ev) => {
        if (!d || d.id !== ev.pointerId) return;
        const b = S.curve.bands[selected];
        const n = d.n + (d.y - ev.clientY) / (ev.shiftKey ? 900 : 180);
        b[param] = round(fromNorm(k, n), param);
        changed(); syncInspector(); syncRail();
      });
      const release = () => { d = null; knobEls[param].dragging = false; el.classList.remove('dragging'); };
      dial.addEventListener('pointerup', release);
      dial.addEventListener('pointercancel', release);
      dial.addEventListener('dblclick', () => {
        const b = S.curve.bands[selected];
        if (!b) return;
        checkpoint();
        b[param] = param === 'q' ? (isCut(b.type) ? 0.71 : b.type === NOTCH ? 8 : b.type === BELL ? 1.41 : 0.71) : k.def;
        changed(); syncInspector(); syncRail();
      });
      dial.addEventListener('wheel', (ev) => {
        const b = S.curve.bands[selected];
        if (!b) return;
        ev.preventDefault();
        keyCheckpoint();
        const n = toNorm(k, b[param]) + (ev.deltaY < 0 ? 1 : -1) / (ev.shiftKey ? 400 : 80);
        b[param] = round(fromNorm(k, n), param);
        changed(); syncInspector(); syncRail();
      }, { passive: false });
      const commit = () => {
        const b = S.curve.bands[selected];
        if (!b) return;
        const v = k.parse(input.value);
        if (Number.isFinite(v)) {
          checkpoint();
          b[param] = round(clamp(v, param === 'freq' ? 10 : k.min, param === 'freq' ? 22000 : k.max), param);
          changed(); syncRail();
        }
        syncInspector();
      };
      input.addEventListener('change', commit);
      input.addEventListener('keydown', (ev) => {
        if (ev.key === 'Enter') { input.blur(); }
        else if (ev.key === 'Escape') { syncInspector(); input.blur(); }
        else if (ev.key === 'ArrowUp' || ev.key === 'ArrowDown') {
          ev.preventDefault();
          const b = S.curve.bands[selected];
          if (!b) return;
          keyCheckpoint();
          const n = toNorm(k, b[param]) + (ev.key === 'ArrowUp' ? 1 : -1) / (ev.shiftKey ? 400 : 100);
          b[param] = round(fromNorm(k, n), param);
          changed(); syncInspector(); syncRail();
          input.select();
        }
      });
      input.addEventListener('focus', () => input.select());
    });
  }

  function drawKnobArc(param, n) {
    const k = KNOBS[param], ke = knobEls[param];
    const a = -135 + n * ARC;
    const from = k.bipolar ? 0 : -135;
    ke.arc.setAttribute('d', a >= from ? arcPath(from, a) : arcPath(a, from));
    const [x0, y0] = polar(a);
    const ix = 27 + (x0 - 27) * 0.42, iy = 27 + (y0 - 27) * 0.42, ox = 27 + (x0 - 27) * 0.62, oy = 27 + (y0 - 27) * 0.62;
    ke.tick.setAttribute('d', 'M' + ix.toFixed(2) + ' ' + iy.toFixed(2) + 'L' + ox.toFixed(2) + ' ' + oy.toFixed(2));
  }

  function paintKnob(param, value, disabled, c) {
    const k = KNOBS[param], ke = knobEls[param];
    if (!ke) return;
    const n = clamp(toNorm(k, value), 0, 1);
    if (ke.shown === undefined || ke.dragging || reduceMotion.matches) {
      ke.shown = n;
      ke.tween = null;
      drawKnobArc(param, n);
    } else if (Math.abs(n - ke.shown) > 1e-4 && (!ke.tween || ke.tween.to !== n)) {
      // Troca de banda, preset ou desfazer: o ponteiro gira até o valor novo em 220 ms.
      ke.tween = { from: ke.shown, to: n, t0: performance.now() };
      const step = (now) => {
        const tw = ke.tween;
        if (!tw) return;
        const t = clamp((now - tw.t0) / 220, 0, 1);
        ke.shown = tw.from + (tw.to - tw.from) * easeOutCubic(t);
        drawKnobArc(param, ke.shown);
        if (t < 1) requestAnimationFrame(step); else ke.tween = null;
      };
      requestAnimationFrame(step);
    }
    if (document.activeElement !== ke.input) ke.input.value = k.fmt(value);
    ke.label.textContent = t(k.label);
    ke.el.classList.toggle('disabled', !!disabled);
    ke.el.style.setProperty('--c', c);
  }

  function syncInspector() {
    const b = S.curve.bands[selected];
    const insp = $('inspector');
    insp.classList.toggle('none', !b);
    if (!b) return;
    const c = color(selected);
    insp.style.setProperty('--c', c);
    document.querySelectorAll('.type-btn').forEach((btn) => {
      const type = Number(btn.dataset.type);
      btn.setAttribute('aria-checked', String(type === b.type));
      btn.title = t('types')[type];
      btn.setAttribute('aria-label', t('types')[type]);
    });
    $('slopeField').classList.toggle('hidden', !isCut(b.type));
    document.querySelectorAll('#slope button').forEach((btn) => btn.setAttribute('aria-checked', String(Number(btn.dataset.slope) === (b.slope || 1))));
    paintKnob('freq', b.freq, false, c);
    paintKnob('gain', isGain(b.type) ? b.gain : 0, !isGain(b.type), c);
    paintKnob('q', b.q, false, c);
    $('bandOn').checked = !!b.enabled;
  }

  $('slope').addEventListener('click', (ev) => {
    const btn = ev.target.closest('button');
    const b = S.curve.bands[selected];
    if (!btn || !b) return;
    checkpoint();
    b.slope = Number(btn.dataset.slope);
    changed(); syncInspector();
  });
  $('bandOn').addEventListener('change', () => {
    const b = S.curve.bands[selected];
    if (!b) return;
    checkpoint();
    b.enabled = $('bandOn').checked;
    changed(); renderAll();
  });
  $('deleteBand').addEventListener('click', () => deleteBand(selected));
  $('addBand').addEventListener('click', () => addBand());

  /* ---------------- Menu de contexto ---------------- */
  function openCtx(i, x, y) {
    const b = S.curve.bands[i];
    const m = $('ctx');
    m.innerHTML = '';
    for (const type of TYPE_ORDER) {
      const it = menuItem(typeSvg(type).replace('<svg', '<svg class="spark" style="width:26px"') + '<span>' + t('types')[type] + '</span>', () => setType(i, type));
      it.setAttribute('aria-checked', String(b.type === type));
      m.appendChild(it);
    }
    m.appendChild(sep());
    m.appendChild(menuItem('<span>' + (b.enabled ? t('off') : t('on')) + '</span>', () => { checkpoint(); b.enabled = !b.enabled; changed(); renderAll(); }));
    m.appendChild(menuItem('<span>' + t('delete') + '</span>', () => deleteBand(i)));
    m.hidden = false;
    const w = m.offsetWidth, h = m.offsetHeight;
    m.style.left = clamp(x, 6, innerWidth - w - 6) + 'px';
    m.style.top = clamp(y, 6, innerHeight - h - 6) + 'px';
    m.querySelector('.menu-item')?.focus();
  }
  function menuItem(html, onClick) {
    const it = document.createElement('button');
    it.type = 'button';
    it.className = 'menu-item';
    it.setAttribute('role', 'menuitem');
    it.innerHTML = html;
    it.addEventListener('click', () => { closeMenus(); onClick(); });
    return it;
  }
  function sep() { const d = document.createElement('div'); d.className = 'menu-sep'; return d; }
  function closeMenus() {
    $('ctx').hidden = true;
    $('presetMenu').hidden = true;
    $('presetBtn').setAttribute('aria-expanded', 'false');
    $('deviceMenu').hidden = true;
    $('deviceBtn').setAttribute('aria-expanded', 'false');
  }
  document.addEventListener('pointerdown', (ev) => {
    if (!ev.target.closest('.menu') && !ev.target.closest('#presetBtn') && !ev.target.closest('#deviceBtn')) closeMenus();
  });

  /* ---------------- Presets ---------------- */
  function sparkline(bands) {
    const pts = [];
    const st = bands.map((b) => design(b));
    for (let i = 0; i <= 24; i++) {
      const f = 20 * Math.pow(1000, i / 24);
      let db = 0;
      for (const s of st) db += stagesDb(s, f);
      pts.push((i * 46 / 24).toFixed(1) + ' ' + (9 - clamp(db, -9, 9) * 0.85).toFixed(1));
    }
    return '<svg class="spark" viewBox="0 0 46 18" aria-hidden="true"><path d="M' + pts.join('L') + '"/></svg>';
  }

  function openPresetMenu() {
    const m = $('presetMenu');
    m.innerHTML = '';
    const head = (txt) => { const d = document.createElement('div'); d.className = 'menu-head'; d.textContent = txt; return d; };
    m.appendChild(head(t('builtIn')));
    for (const p of BUILTIN) {
      const [name, hint] = t('presets.' + p.id);
      const it = menuItem(sparkline(p.bands()) + '<span></span><span class="hint"></span>', () => applyPreset('b:' + p.id));
      it.children[1].textContent = name;
      it.children[2].textContent = hint;
      it.setAttribute('aria-checked', String(S.curve.preset === 'b:' + p.id));
      m.appendChild(it);
    }
    m.appendChild(sep());
    m.appendChild(head(t('mine')));
    if (!S.presets.length) {
      const none = document.createElement('div');
      none.className = 'menu-head';
      none.style.paddingTop = '2px';
      none.textContent = t('none');
      m.appendChild(none);
    }
    for (const p of S.presets) {
      const it = menuItem(sparkline(p.curve.bands) + '<span></span><span class="remove" role="button"><svg viewBox="0 0 24 24"><path d="M6 6l12 12M18 6 6 18"/></svg></span>', () => applyPreset('user:' + p.name));
      it.children[1].textContent = p.name;
      it.setAttribute('aria-checked', String(S.curve.preset === 'user:' + p.name));
      const rm = it.querySelector('.remove');
      rm.title = t('delete');
      rm.addEventListener('click', (e) => { e.stopPropagation(); post({ type: 'presetDelete', name: p.name }); closeMenus(); });
      m.appendChild(it);
    }
    const save = document.createElement('div');
    save.className = 'menu-save';
    save.innerHTML = '<input type="text" maxlength="40"><button type="button" class="ghost"></button>';
    const input = save.querySelector('input'), btn = save.querySelector('button');
    input.placeholder = t('savePh');
    btn.textContent = t('save');
    const doSave = () => {
      const name = input.value.trim();
      if (!name) { input.focus(); return; }
      post({ type: 'presetSave', name });
      S.curve.preset = 'user:' + name;
      changed();
      closeMenus();
      toast(fill(t('n.saved'), { name }), 'ok');
    };
    btn.addEventListener('click', doSave);
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') doSave(); });
    m.appendChild(save);
    m.appendChild(sep());
    m.appendChild(menuItem('<span>' + t('findHeadphone') + '</span><span class="hint">' + t('opensSite') + '</span>', () => post({ type: 'openAutoEq' })));
    m.querySelectorAll('.menu-item, .menu-head').forEach((it, i) => it.style.setProperty('--i', String(Math.min(i, 12))));
    m.hidden = false;
    $('presetBtn').setAttribute('aria-expanded', 'true');
    (m.querySelector('[aria-checked="true"]') || m.querySelector('.menu-item'))?.focus();
  }

  function applyPreset(id) {
    const src = presetSource(id);
    if (!src) return;
    checkpoint();
    const before = S.curve.bands;
    S.curve.bands = src.bands.map((b) => ({ ...b }));
    adoptIds(before, S.curve.bands);
    S.curve.autoPreamp = src.autoPreamp !== false;
    S.curve.preampDb = src.preampDb || 0;
    S.curve.preset = id;
    selected = 0;
    changed(); renderAll();
  }

  function updatePresetButton() {
    $('presetName').textContent = presetLabel(S.curve.preset);
    $('presetEdited').hidden = !isEdited();
  }

  $('presetBtn').addEventListener('click', () => ($('presetMenu').hidden ? openPresetMenu() : closeMenus()));

  /* ---------------- Preamp ---------------- */
  const PRE_MIN = -24, PRE_MAX = 12;
  function updatePreamp() {
    const v = effectivePreamp();
    const track = $('preampTrack');
    const pos = (x) => (1 - (clamp(x, PRE_MIN, PRE_MAX) - PRE_MIN) / (PRE_MAX - PRE_MIN)) * 100;
    const yv = pos(v), y0 = pos(0);
    $('preampThumb').style.top = yv + '%';
    track.querySelector('.preamp-zero').style.top = 'calc(' + y0 + '% - 1px)';
    const fillEl = $('preampFill');
    fillEl.style.top = Math.min(yv, y0) + '%';
    fillEl.style.height = Math.abs(yv - y0) + '%';
    $('preampOut').textContent = fmtGain(v);
    $('autoPreamp').checked = !!S.curve.autoPreamp;
    document.querySelector('.preamp').classList.toggle('auto-on', !!S.curve.autoPreamp);
    track.setAttribute('aria-valuenow', v.toFixed(1));
    track.setAttribute('aria-valuetext', fmtGain(v));
  }
  (() => {
    const track = $('preampTrack');
    let d = null;
    const setFrom = (clientY) => {
      const r = track.getBoundingClientRect();
      const n = 1 - clamp((clientY - r.top) / r.height, 0, 1);
      S.curve.preampDb = Math.round((PRE_MIN + n * (PRE_MAX - PRE_MIN)) * 2) / 2;
      changed();
    };
    track.addEventListener('pointerdown', (ev) => {
      if (S.curve.autoPreamp) return;
      checkpoint();
      d = ev.pointerId;
      track.classList.add('dragging');
      track.setPointerCapture(ev.pointerId);
      setFrom(ev.clientY);
    });
    track.addEventListener('pointermove', (ev) => { if (d === ev.pointerId) setFrom(ev.clientY); });
    track.addEventListener('pointerup', () => { d = null; track.classList.remove('dragging'); });
    track.addEventListener('dblclick', () => { if (S.curve.autoPreamp) return; checkpoint(); S.curve.preampDb = 0; changed(); });
    const nudge = (delta) => {
      if (S.curve.autoPreamp) return;
      keyCheckpoint();
      S.curve.preampDb = clamp(Math.round((S.curve.preampDb + delta) * 10) / 10, -30, 12);
      changed();
    };
    track.addEventListener('wheel', (ev) => { ev.preventDefault(); nudge(ev.deltaY < 0 ? 0.5 : -0.5); }, { passive: false });
    track.addEventListener('keydown', (ev) => {
      if (ev.key === 'ArrowUp' || ev.key === 'ArrowRight') { ev.preventDefault(); nudge(ev.shiftKey ? 0.1 : 0.5); }
      if (ev.key === 'ArrowDown' || ev.key === 'ArrowLeft') { ev.preventDefault(); nudge(ev.shiftKey ? -0.1 : -0.5); }
    });
    $('autoPreamp').addEventListener('change', () => {
      checkpoint();
      // Ao desligar o Auto, o manual começa do valor que o Auto estava usando (sem pulo de volume).
      if (!$('autoPreamp').checked) S.curve.preampDb = Math.round(effectivePreamp() * 10) / 10;
      S.curve.autoPreamp = $('autoPreamp').checked;
      changed();
    });
  })();

  /* ---------------- Liga/desliga, ferramentas ---------------- */
  function setBypass(on) {
    checkpoint();
    S.curve.bypass = on;
    changed(); syncPower();
  }
  function syncPower() {
    const on = !S.curve.bypass;
    $('power').setAttribute('aria-checked', String(on));
    $('powerText').textContent = on ? t('on') : t('off');
    $('power').title = on ? '' : t('bypassed');
  }
  $('power').addEventListener('click', () => setBypass(!S.curve.bypass));
  $('undo').addEventListener('click', undo);
  $('redo').addEventListener('click', redo);

  $('importBtn').addEventListener('click', () => $('file').click());
  $('file').addEventListener('change', async () => {
    const f = $('file').files[0];
    $('file').value = '';
    if (f) post({ type: 'import', text: await f.text() });
  });
  document.addEventListener('dragover', (e) => { if (e.dataTransfer && [...e.dataTransfer.types].includes('Files')) e.preventDefault(); });
  document.addEventListener('drop', async (e) => {
    const f = e.dataTransfer && e.dataTransfer.files[0];
    if (!f) return;
    e.preventDefault();
    post({ type: 'import', text: await f.text() });
  });
  $('exportBtn').addEventListener('click', () => post({ type: 'export' }));

  function syncSpectrumBtn() { $('spectrumBtn').setAttribute('aria-pressed', String(spectrumOn)); }
  $('spectrumBtn').addEventListener('click', () => {
    spectrumOn = !spectrumOn;
    store.set('spectrum', spectrumOn);
    if (!spectrumOn) spec = null;
    syncSpectrumBtn();
    post({ type: 'spectrum', on: spectrumOn });
    requestDraw();
  });

  function syncRange() {
    document.querySelectorAll('[data-range]').forEach((b) => b.setAttribute('aria-checked', String(Number(b.dataset.range) === range)));
  }
  document.querySelectorAll('[data-range]').forEach((b) => b.addEventListener('click', () => {
    prevRange = range;
    range = Number(b.dataset.range);
    store.set('range', range);
    syncRange(); requestDraw();
  }));

  document.addEventListener('keydown', (ev) => {
    const typing = ev.target.matches('input, textarea, select');
    if (ev.key === 'Escape') closeMenus();
    if (typing) return;
    const k = ev.key.toLowerCase();
    if ((ev.ctrlKey || ev.metaKey) && k === 'z' && !ev.shiftKey) { ev.preventDefault(); undo(); }
    else if ((ev.ctrlKey || ev.metaKey) && (k === 'y' || (k === 'z' && ev.shiftKey))) { ev.preventDefault(); redo(); }
    else if (k === 'b' && !ev.ctrlKey && !ev.altKey) { ev.preventDefault(); setBypass(!S.curve.bypass); }
  });

  /* ---------------- Sistema (ativar no Windows) ---------------- */
  let previewOn = false;
  function renderSystem() {
    const sys = S.system || {}, live = S.live || { state: 'off' };
    const el = $('system'), primary = $('systemPrimary'), secondary = $('systemSecondary');
    let state = 'off', title = '', text = '', p = null, s = null, chip = 'off', chipText = t('liveOff');
    const kHz = live.rate ? num(live.rate / 1000, live.rate % 1000 ? 1 : 0) : '';
    if (!S.native) {
      state = 'error'; title = t('sysNoNativeTitle'); text = t('sysNoNativeText'); chip = 'warn'; chipText = t('liveWarn');
    } else if (S.store) {
      state = 'waiting'; title = t('sysStoreTitle'); text = t('sysStoreText'); chipText = t('liveStore');
    } else if (!sys.installed) {
      state = 'off'; title = t('sysOffTitle'); text = t('sysOffText');
      p = { label: t('activate'), run: () => confirmSystem('install') };
    } else if (sys.enhancementsOff) {
      state = 'warn'; title = t('sysEnhTitle'); text = t('sysEnhText'); chip = 'warn'; chipText = t('liveWarn');
      p = { label: t('fix'), run: () => confirmSystem('fix') };
      s = { label: t('removeSys'), run: () => confirmSystem('remove') };
    } else if (sys.needsUpdate) {
      state = 'warn'; title = t('sysUpdateTitle'); text = t('sysUpdateText'); chip = 'warn'; chipText = t('liveWarn');
      p = { label: t('update'), run: () => confirmSystem('update') };
      s = { label: t('removeSys'), run: () => confirmSystem('remove') };
    } else if (live.state === 'running') {
      state = 'running'; chip = 'running'; chipText = fill(t('liveRunning'), { rate: kHz });
      title = t('sysRunningTitle');
      text = fill(t('sysRunningText'), { rate: kHz, ch: live.channels || 2 });
      if (live.chained) text += ' ' + t('sysChained');
      if (live.clips > 0) { state = 'warn'; text = fill(t('sysClips'), { n: live.clips }); }
      s = { label: t('removeSys'), run: () => confirmSystem('remove') };
    } else if (live.state === 'format') {
      state = 'warn'; chip = 'warn'; chipText = t('liveWarn'); title = t('sysFormatTitle'); text = t('sysFormatText');
      s = { label: t('removeSys'), run: () => confirmSystem('remove') };
    } else {
      state = 'waiting'; chip = 'waiting'; chipText = t('liveWaiting'); title = t('sysWaitingTitle'); text = t('sysWaitingText');
      s = { label: t('removeSys'), run: () => confirmSystem('remove') };
    }
    el.dataset.state = state;
    $('systemTitle').textContent = title;
    $('systemText').textContent = text;
    for (const [btn, cfg] of [[primary, p], [secondary, s]]) {
      btn.hidden = !cfg;
      if (cfg) { btn.textContent = cfg.label; btn.onclick = cfg.run; }
    }
    $('testNoise').hidden = !S.native;
    $('testNoise').setAttribute('aria-pressed', String(previewOn));
    $('liveChip').dataset.state = chip;
    $('liveText').textContent = chipText;
  }
  $('liveChip').addEventListener('click', () => {
    const el = $('system');
    el.animate([{ boxShadow: '0 0 0 0 var(--accent-ring)' }, { boxShadow: '0 0 0 6px var(--accent-ring)' }, { boxShadow: '0 0 0 0 transparent' }], { duration: 700 });
    ($('systemPrimary').hidden ? $('systemSecondary') : $('systemPrimary')).focus();
  });
  $('testNoise').addEventListener('click', () => {
    previewOn = !previewOn;
    $('testNoise').setAttribute('aria-pressed', String(previewOn));
    $('meter').hidden = !previewOn;
    post({ type: 'preview', on: previewOn });
  });

  function confirmSystem(kind) {
    const dlg = $('dialog');
    const map = {
      install: ['installTitle', 'installBody', 'activate', 'install'],
      update: ['updateTitle', 'updateBody', 'update', 'install'],
      fix: ['fixTitle', 'fixBody', 'fix', 'install'],
      remove: ['removeTitle', 'removeBody', 'remove', 'remove'],
    }[kind];
    $('dialogTitle').textContent = t(map[0]);
    $('dialogBody').innerHTML = t(map[1]);
    $('dialogOk').textContent = t(map[2]);
    dlg.onclose = () => { if (dlg.returnValue === 'ok') post({ type: map[3] }); };
    dlg.returnValue = '';
    dlg.showModal();
    $('dialogOk').focus();
  }

  let toastTimer = 0;
  function toast(text, kind = 'info') {
    const el = $('toast');
    el.textContent = text;
    el.dataset.kind = kind;
    el.classList.remove('leaving');
    el.hidden = false;
    el.style.animation = 'none';
    void el.offsetWidth;   // reinicia a entrada quando um aviso substitui outro
    el.style.animation = '';
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      el.classList.add('leaving');
      toastTimer = setTimeout(() => { el.hidden = true; el.classList.remove('leaving'); }, reduceMotion.matches ? 0 : 180);
    }, kind === 'error' ? 9000 : 4500);
  }

  /* ---------------- Montagem ---------------- */
  function applyText() {
    document.documentElement.lang = lang === 'en' ? 'en' : lang === 'es' ? 'es' : 'pt-BR';
    document.querySelectorAll('[data-i18n]').forEach((el) => { el.textContent = t(el.dataset.i18n); });
    document.querySelectorAll('[data-i18n-title]').forEach((el) => { el.title = t(el.dataset.i18nTitle); });
    document.querySelectorAll('[data-i18n-aria]').forEach((el) => el.setAttribute('aria-label', t(el.dataset.i18nAria)));
    canvas.setAttribute('aria-label', t('plot'));
    $('autoPreamp').parentElement.title = t('autoTip');
  }

  function renderAll() {
    syncRail(); syncInspector(); syncPower(); updatePresetButton(); updatePreamp(); syncUndo(); renderSystem();
    requestDraw();
  }

  function load(m) {
    const firstLoad = !loaded;
    const deviceChanged = S.activeId !== m.activeId;
    lang = I18N[m.language] ? m.language : 'pt';
    setFormats();
    document.documentElement.dataset.scheme = m.dark ? 'dark' : 'light';
    S.native = !!m.native;
    S.store = !!m.store;
    S.devices = m.devices || [];
    S.activeId = m.activeId || '';
    S.presets = m.presets || [];
    S.system = m.system || {};
    // Só troca a curva quando ela vem de fora (outro dispositivo ou primeira carga); senão a da tela manda.
    if (firstLoad || deviceChanged) {
      const before = S.curve.bands;
      S.curve = cloneCurve(m.curve);
      adoptIds(before, S.curve.bands);
      if (firstLoad) startIntro();
      undoStack = []; redoStack = [];
      selected = 0;
    }
    renderDeviceButton();
    if (firstLoad) {
      applyText();
      buildTypes();
      buildKnobs();
      syncRange();          // a escala e o espectro voltam como você deixou
      syncSpectrumBtn();
      post({ type: 'spectrum', on: spectrumOn });
    } else applyText();
    loaded = true;
    invalidateCurves();
    renderAll();
  }

  /* ---------------- Seletor de dispositivo ---------------- */
  const DEVICE_ICONS = {
    headphones: '<path d="M4 15v-3a8 8 0 0 1 16 0v3"/><rect x="3" y="14" width="4.5" height="6.5" rx="1.8"/><rect x="16.5" y="14" width="4.5" height="6.5" rx="1.8"/>',
    monitor: '<rect x="3" y="4" width="18" height="12.5" rx="2"/><path d="M9 20.5h6M12 16.5v4"/>',
    speaker: '<rect x="6" y="3" width="12" height="18" rx="2.5"/><circle cx="12" cy="14.5" r="3"/><path d="M12 7.5h.01" stroke-width="2.4"/>',
    virtual: '<path d="M5 12a7 7 0 0 1 14 0"/><path d="M8.5 12a3.5 3.5 0 0 1 7 0"/><circle cx="12" cy="16" r="1.6"/>',
  };
  // Pelo nome que o Windows dá: fone, monitor (som por HDMI/DisplayPort), alto-falante ou dispositivo virtual.
  function deviceKind(name) {
    const n = name.toLowerCase();
    if (/steam|virtual|vb-audio|voicemeeter|cable|nvidia broadcast|sonar/.test(n)) return 'virtual';
    if (/head|fone|auricular|earphone|earbud|buds|airpods|headset/.test(n)) return 'headphones';
    if (/display audio|hdmi|displayport|nvidia high definition|amd high definition|intel\(r\) display|monitor|\d{2}g\d|ultragear|odyssey/.test(n)) return 'monitor';
    return 'speaker';
  }
  function splitName(name) {
    const m = name.match(/^(.*?)\s*\((.*)\)\s*$/);
    return m ? { main: m[1], sub: m[2] } : { main: name, sub: '' };
  }
  const deviceSvg = (kind) => '<svg viewBox="0 0 24 24" aria-hidden="true">' + DEVICE_ICONS[kind] + '</svg>';

  function renderDeviceButton() {
    const d = S.devices.find((x) => x.id === S.activeId) || S.devices[0];
    $('deviceName').textContent = d ? splitName(d.name).main : '—';
    $('deviceIco').innerHTML = deviceSvg(d ? deviceKind(d.name) : 'speaker');
    $('deviceEq').hidden = !(d && d.installed);
    $('deviceEq').title = t('devEq');
    $('deviceBtn').title = d ? d.name : '';
  }

  function openDeviceMenu() {
    const m = $('deviceMenu');
    m.innerHTML = '';
    S.devices.forEach((d, i) => {
      const { main, sub } = splitName(d.name);
      const it = document.createElement('button');
      it.type = 'button';
      it.className = 'dev-item';
      it.setAttribute('role', 'option');
      it.setAttribute('aria-selected', String(d.id === S.activeId));
      it.style.setProperty('--i', String(Math.min(i, 10)));
      it.innerHTML = '<span class="dev-ico">' + deviceSvg(deviceKind(d.name)) + '</span>'
        + '<span class="dev-text"><b></b><small></small></span><span class="dev-tags"></span>'
        + '<svg class="dev-check" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>';
      it.querySelector('b').textContent = main;
      it.querySelector('small').textContent = sub;
      const tags = it.querySelector('.dev-tags');
      if (d.installed) { const tg = document.createElement('span'); tg.className = 'dev-tag on'; tg.textContent = t('devEq'); tags.appendChild(tg); }
      if (d.isDefault) { const tg = document.createElement('span'); tg.className = 'dev-tag'; tg.textContent = t('devDefault'); tags.appendChild(tg); }
      it.title = d.name;
      it.addEventListener('click', () => {
        closeMenus();
        $('deviceBtn').focus();
        if (d.id !== S.activeId) post({ type: 'select', id: d.id });
      });
      m.appendChild(it);
    });
    m.hidden = false;
    $('deviceBtn').setAttribute('aria-expanded', 'true');
    (m.querySelector('[aria-selected="true"]') || m.querySelector('.dev-item'))?.focus();
  }

  $('deviceBtn').addEventListener('click', () => ($('deviceMenu').hidden ? openDeviceMenu() : closeMenus()));
  $('deviceBtn').addEventListener('keydown', (ev) => {
    if ((ev.key === 'ArrowDown' || ev.key === 'ArrowUp') && $('deviceMenu').hidden) { ev.preventDefault(); openDeviceMenu(); }
  });

  // Setas, Home e End andam pelos itens de qualquer menu aberto; Esc fecha e devolve o foco ao botão.
  document.addEventListener('keydown', (ev) => {
    const menu = ev.target.closest && ev.target.closest('.menu');
    if (!menu) return;
    const items = [...menu.querySelectorAll('.menu-item, .dev-item')];
    const at = items.indexOf(ev.target);
    let next = -1;
    if (ev.key === 'ArrowDown') next = (at + 1) % items.length;
    else if (ev.key === 'ArrowUp') next = (at - 1 + items.length) % items.length;
    else if (ev.key === 'Home') next = 0;
    else if (ev.key === 'End') next = items.length - 1;
    else if (ev.key === 'Escape') {
      const owner = menu.id === 'deviceMenu' ? $('deviceBtn') : menu.id === 'presetMenu' ? $('presetBtn') : null;
      closeMenus();
      owner?.focus();
      ev.preventDefault();
      return;
    }
    if (next >= 0 && items.length) { ev.preventDefault(); items[next].focus(); }
  });

  function onMessage(m) {
    if (!m || !m.type) return;
    switch (m.type) {
      case 'state': load(m); break;
      case 'live': S.live = m; renderSystem(); break;
      case 'busy': {
        S.busy = !!m.on;
        $('busy').hidden = !m.on;
        if (m.on) { $('busyTitle').textContent = t(m.action === '--eq-remove' ? 'busyRemove' : 'busyInstall'); previewOn = false; $('meter').hidden = true; }
        break;
      }
      case 'notice': {
        let text = t('n.' + m.code);
        if (text === 'n.' + m.code || typeof text !== 'string') text = m.text || m.code;
        text = fill(text, { text: m.text || '' });
        if (m.notes && m.code !== 'rolled-back') for (const n of m.notes) { const extra = t('n.' + n); if (typeof extra === 'string' && extra !== 'n.' + n) text += ' ' + extra; }
        toast(text, m.kind);
        break;
      }
      case 'imported': {
        checkpoint();
        const c = cloneCurve(m.curve);
        adoptIds(S.curve.bands, c.bands);
        S.curve.bands = c.bands; S.curve.preampDb = c.preampDb; S.curve.autoPreamp = c.autoPreamp; S.curve.preset = '';
        selected = 0;
        changed(); renderAll();
        toast(fill(t('n.imported'), { n: c.bands.length }) + (m.trimmed ? ' ' + t('n.importTrim') : ''), 'ok');
        break;
      }
      case 'preview':
        previewOn = !!m.on;
        $('meter').hidden = !previewOn;
        renderSystem();
        if (m.error) toast(t('n.' + m.error) !== 'n.' + m.error ? t('n.' + m.error) : m.error, 'error');
        break;
      case 'peak': {
        const v = clamp((m.db + 60) / 60, 0, 1);
        $('meterFill').style.width = (v * 100).toFixed(1) + '%';
        $('meter').classList.toggle('clip', m.db > -0.1);
        break;
      }
      case 'spectrum': {
        const bins = m.bins;
        if (!spec || spec.length !== bins.length) spec = Float32Array.from(bins);
        // Sobe rápido e desce devagar, como um analisador de mesa.
        for (let i = 0; i < bins.length; i++) spec[i] = bins[i] > spec[i] ? spec[i] + (bins[i] - spec[i]) * 0.6 : Math.max(bins[i], spec[i] - 1.6);
        if (!specPeak || specPeak.length !== spec.length) specPeak = Float32Array.from(spec);
        for (let i = 0; i < spec.length; i++) specPeak[i] = Math.max(spec[i], specPeak[i] - 0.4);
        requestDraw();
        break;
      }
    }
  }

  if (bridge) bridge.addEventListener('message', (e) => onMessage(e.data));
  window.addEventListener('resize', resize);
  new ResizeObserver(resize).observe(canvas);
  setFormats();
  resize();

  /* ---------------- Modo de demonstração (página aberta fora do Volum) ---------------- */
  if (!bridge) {
    const demoCurve = { bypass: false, preampDb: 0, autoPreamp: true, preset: 'b:footsteps', bands: BUILTIN[1].bands() };
    demoCurve.bands[4].gain = 5;
    onMessage({
      type: 'state', language: (navigator.language || 'pt').slice(0, 2), dark: !matchMedia('(prefers-color-scheme: light)').matches, native: true, store: false,
      devices: [{ id: 'a', name: 'Headphones (Fone gamer)', isDefault: true, installed: true }, { id: 'b', name: 'Alto-falantes (Realtek)', installed: false }],
      activeId: 'a', curve: demoCurve, presets: [{ name: 'Valorant', curve: { bands: BUILTIN[2].bands(), autoPreamp: true } }],
      system: { installed: true, needsUpdate: false, enhancementsOff: false },
    });
    onMessage({ type: 'live', state: 'running', rate: 48000, channels: 2, clips: 0, chained: false });
    let ph = 0;
    setInterval(() => {
      ph += 0.08;
      const bins = Array.from({ length: 160 }, (_, i) => {
        const f = 20 * Math.pow(1000, (i + 0.5) / 160);
        return -32 - 3 * Math.log2(f / 1000) - 6 * Math.abs(Math.log2(f / 180)) * 0.5 + 6 * Math.sin(i * 0.37 + ph) + (Math.random() * 8 - 4) - (f > 12000 ? 14 : 0);
      });
      onMessage({ type: 'spectrum', bins });
    }, 33);
  } else {
    post({ type: 'ready' });
  }
})();
