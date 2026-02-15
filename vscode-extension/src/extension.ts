import * as vscode from "vscode";
import * as path from "path";
import * as fs from "fs";
import * as os from "os";
import { TerminalBridge } from "./terminal-bridge";

let panel: vscode.WebviewPanel | undefined;
let bridge: TerminalBridge | undefined;

export function activate(context: vscode.ExtensionContext) {
  const openCommand = vscode.commands.registerCommand("lazynuget.open", () => {
    const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!folder) {
      vscode.window.showErrorMessage(
        "LazyNuGet: Open a folder containing .NET projects first."
      );
      return;
    }

    // Reuse existing panel
    if (panel) {
      panel.reveal();
      return;
    }

    const binaryPath = resolveBinaryPath(context);
    if (!binaryPath) {
      vscode.window.showErrorMessage(
        "LazyNuGet: Could not find the lazynuget binary. Check the lazynuget.binaryPath setting."
      );
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
    });
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
  html = html.replace("{{xtermJs}}", xtermJs.toString());
  html = html.replace("{{xtermFitJs}}", xtermFitJs.toString());
  html = html.replace("{{xtermWebglJs}}", xtermWebglJs.toString());
  html = html.replace("{{mainJs}}", mainJs.toString());

  return html;
}

export function deactivate() {
  bridge?.dispose();
  panel?.dispose();
}
