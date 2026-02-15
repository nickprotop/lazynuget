(function () {
  // @ts-ignore - acquireVsCodeApi is injected by VS Code
  const vscode = acquireVsCodeApi();

  const terminalContainer = document.getElementById("terminal");
  if (!terminalContainer) {
    console.error("Terminal container not found");
    return;
  }

  // Check if xterm.js loaded
  if (typeof Terminal === "undefined") {
    console.error("Terminal is not defined - xterm.js not loaded");
    terminalContainer.innerHTML = `
      <div style="color: var(--vscode-errorForeground, #f48771); padding: 20px; font-family: var(--vscode-font-family);">
        <h3>Failed to load terminal</h3>
        <p>The xterm.js library could not be loaded. Please try:</p>
        <ul>
          <li>Reload the window (Ctrl+R)</li>
          <li>Reinstall the LazyNuGet extension</li>
          <li>Check the browser console for errors</li>
        </ul>
      </div>`;
    return;
  }

  if (typeof FitAddon === "undefined") {
    console.error("FitAddon is not defined");
    terminalContainer.innerHTML = `
      <div style="color: var(--vscode-errorForeground, #f48771); padding: 20px; font-family: var(--vscode-font-family);">
        <h3>Terminal addon missing</h3>
        <p>The FitAddon could not be loaded. Please reload the window or reinstall the extension.</p>
      </div>`;
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

  // Load canvas renderer (required for xterm.js 5.x)
  if (typeof CanvasAddon !== "undefined") {
    const canvasAddon = new CanvasAddon.CanvasAddon();
    terminal.loadAddon(canvasAddon);
    console.log('Canvas renderer loaded');
  } else {
    console.error('CanvasAddon not found!');
  }

  // Try WebGL for much better performance
  if (typeof WebglAddon !== "undefined") {
    try {
      const webglAddon = new WebglAddon.WebglAddon();
      webglAddon.onContextLoss(() => {
        console.log('WebGL context lost, falling back to canvas');
        webglAddon.dispose();
      });
      terminal.loadAddon(webglAddon);
      console.log('WebGL renderer loaded - should be fast now!');
    } catch (e) {
      console.log('WebGL failed, using canvas:', e);
    }
  }

  // Load Unicode11 addon for proper emoji width handling
  if (typeof Unicode11Addon !== "undefined") {
    const unicode11Addon = new Unicode11Addon.Unicode11Addon();
    terminal.loadAddon(unicode11Addon);
    terminal.unicode.activeVersion = '11';
    console.log('Unicode11 addon loaded - emojis should align correctly');
  }

  const fitAddon = new FitAddon.FitAddon();
  terminal.loadAddon(fitAddon);

  console.log('Opening terminal...');
  terminal.open(terminalContainer);

  // Clean up any DOM renderer artifacts
  const domRows = terminalContainer.querySelector('.xterm-rows');
  if (domRows) {
    console.log('Removing DOM renderer artifacts');
    domRows.remove();
  }

  // Wait for next tick to ensure DOM is ready
  setTimeout(() => {
    fitAddon.fit();

    console.log(`Terminal initialized: ${terminal.cols}x${terminal.rows}`);

    // Signal ready to extension host AFTER sizing
    vscode.postMessage({
      type: "ready",
      cols: terminal.cols,
      rows: terminal.rows,
    });

    terminal.focus();

    // Safety timeout: hide loading after 10 seconds even if threshold not met
    setTimeout(() => {
      if (loadingElement && !loadingElement.classList.contains("hidden")) {
        loadingElement.classList.add("hidden");
        console.log("Loading timeout - hiding indicator");
      }
    }, 10000);
  }, 0);

  // User input (keyboard + mouse) → extension host → PTY
  terminal.onData((data) => {
    vscode.postMessage({ type: "input", data });
  });

  // Binary data (mouse events can produce these)
  terminal.onBinary((data) => {
    vscode.postMessage({ type: "input", data });
  });

  // Track if we've received first render
  let dataChunksReceived = 0;
  let totalBytesReceived = 0;
  const loadingElement = document.getElementById("loading");

  // Extension host → terminal render
  window.addEventListener("message", (event) => {
    const msg = event.data;
    switch (msg.type) {
      case "output":
        terminal.write(msg.data);

        dataChunksReceived++;
        totalBytesReceived += msg.data.length;

        // Hide loading after receiving substantial data (multiple chunks + enough bytes)
        if (dataChunksReceived >= 5 && totalBytesReceived > 5000 && loadingElement && !loadingElement.classList.contains("hidden")) {
          // Small delay to let WebGL finish initial render
          setTimeout(() => {
            loadingElement.classList.add("hidden");
            console.log(`Initial render complete (${dataChunksReceived} chunks, ${totalBytesReceived} bytes)`);
          }, 300);
        }
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
})();
