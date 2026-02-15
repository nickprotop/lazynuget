import * as vscode from "vscode";
import * as path from "path";
import * as fs from "fs";
import * as os from "os";
import { TerminalBridge } from "./terminal-bridge";

let panel: vscode.WebviewPanel | undefined;
let bridge: TerminalBridge | undefined;
let statusBarItem: vscode.StatusBarItem | undefined;

export function activate(context: vscode.ExtensionContext) {
  // Create status bar item
  statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
  statusBarItem.command = "lazynuget.open";
  statusBarItem.text = "$(package) LazyNuGet";
  statusBarItem.tooltip = "Open LazyNuGet Package Manager";

  // Only show in .NET projects
  updateStatusBarVisibility();

  // Watch for workspace changes
  context.subscriptions.push(
    vscode.workspace.onDidChangeWorkspaceFolders(() => updateStatusBarVisibility()),
    vscode.workspace.onDidOpenTextDocument(() => updateStatusBarVisibility())
  );

  context.subscriptions.push(statusBarItem);

  const openCommand = vscode.commands.registerCommand("lazynuget.open", async () => {
    const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!folder) {
      const selection = await vscode.window.showErrorMessage(
        "LazyNuGet requires an open folder with .NET projects",
        "Open Folder"
      );
      if (selection === "Open Folder") {
        vscode.commands.executeCommand("vscode.openFolder");
      }
      return;
    }

    // Reuse existing panel
    if (panel) {
      panel.reveal();
      return;
    }

    const binaryPath = resolveBinaryPath(context);
    if (!binaryPath) {
      const selection = await vscode.window.showErrorMessage(
        "LazyNuGet binary not found. Please set the path in settings or reinstall the extension.",
        "Open Settings",
        "Download Binary"
      );
      if (selection === "Open Settings") {
        vscode.commands.executeCommand("workbench.action.openSettings", "lazynuget.binaryPath");
      } else if (selection === "Download Binary") {
        vscode.env.openExternal(vscode.Uri.parse("https://github.com/nickprotop/lazynuget/releases"));
      }
      return;
    }

    panel = vscode.window.createWebviewPanel(
      "lazynuget",
      "LazyNuGet",
      vscode.ViewColumn.One,
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        localResourceRoots: [
          vscode.Uri.joinPath(context.extensionUri, "dist", "webview"),
          vscode.Uri.joinPath(context.extensionUri, "node_modules"),
        ],
      }
    );

    panel.iconPath = vscode.Uri.joinPath(
      context.extensionUri,
      "resources",
      "icon.svg"
    );

    panel.webview.html = getWebviewHtml(panel.webview, context.extensionUri);

    // Wait for xterm.js to report its actual dimensions before spawning
    const messageHandler = panel.webview.onDidReceiveMessage((msg) => {
      switch (msg.type) {
        case "ready":
          startBridge(binaryPath, folder, msg.cols, msg.rows);
          break;
        case "input":
          bridge?.write(msg.data);
          break;
        case "resize":
          bridge?.resize(msg.cols, msg.rows);
          break;
      }
    });

    panel.onDidChangeViewState((e) => {
      if (e.webviewPanel.visible) {
        panel?.webview.postMessage({ type: "focus" });
      }
    });

    panel.onDidDispose(() => {
      messageHandler.dispose();
      bridge?.dispose();
      bridge = undefined;
      panel = undefined;
      if (statusBarItem) {
        statusBarItem.text = "$(package) LazyNuGet";
        statusBarItem.tooltip = "Open LazyNuGet Package Manager";
      }
    });

    // Update status bar when panel is open
    if (statusBarItem) {
      statusBarItem.text = "$(package) LazyNuGet $(check)";
      statusBarItem.tooltip = "LazyNuGet is running - click to focus";
    }
  });

  context.subscriptions.push(openCommand);
}

function startBridge(
  binaryPath: string,
  cwd: string,
  cols: number,
  rows: number
): void {
  bridge = new TerminalBridge();

  bridge.onData((data) => {
    panel?.webview.postMessage({ type: "output", data });
  });

  bridge.onExit((code) => {
    panel?.webview.postMessage({
      type: "output",
      data: `\r\n\x1b[90m[LazyNuGet exited with code ${code}. Close this tab or press any key to restart.]\x1b[0m`,
    });

    // Allow restarting on any keypress
    const restartHandler = panel?.webview.onDidReceiveMessage((msg) => {
      if (msg.type === "input") {
        restartHandler?.dispose();
        bridge?.dispose();
        startBridge(binaryPath, cwd, cols, rows);
      }
    });
  });

  bridge.start({ binaryPath, cwd, cols, rows });
}

