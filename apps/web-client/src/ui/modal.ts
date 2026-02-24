export interface ModalController {
  show(onContinue: () => void, onStop: () => void, timeoutSeconds: number): void;
  close(): void;
  dispose(): void;
}

export function createModalController(): ModalController {
  let root: HTMLDivElement | null = null;
  let intervalId: number | null = null;
  let keyHandler: ((event: KeyboardEvent) => void) | null = null;
  let closed = true;

  const ensureRoot = (): HTMLDivElement => {
    if (root) return root;

    root = document.createElement("div");
    root.className = "jellycheckr-modal-backdrop";
    root.setAttribute("role", "dialog");
    root.setAttribute("aria-modal", "true");

    root.innerHTML = `
      <div class="jellycheckr-modal">
        <h3>Continue watching?</h3>
        <p class="jellycheckr-countdown"></p>
        <div class="jellycheckr-actions">
          <button class="jellycheckr-continue">Continue watching</button>
          <button class="jellycheckr-stop">Stop</button>
        </div>
      </div>
    `;

    document.body.appendChild(root);
    injectStyles();

    return root;
  };

  const show = (
    onContinue: () => void,
    onStop: () => void,
    timeoutSeconds: number
  ): void => {
    const node = ensureRoot();
    closed = false;

    node.style.display = "flex";

    const continueButton =
      node.querySelector<HTMLButtonElement>(".jellycheckr-continue");
    const stopButton =
      node.querySelector<HTMLButtonElement>(".jellycheckr-stop");
    const countdownNode =
      node.querySelector<HTMLParagraphElement>(".jellycheckr-countdown");

    if (!continueButton || !stopButton || !countdownNode) return;

    let secondsLeft = timeoutSeconds;

    const renderCountdown = () => {
      countdownNode.innerHTML = `Stopping in <strong>${Math.max(
        secondsLeft,
        0
      )}</strong>s`;
    };

    renderCountdown();

    continueButton.focus();

    continueButton.onclick = () => {
      if (closed) return;
      onContinue();
    };

    stopButton.onclick = () => {
      if (closed) return;
      onStop();
    };

    keyHandler = (event: KeyboardEvent): void => {
      if (event.key === "Tab") {
        event.preventDefault();
        if (document.activeElement === continueButton) stopButton.focus();
        else continueButton.focus();
      }

      if (event.key === "ArrowRight") stopButton.focus();
      if (event.key === "ArrowLeft") continueButton.focus();

      if (event.key === "Escape" && !closed) onStop();

      if (
        event.key === "Enter" &&
        document.activeElement === stopButton &&
        !closed
      )
        onStop();

      if (
        event.key === "Enter" &&
        document.activeElement === continueButton &&
        !closed
      )
        onContinue();
    };

    window.addEventListener("keydown", keyHandler);

    if (intervalId !== null) window.clearInterval(intervalId);

    intervalId = window.setInterval(() => {
      secondsLeft -= 1;
      renderCountdown();

      if (secondsLeft <= 0 && !closed) {
        window.clearInterval(intervalId!);
        intervalId = null;
        onStop();
      }
    }, 1000);
  };

  const close = (): void => {
    closed = true;

    if (intervalId !== null) {
      window.clearInterval(intervalId);
      intervalId = null;
    }

    if (root) root.style.display = "none";

    if (keyHandler) {
      window.removeEventListener("keydown", keyHandler);
      keyHandler = null;
    }
  };

  const dispose = (): void => {
    close();
    root?.remove();
    root = null;
  };

  return { show, close, dispose };
}

function injectStyles(): void {
  if (document.getElementById("jellycheckr-modal-style")) return;

  const style = document.createElement("style");
  style.id = "jellycheckr-modal-style";
  style.textContent = `
    .jellycheckr-modal-backdrop {
      position: fixed;
      inset: 0;
      display: none;
      align-items: center;
      justify-content: center;
      background: radial-gradient(circle at center, rgba(0,0,0,0.55) 0%, rgba(0,0,0,0.85) 70%);
      backdrop-filter: blur(6px);
      z-index: 99999;
      animation: jellyFadeIn 220ms ease-out;
    }

    .jellycheckr-modal {
      width: min(520px, 92vw);
      background: linear-gradient(180deg, #141414 0%, #0f0f0f 100%);
      color: #fff;
      border-radius: 10px;
      padding: 28px 28px 24px;
      box-shadow:
        0 20px 60px rgba(0,0,0,0.8),
        inset 0 1px 0 rgba(255,255,255,0.04);
      border: 1px solid rgba(255,255,255,0.08);
      text-align: center;
      transform: translateY(8px) scale(.98);
      animation: jellyModalIn 220ms ease-out forwards;
    }

    .jellycheckr-modal h3 {
      font-size: 1.6rem;
      font-weight: 600;
      letter-spacing: 0.2px;
      margin: 0 0 8px 0;
    }

    .jellycheckr-countdown {
      font-size: 1rem;
      opacity: 0.7;
      margin-bottom: 18px;
    }

    .jellycheckr-countdown strong {
      font-size: 1.4rem;
      opacity: 1;
      margin-left: 4px;
    }

    .jellycheckr-actions {
      display: flex;
      gap: 10px;
      margin-top: 10px;
    }

    .jellycheckr-actions button {
      flex: 1;
      min-height: 44px;
      border-radius: 6px;
      border: none;
      font-size: 0.95rem;
      font-weight: 600;
      cursor: pointer;
      transition: all .18s ease;
    }

    .jellycheckr-continue {
      background: #e50914;
      color: white;
      box-shadow: 0 4px 14px rgba(229,9,20,0.45);
    }

    .jellycheckr-continue:hover,
    .jellycheckr-continue:focus-visible {
      background: #f6121d;
      transform: translateY(-1px);
      box-shadow: 0 6px 18px rgba(229,9,20,0.55);
      outline: none;
    }

    .jellycheckr-stop {
      background: rgba(255,255,255,0.08);
      color: rgba(255,255,255,0.9);
    }

    .jellycheckr-stop:hover,
    .jellycheckr-stop:focus-visible {
      background: rgba(255,255,255,0.16);
      outline: none;
    }

    .jellycheckr-actions button:focus-visible {
      box-shadow: 0 0 0 2px rgba(255,255,255,0.35);
    }

    @keyframes jellyFadeIn {
      from { opacity: 0 }
      to { opacity: 1 }
    }

    @keyframes jellyModalIn {
      to {
        transform: translateY(0) scale(1);
        opacity: 1;
      }
    }
  `;

  document.head.appendChild(style);
}