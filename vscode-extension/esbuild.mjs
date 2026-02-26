import * as esbuild from "esbuild";
import * as fs from "fs";
import * as path from "path";

const isProduction = process.argv.includes("--production");
const isWatch = process.argv.includes("--watch");

// Bundle the extension host (Node.js context)
const extensionConfig = {
  entryPoints: ["src/extension.ts"],
  bundle: true,
  outfile: "dist/extension.js",
  external: ["vscode", "node-pty"],
  format: "cjs",
  platform: "node",
  target: "node18",
  sourcemap: !isProduction,
  minify: isProduction,
};

// Copy webview files (browser context — not bundled with extension)
function copyWebviewFiles() {
  const webviewDist = "dist/webview";
  if (!fs.existsSync(webviewDist)) {
    fs.mkdirSync(webviewDist, { recursive: true });
  }
  fs.copyFileSync("src/webview/main.js", path.join(webviewDist, "main.js"));
  fs.copyFileSync("src/webview/index.html", path.join(webviewDist, "index.html"));
}

async function main() {
  copyWebviewFiles();

  if (isWatch) {
    const ctx = await esbuild.context(extensionConfig);
    await ctx.watch();
    console.log("Watching for changes...");

    // Also watch webview files
    fs.watch("src/webview", (_event, filename) => {
      if (filename) {
        console.log(`Webview file changed: ${filename}`);
        copyWebviewFiles();
      }
    });
  } else {
    await esbuild.build(extensionConfig);
    console.log("Build complete.");
  }
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