function resolveBinaryPath(context: vscode.ExtensionContext): string | undefined {
  // 1. Check user setting
  const configPath = vscode.workspace
    .getConfiguration("lazynuget")
    .get<string>("binaryPath");
  if (configPath && fs.existsSync(configPath)) {
    return configPath;
  }

  // 2. Check bundled binary
  const binaryName = os.platform() === "win32" ? "lazynuget.exe" : "lazynuget";
  const bundledPath = path.join(context.extensionPath, "bin", binaryName);
  if (fs.existsSync(bundledPath)) {
    // Ensure executable permission on Unix
    if (os.platform() !== "win32") {
      try {
        fs.chmodSync(bundledPath, 0o755);
      } catch {
        // May fail if read-only, but binary might already be executable
      }
    }
    return bundledPath;
  }

  // 3. Fallback: check PATH
  const pathDirs = (process.env.PATH || "").split(path.delimiter);
  for (const dir of pathDirs) {
    const fullPath = path.join(dir, binaryName);
    if (fs.existsSync(fullPath)) {
      return fullPath;
    }
  }

  return undefined;
}

function getWebviewHtml(
  webview: vscode.Webview,
  extensionUri: vscode.Uri
): string {
  const xtermCss = webview.asWebviewUri(
    vscode.Uri.joinPath(
      extensionUri,
      "node_modules",
      "@xterm",
      "xterm",
      "css",
      "xterm.css"
    )
  );
  const xtermJs = webview.asWebviewUri(
    vscode.Uri.joinPath(
      extensionUri,
      "node_modules",
      "@xterm",
      "xterm",
      "lib",
      "xterm.js"
    )
  );
  const xtermCanvasJs = webview.asWebviewUri(
    vscode.Uri.joinPath(
      extensionUri,
      "node_modules",
      "@xterm",
      "addon-canvas",
      "lib",
      "addon-canvas.js"
    )
  );
  const xtermFitJs = webview.asWebviewUri(
    vscode.Uri.joinPath(
      extensionUri,
      "node_modules",
      "@xterm",
      "addon-fit",
      "lib",
      "addon-fit.js"
    )
  );
  const xtermWebglJs = webview.asWebviewUri(
    vscode.Uri.joinPath(
      extensionUri,
      "node_modules",
      "@xterm",
      "addon-webgl",
      "lib",
      "addon-webgl.js"
    )
  );
  const xtermUnicodeJs = webview.asWebviewUri(
    vscode.Uri.joinPath(
      extensionUri,
      "node_modules",
      "@xterm",
      "addon-unicode11",
      "lib",
      "addon-unicode11.js"
    )
  );
  const mainJs = webview.asWebviewUri(
    vscode.Uri.joinPath(extensionUri, "dist", "webview", "main.js")
  );
  const cspSource = webview.cspSource;

  // Read the HTML template and replace placeholders
  const htmlPath = path.join(
    extensionUri.fsPath,
    "src",
    "webview",
    "index.html"
  );
  let html = fs.readFileSync(htmlPath, "utf8");

  html = html.replace("{{cspSource}}", cspSource);
  html = html.replace("{{xtermCss}}", xtermCss.toString());
  html = html.replace("{{xtermJs}}", xtermJs.toString());
  html = html.replace("{{xtermCanvasJs}}", xtermCanvasJs.toString());
  html = html.replace("{{xtermFitJs}}", xtermFitJs.toString());
  html = html.replace("{{xtermWebglJs}}", xtermWebglJs.toString());
  html = html.replace("{{xtermUnicodeJs}}", xtermUnicodeJs.toString());
  html = html.replace("{{mainJs}}", mainJs.toString());

  return html;
}

function updateStatusBarVisibility() {
  if (!statusBarItem) return;

  // Check if workspace has .NET project files
  vscode.workspace.findFiles("**/*.{csproj,fsproj,vbproj}", "**/node_modules/**", 1).then(files => {
    if (files.length > 0) {
      statusBarItem?.show();
    } else {
      statusBarItem?.hide();
    }
  });
}

export function deactivate() {
  bridge?.dispose();
  panel?.dispose();
  statusBarItem?.dispose();
}
