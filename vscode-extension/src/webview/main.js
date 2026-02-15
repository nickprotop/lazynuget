(function () {
  // @ts-ignore - acquireVsCodeApi is injected by VS Code
  const vscode = acquireVsCodeApi();

  const terminalContainer = document.getElementById("terminal");
  if (!terminalContainer) {
    return;
  }

  // Initialize xterm.js
  const terminal = new Terminal({
    cursorBlink: true,
    cursorStyle: "block",
    fontSize: 14,
    fontFamily: "Menlo, Monaco, 'Courier New', monospace",
    allowProposedApi: true,
    theme: {
      background: getComputedStyle(document.documentElement)
        .getPropertyValue("--vscode-editor-background")
        .trim() || "#1e1e1e",
      foreground: getComputedStyle(document.documentElement)
        .getPropertyValue("--vscode-editor-foreground")
        .trim() || "#d4d4d4",
      cursor: getComputedStyle(document.documentElement)
        .getPropertyValue("--vscode-terminalCursor-foreground")
        .trim() || "#ffffff",
    },
  });

  const fitAddon = new FitAddon.FitAddon();
  terminal.loadAddon(fitAddon);

  // Try to load WebGL addon for better performance, fall back to canvas
  try {
    const webglAddon = new WebglAddon.WebglAddon();
    webglAddon.onContextLoss(() => {
      webglAddon.dispose();
    });
    terminal.loadAddon(webglAddon);
  } catch {
    // WebGL not available, xterm.js falls back to canvas automatically
  }

  terminal.open(terminalContainer);
  fitAddon.fit();

  // User input (keyboard + mouse) → extension host → PTY
  terminal.onData((data) => {
    vscode.postMessage({ type: "input", data });
  });

  // Binary data (mouse events can produce these)
  terminal.onBinary((data) => {
    vscode.postMessage({ type: "input", data });
  });

  // Extension host → terminal render
  window.addEventListener("message", (event) => {
    const msg = event.data;
    switch (msg.type) {
      case "output":
        terminal.write(msg.data);
        break;
      case "focus":
        terminal.focus();
        break;
    }
  });

  // Handle resize: container size changes → recalculate cols/rows → notify host
  const resizeObserver = new ResizeObserver(() => {
    fitAddon.fit();
    vscode.postMessage({
      type: "resize",
      cols: terminal.cols,
      rows: terminal.rows,
    });
  });
  resizeObserver.observe(terminalContainer);

  // Signal ready to extension host
  terminal.focus();
  vscode.postMessage({
    type: "ready",
    cols: terminal.cols,
    rows: terminal.rows,
  });
})();
