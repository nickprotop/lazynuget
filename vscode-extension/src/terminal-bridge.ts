import * as pty from "node-pty";
import * as os from "os";

export interface TerminalBridgeOptions {
  binaryPath: string;
  cwd: string;
  cols: number;
  rows: number;
}

export class TerminalBridge {
  private process: pty.IPty | undefined;
  private readonly onDataCallbacks: Array<(data: string) => void> = [];
  private readonly onExitCallbacks: Array<(code: number) => void> = [];

  start(options: TerminalBridgeOptions): void {
    const shell =
      os.platform() === "win32" ? options.binaryPath.replace(/\//g, "\\") : options.binaryPath;

    this.process = pty.spawn(shell, [options.cwd], {
      name: "xterm-256color",
      cols: options.cols,
      rows: options.rows,
      cwd: options.cwd,
      env: {
        ...process.env,
        TERM: "xterm-256color",
        COLORTERM: "truecolor",
      },
    });

    this.process.onData((data) => {
      for (const cb of this.onDataCallbacks) {
        cb(data);
      }
    });

    this.process.onExit(({ exitCode }) => {
      for (const cb of this.onExitCallbacks) {
        cb(exitCode);
      }
    });
  }

  write(data: string): void {
    this.process?.write(data);
  }

  resize(cols: number, rows: number): void {
    try {
      this.process?.resize(cols, rows);
    } catch {
      // Ignore resize errors on already-exited processes
    }
  }

  onData(callback: (data: string) => void): void {
    this.onDataCallbacks.push(callback);
  }

  onExit(callback: (code: number) => void): void {
    this.onExitCallbacks.push(callback);
  }

  dispose(): void {
    try {
      this.process?.kill();
    } catch {
      // Process may already be dead
    }
    this.process = undefined;
    this.onDataCallbacks.length = 0;
    this.onExitCallbacks.length = 0;
  }
}
