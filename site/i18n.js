/* =========================================================
   Volum — idiomas do site (português é o texto do próprio HTML)
   O idioma vem do navegador: pt → português, es → espanhol, qualquer outro → inglês.
   Para testar: index.html?lang=en (ou es, pt).
   Cada texto em português (com os espaços juntados) aponta para [inglês, espanhol].
   Se um texto do HTML mudar, a tradução dele para de valer (fica em português) até atualizar aqui.
   ========================================================= */
(() => {
  'use strict';
  const q = new URLSearchParams(location.search).get('lang');
  const raw = (q || (navigator.languages && navigator.languages[0]) || navigator.language || 'pt').toLowerCase();
  const LANG = raw.startsWith('pt') ? 'pt' : raw.startsWith('es') ? 'es' : 'en';

  const T = {
    // Página
    'Volum — o volume de cada app, a um clique': ['Volum — every app\'s volume, one click away', 'Volum — el volumen de cada app, a un clic'],
    'Volum é um mixer de volume leve para Windows: controle o som de cada aplicativo e do microfone direto da barra de tarefas.':
      ['Volum is a lightweight volume mixer for Windows: control the sound of each app and your microphone right from the taskbar.',
       'Volum es un mezclador de volumen ligero para Windows: controla el sonido de cada aplicación y del micrófono desde la barra de tareas.'],

    // Topo
    'Seções': ['Sections', 'Secciones'],
    'Experimente': ['Try it', 'Pruébalo'],
    'Recursos': ['Features', 'Funciones'],
    'Como funciona': ['How it works', 'Cómo funciona'],
    'Perguntas': ['FAQ', 'Preguntas'],
    'Sugestões': ['Feedback', 'Sugerencias'],
    'Baixar': ['Download', 'Descargar'],

    // Hero
    'Mascote do Volum': ['Volum mascot', 'Mascota de Volum'],
    '<span class="dot"></span> Grátis para Windows 10 e 11': ['<span class="dot"></span> Free for Windows 10 and 11', '<span class="dot"></span> Gratis para Windows 10 y 11'],
    'O volume de cada app, <span class="grad">a um <em class="script">clique</em>.</span>':
      ['Every app\'s volume, <span class="grad">one <em class="script">click</em> away.</span>',
       'El volumen de cada app, <span class="grad">a un <em class="script">clic</em>.</span>'],
    'Volum é um mixer leve que mora na barra de tarefas do Windows. Abaixe o jogo, suba a call, ajuste o microfone e, se quiser, ganhe uma ilha no topo da tela com a música, o Pomodoro e lembretes para beber água e levantar.':
      ['Volum is a lightweight mixer that lives in the Windows taskbar. Turn the game down, the call up, adjust your mic and, if you want, get a dynamic island at the top of the screen with your music, a Pomodoro timer and reminders to drink water and stretch.',
       'Volum es un mezclador ligero que vive en la barra de tareas de Windows. Baja el juego, sube la llamada, ajusta el micrófono y, si quieres, ten una isla en la parte superior de la pantalla con tu música, el Pomodoro y recordatorios para beber agua y levantarte.'],
    'Baixar para Windows': ['Download for Windows', 'Descargar para Windows'],
    'Experimentar agora': ['Try it now', 'Probar ahora'],
    'Apoiar o Volum': ['Support Volum', 'Apoyar a Volum'],
    'Tão leve que você esquece que está aberto': ['So light you\'ll forget it\'s running', 'Tan ligero que olvidarás que está abierto'],
    'Instala em segundos': ['Installs in seconds', 'Se instala en segundos'],
    'Sem cadastro': ['No sign-up', 'Sin registro'],

    // Demo
    'Demonstração interativa da ilha do Volum': ['Interactive demo of the Volum island', 'Demostración interactiva de la isla de Volum'],
    'Demonstração interativa do mixer do Volum: mexa nos volumes': ['Interactive demo of the Volum mixer: play with the volumes', 'Demostración interactiva del mezclador de Volum: mueve los volúmenes'],
    'Atividades da ilha': ['Island activities', 'Actividades de la isla'],
    'Música': ['Music', 'Música'],
    'Água': ['Water', 'Agua'],
    'Agenda': ['Calendar', 'Agenda'],
    'Carregador': ['Charger', 'Cargador'],
    'Fone': ['Headphones', 'Auriculares'],
    'Câmera': ['Camera', 'Cámara'],
    'Abrir ou fechar o mixer do Volum': ['Open or close the Volum mixer', 'Abrir o cerrar el mezclador de Volum'],
    'Passe o mouse na ilha, use os botões da barra de tarefas e clique no Volum na bandeja':
      ['Hover over the island, use the taskbar buttons and click Volum in the tray',
       'Pasa el mouse por la isla, usa los botones de la barra de tareas y haz clic en Volum en la bandeja'],
    'Arraste os volumes e silencie os apps. Clique de novo no Volum para fechar':
      ['Drag the volumes and mute apps. Click Volum again to close', 'Arrastra los volúmenes y silencia las apps. Haz clic de nuevo en Volum para cerrar'],
    'No app, a engrenagem abre a janela de configurações': ['In the app, the gear opens the settings window', 'En la app, el engranaje abre la ventana de configuración'],

    // Números
    '0,1': ['0.1', '0,1'],
    'clique para abrir':['click to open', 'clic para abrir'],
    'de processador parado': ['CPU when idle', 'de CPU en reposo'],
    'por atualização': ['per update', 'por actualización'],
    'idiomas': ['languages', 'idiomas'],

    // Recursos
    'Tudo o que o mixer do Windows <br>deveria ter, num clique.': ['Everything the Windows mixer <br>should have, in one click.', 'Todo lo que el mezclador de Windows <br>debería tener, en un clic.'],
    'Sem abrir configurações, sem procurar painel escondido. E agora com uma ilha no topo da tela que cuida de você enquanto trabalha.':
      ['No digging through settings, no hunting for hidden panels. And now with an island at the top of the screen that looks after you while you work.',
       'Sin abrir configuraciones ni buscar paneles escondidos. Y ahora con una isla en la parte superior de la pantalla que te cuida mientras trabajas.'],
    'Volume de cada app': ['Per-app volume', 'Volumen de cada app'],
    'Cada programa que está tocando som aparece com seu ícone, volume e mute. A ondinha ao lado anda junto com o som.':
      ['Every program playing sound shows up with its icon, volume and mute. The little wave next to it moves with the sound.',
       'Cada programa que reproduce sonido aparece con su icono, volumen y silencio. La ondita de al lado se mueve con el sonido.'],
    'Microfone na mão': ['Mic at hand', 'Micrófono a mano'],
    'Silencie, ajuste o ganho e troque de microfone sem abrir as configurações do Windows.':
      ['Mute it, adjust the gain and switch microphones without opening Windows settings.', 'Silencia, ajusta la ganancia y cambia de micrófono sin abrir la configuración de Windows.'],
    'Ilha no topo da tela': ['Dynamic island at the top', 'Isla en la parte superior'],
    'Uma pílula preta no topo, estilo iPhone: música tocando com capa e controles, água e os avisos do Volum. Some sozinha em jogos e vídeos em tela cheia.':
      ['A black pill at the top, iPhone style: what\'s playing with cover art and controls, water and Volum\'s alerts. It hides by itself in full-screen games and videos.',
       'Una píldora negra arriba, estilo iPhone: la música que suena con portada y controles, agua y los avisos de Volum. Se oculta sola en juegos y videos a pantalla completa.'],
    'Pomodoro na ilha': ['Pomodoro on the island', 'Pomodoro en la isla'],
    'Foco e pausas com a contagem ali no topo: comece, pause e pule sem sair do que está fazendo. Os tempos você escolhe.':
      ['Focus and breaks with the countdown right at the top: start, pause and skip without leaving what you\'re doing. You pick the times.',
       'Enfoque y pausas con la cuenta regresiva arriba: empieza, pausa y salta sin dejar lo que estás haciendo. Tú eliges los tiempos.'],
    'Lembrete de água': ['Water reminder', 'Recordatorio de agua'],
    'Escolha a meta do dia e de quanto em quanto tempo quer ser lembrado. Marque cada copo direto no mixer ou na ilha.':
      ['Choose your daily goal and how often you want a reminder. Log each glass right in the mixer or on the island.',
       'Elige la meta del día y cada cuánto quieres que te lo recuerde. Marca cada vaso en el mezclador o en la isla.'],
    'Hora de levantar': ['Time to stand up', 'Hora de levantarse'],
    'Depois de um tempo sentado, a ilha lembra você de esticar as pernas e tomar um sol. Durante um foco, ela espera a pausa.':
      ['After a while sitting down, the island reminds you to stretch your legs and get some sun. During a focus session, it waits for the break.',
       'Después de un rato sentado, la isla te recuerda estirar las piernas y tomar un poco de sol. Durante un enfoque, espera a la pausa.'],
    'Abre no meio do jogo': ['Opens mid-game', 'Se abre en medio del juego'],
    'Ctrl+Alt+V abre o mixer por cima de tudo. E dá para travar o teclado por alguns minutos para limpar, sem desligar nada.':
      ['Ctrl+Alt+V opens the mixer on top of everything. You can also lock the keyboard for a few minutes to clean it, without turning anything off.',
       'Ctrl+Alt+V abre el mezclador encima de todo. Y puedes bloquear el teclado unos minutos para limpiarlo, sin apagar nada.'],
    'Leve de verdade': ['Truly lightweight', 'Ligero de verdad'],
    'Parado, praticamente não usa processador. Para PCs mais simples, o Modo leve desliga os efeitos e economiza ainda mais memória.':
      ['When idle, it barely uses any CPU. For lower-end PCs, Light mode turns off the effects and saves even more memory.',
       'En reposo, casi no usa procesador. Para PCs más sencillos, el Modo ligero apaga los efectos y ahorra aún más memoria.'],
    'Atualiza sozinho': ['Updates itself', 'Se actualiza solo'],
    'Versões novas chegam em silêncio. Em português, inglês e espanhol, e abre com o Windows, se você quiser.':
      ['New versions arrive silently. In English, Portuguese and Spanish, and it starts with Windows if you want.',
       'Las versiones nuevas llegan en silencio. En español, inglés y portugués, y se abre con Windows si quieres.'],

    // Como funciona
    'Pronto em três passos.': ['Ready in three steps.', 'Listo en tres pasos.'],
    'Não precisa instalar mais nada antes: tudo o que o Volum usa já vem junto.':
      ['Nothing else to install first: everything Volum needs comes included.', 'No necesitas instalar nada antes: todo lo que Volum usa ya viene incluido.'],
    'Baixe e instale': ['Download and install', 'Descarga e instala'],
    'Abra o instalador. Ele termina sozinho em poucos segundos, sem pedir senha de administrador.':
      ['Open the installer. It finishes on its own in a few seconds, with no admin password needed.', 'Abre el instalador. Termina solo en pocos segundos, sin pedir contraseña de administrador.'],
    'Clique na bandeja': ['Click the tray', 'Haz clic en la bandeja'],
    'O ícone do Volum fica perto do relógio. Um clique abre o mixer; clicar fora fecha.':
      ['The Volum icon sits next to the clock. One click opens the mixer; clicking outside closes it.', 'El icono de Volum está junto al reloj. Un clic abre el mezclador; hacer clic fuera lo cierra.'],
    'Ajuste e esqueça': ['Set it and forget it', 'Ajusta y olvídate'],
    'Arraste, silencie, troque o microfone. O Volum fica quietinho até você precisar de novo.':
      ['Drag, mute, switch microphones. Volum stays quiet until you need it again.', 'Arrastra, silencia, cambia el micrófono. Volum se queda tranquilo hasta que lo necesites de nuevo.'],
    'O Windows mostrou um aviso azul?': ['Did Windows show a blue warning?', '¿Windows mostró un aviso azul?'],
    'É normal para apps novos que ainda não têm assinatura digital paga. Clique em <kbd>Mais informações</kbd> e depois em <kbd>Executar assim mesmo</kbd>. O código do Volum é aberto e fica no GitHub.':
      ['That\'s normal for new apps that don\'t have a paid digital signature yet. Click <kbd>More info</kbd> and then <kbd>Run anyway</kbd>. Volum is open source and lives on GitHub. Or get it from the Microsoft Store, with no warning.',
       'Es normal en apps nuevas que aún no tienen firma digital de pago. Haz clic en <kbd>Más información</kbd> y luego en <kbd>Ejecutar de todas formas</kbd>. El código de Volum es abierto y está en GitHub. O descárgalo desde la Microsoft Store, sin aviso.'],

    // Perguntas
    'Dúvidas comuns': ['Common questions', 'Preguntas frecuentes'],
    'O Volum é grátis?': ['Is Volum free?', '¿Volum es gratis?'],
    'Sim. É gratuito e não pede cadastro.': ['Yes. It\'s free and needs no sign-up.', 'Sí. Es gratuito y no pide registro.'],
    'Ele deixa o computador mais lento?': ['Does it slow down my computer?', '¿Hace más lenta la computadora?'],
    'Não. Parado, ele praticamente não usa processador. Por padrão, a interface fica "dormindo" para abrir na hora. Se preferir economizar memória, ligue o Modo leve: o Volum só demora um pouquinho mais para abrir.':
      ['No. When idle, it barely uses any CPU. By default, the interface "sleeps" so it opens instantly. If you\'d rather save memory, turn on Light mode: Volum just takes a moment longer to open.',
       'No. En reposo, casi no usa procesador. Por defecto, la interfaz queda "dormida" para abrir al instante. Si prefieres ahorrar memoria, activa el Modo ligero: Volum solo tarda un poquito más en abrir.'],
    'Funciona no Windows 10?': ['Does it work on Windows 10?', '¿Funciona en Windows 10?'],
    'Funciona no Windows 10 e no 11 (64 bits), com o mesmo visual nos dois.': ['It works on Windows 10 and 11 (64-bit), with the same look on both.', 'Funciona en Windows 10 y 11 (64 bits), con el mismo aspecto en los dos.'],
    'A ilha atrapalha jogos ou vídeos?': ['Does the island get in the way of games or videos?', '¿La isla molesta en juegos o videos?'],
    'Não. Em tela cheia ela some sozinha, e se ficar em cima de abas ou menus de outro app, ela vira só um risquinho. Também dá para desligar em Configurações → Ilha.':
      ['No. In full screen it hides by itself, and when it sits over another app\'s tabs or menus it gets out of the way. You can also turn it off in Settings → Island.',
       'No. A pantalla completa se oculta sola, y si queda encima de pestañas o menús de otra app, se aparta. También puedes apagarla en Configuración → Isla.'],
    'O Volum coleta meus dados?': ['Does Volum collect my data?', '¿Volum recopila mis datos?'],
    'Não. A única conexão que ele faz é com o GitHub, para ver se existe versão nova.':
      ['No. It only connects to check for new versions and, if you turn them on, for the weather and your calendar.', 'No. Solo se conecta para buscar versiones nuevas y, si los activas, para el clima y tu agenda.'],
    'Como posso apoiar o Volum?': ['How can I support Volum?', '¿Cómo puedo apoyar a Volum?'],
    'O Volum é grátis e vai continuar grátis. Se ele te ajuda e você quiser apoiar, qualquer valor ajuda a manter o projeto andando: no Brasil, pelo Pix; de fora, pelo Ko-fi.':
      ['Volum is free and will stay free. If it helps you and you\'d like to support it, any amount keeps the project going, through Ko-fi.',
       'Volum es gratis y seguirá siéndolo. Si te ayuda y quieres apoyarlo, cualquier monto ayuda a mantener el proyecto, por Ko-fi.'],
    'Como desinstalar?': ['How do I uninstall it?', '¿Cómo desinstalarlo?'],
    'Configurações do Windows → Aplicativos → Aplicativos instalados → Volum → Desinstalar.':
      ['Windows Settings → Apps → Installed apps → Volum → Uninstall.', 'Configuración de Windows → Aplicaciones → Aplicaciones instaladas → Volum → Desinstalar.'],

    // Sugestões
    'Sugestões e bugs': ['Ideas and bugs', 'Sugerencias y errores'],
    'Ajude a deixar o Volum melhor.': ['Help make Volum better.', 'Ayuda a mejorar Volum.'],
    'Teve uma ideia ou algo não funcionou direito? Conta aqui. Toda mensagem é lida.':
      ['Got an idea, or something didn\'t work right? Tell us here. Every message gets read.', '¿Tuviste una idea o algo no funcionó bien? Cuéntalo aquí. Todos los mensajes se leen.'],
    'Tipo de mensagem': ['Message type', 'Tipo de mensaje'],
    'Sugestão': ['Idea', 'Sugerencia'],
    'Algo deu errado': ['Something went wrong', 'Algo salió mal'],
    'Sua mensagem': ['Your message', 'Tu mensaje'],
    'O que você gostaria que o Volum fizesse?': ['What would you like Volum to do?', '¿Qué te gustaría que hiciera Volum?'],
    'O que aconteceu? O que você estava fazendo na hora?': ['What happened? What were you doing at the time?', '¿Qué pasó? ¿Qué estabas haciendo en ese momento?'],
    'E-mail <small>(opcional, só se quiser resposta)</small>': ['Email <small>(optional, only if you want a reply)</small>', 'Correo <small>(opcional, solo si quieres respuesta)</small>'],
    'voce@email.com': ['you@email.com', 'tu@email.com'],
    'Não precisa de cadastro.': ['No sign-up needed.', 'No necesitas registrarte.'],
    'Enviar': ['Send', 'Enviar'],
    'Usa o GitHub? Você também pode <a href="https://github.com/esdrasstm/Volum/issues/new">abrir uma issue</a>.':
      ['On GitHub? You can also <a href="https://github.com/esdrasstm/Volum/issues/new">open an issue</a>.', '¿Usas GitHub? También puedes <a href="https://github.com/esdrasstm/Volum/issues/new">abrir un issue</a>.'],
    'Escreva sua mensagem antes de enviar.': ['Write your message before sending.', 'Escribe tu mensaje antes de enviar.'],
    'Enviando...': ['Sending...', 'Enviando...'],
    'Valeu! Sua mensagem chegou.': ['Thanks! Your message got through.', '¡Gracias! Tu mensaje llegó.'],
    'Não deu para enviar agora. Confira a internet e tente de novo.': ['Couldn\'t send it right now. Check your connection and try again.', 'No se pudo enviar ahora. Revisa tu conexión e inténtalo de nuevo.'],
    'Vai junto: {0}. Não precisa de cadastro.': ['Sent along: {0}. No sign-up needed.', 'Se envía también: {0}. No necesitas registrarte.'],

    // Final
    'Seu som, do seu jeito.': ['Your sound, your way.', 'Tu sonido, a tu manera.'],
    'Baixe o Volum e deixe o mixer do Windows a um clique de distância.': ['Download Volum and keep the Windows mixer one click away.', 'Descarga Volum y ten el mezclador de Windows a un clic de distancia.'],

    // Apoio (a janela do Pix só abre no Brasil, mas os textos ficam prontos)
    'Fechar': ['Close', 'Cerrar'],
    'Apoiar o Volum ♥': ['Support Volum ♥', 'Apoyar a Volum ♥'],

    // Rodapé
    'Feito por Esdras': ['Made by Esdras', 'Hecho por Esdras'],
    'Todas as versões': ['All versions', 'Todas las versiones'],
    'Privacidade': ['Privacy', 'Privacidad'],
  };

  const norm = (s) => s.replace(/\s+/g, ' ').trim();
  const tr = (pt) => {
    if (LANG === 'pt') return pt;
    const v = T[norm(pt)];
    return v ? v[LANG === 'en' ? 0 : 1] : pt;
  };

  function apply(root) {
    if (LANG === 'pt') return;
    // 1) Elementos cujo conteúdo inteiro é um texto conhecido (pode ter <span>, <kbd>, <a> dentro)
    const done = new Set();
    root.querySelectorAll('title, h1, h2, h3, p, summary, span, a, label, button, div, b, small').forEach((el) => {
      for (let p = el.parentElement; p; p = p.parentElement) if (done.has(p)) return;
      const key = norm(el.innerHTML);
      if (key && T[key]) { el.innerHTML = tr(key); done.add(el); }
    });
    // 2) Textos soltos ao lado de ícones (ex.: "Baixar para Windows" depois do <svg>)
    const walker = document.createTreeWalker(root.body || root, NodeFilter.SHOW_TEXT);
    for (let n; (n = walker.nextNode());) {
      const key = norm(n.nodeValue);
      if (!key) continue;
      if (T[key]) { n.nodeValue = n.nodeValue.replace(n.nodeValue.trim(), tr(key)); continue; }
      // "© 2026 Volum · Feito por Esdras": troca só o pedaço conhecido
      for (const part of [' Feito por Esdras']) if (n.nodeValue.includes(part)) n.nodeValue = n.nodeValue.replace(part.trim(), tr(part.trim()));
    }
    // 3) Atributos (leitores de tela, dicas, exemplos dos campos, rótulos da barra de tarefas)
    root.querySelectorAll('[aria-label], [title], [placeholder], [alt], [data-label], meta[name="description"]').forEach((el) => {
      for (const a of ['aria-label', 'title', 'placeholder', 'alt', 'data-label', 'content']) {
        const v = el.getAttribute(a);
        if (v && T[norm(v)]) el.setAttribute(a, tr(v));
      }
    });
    document.title = tr(document.title);
  }

  document.documentElement.lang = LANG === 'pt' ? 'pt-BR' : LANG;
  window.SiteI18n = { LANG, tr, apply };
})();
