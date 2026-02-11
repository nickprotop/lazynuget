#!/bin/bash
# LazyNuGet Quick Install Script
# Downloads and installs the latest release from GitHub
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

INSTALL_DIR="$HOME/.local"
REPO="nickprotop/lazynuget"

echo "Installing LazyNuGet from latest release..."
echo ""

# 1. Detect architecture
ARCH=$(uname -m)
case "$ARCH" in
    x86_64)
        BINARY_NAME="lazynuget-linux-x64"
        ;;
    aarch64|arm64)
        BINARY_NAME="lazynuget-linux-arm64"
        ;;
    *)
        echo "Error: Unsupported architecture: $ARCH"
        echo "Supported: x86_64, aarch64/arm64"
        exit 1
        ;;
esac

echo "Detected architecture: $ARCH"
echo "Binary to download: $BINARY_NAME"
echo ""

# 2. Get latest release info
echo "Fetching latest release..."
RELEASE_JSON=$(curl -s "https://api.github.com/repos/$REPO/releases/latest")
RELEASE_TAG=$(echo "$RELEASE_JSON" | grep '"tag_name"' | sed -E 's/.*"([^"]+)".*/\1/')

if [ -z "$RELEASE_TAG" ]; then
    echo "Error: Could not fetch latest release information"
    echo "Please check your internet connection or install manually from:"
    echo "https://github.com/$REPO/releases"
    exit 1
fi

echo "Latest release: $RELEASE_TAG"
echo ""

# 3. Construct download URLs
BINARY_URL="https://github.com/$REPO/releases/download/$RELEASE_TAG/$BINARY_NAME"
UNINSTALL_URL="https://github.com/$REPO/releases/download/$RELEASE_TAG/uninstall.sh"

# 4. Create directories
mkdir -p "$INSTALL_DIR/bin"

# 5. Download binary
echo "Downloading LazyNuGet binary..."
if ! curl -L -f -o "/tmp/lazynuget" "$BINARY_URL"; then
    echo "Error: Failed to download binary from $BINARY_URL"
    exit 1
fi

chmod +x "/tmp/lazynuget"
mv "/tmp/lazynuget" "$INSTALL_DIR/bin/lazynuget"
echo "✓ Installed binary to $INSTALL_DIR/bin/lazynuget"

# 6. Download and install uninstaller
echo "Downloading uninstaller..."
if curl -L -f -o "/tmp/lazynuget-uninstall" "$UNINSTALL_URL" 2>/dev/null; then
    chmod +x "/tmp/lazynuget-uninstall"
    mv "/tmp/lazynuget-uninstall" "$INSTALL_DIR/bin/lazynuget-uninstall"
    echo "✓ Installed uninstaller to $INSTALL_DIR/bin/lazynuget-uninstall"
else
    echo "Warning: Could not download uninstaller (non-critical)"
fi

# 7. Add to PATH if needed
PATH_ADDED=false
if ! echo "$PATH" | grep -q "$INSTALL_DIR/bin"; then
    echo ""
    if [ -f "$HOME/.bashrc" ]; then
        echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.bashrc"
        echo "✓ Added ~/.local/bin to PATH in ~/.bashrc"
        SHELL_RC="$HOME/.bashrc"
        PATH_ADDED=true
    elif [ -f "$HOME/.zshrc" ]; then
        echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.zshrc"
        echo "✓ Added ~/.local/bin to PATH in ~/.zshrc"
        SHELL_RC="$HOME/.zshrc"
        PATH_ADDED=true
    else
        echo "Warning: Could not detect shell config file (.bashrc or .zshrc)"
        echo "         Add manually: export PATH=\"\$HOME/.local/bin:\$PATH\""
    fi
else
    echo "✓ ~/.local/bin is already in PATH"
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✓ Installation complete!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

if [ "$PATH_ADDED" = true ]; then
    echo "To get started, either reload your shell config:"
    echo ""
    echo "  source $SHELL_RC"
    echo ""
    echo "Or run directly with the full path:"
    echo ""
    echo "  ~/.local/bin/lazynuget"
    echo ""
else
    echo "Run 'lazynuget' to get started!"
    echo ""
fi

echo "Usage:"
echo "  lazynuget [path]       - Manage NuGet packages (default: current directory)"
echo "  lazynuget --help       - Show all options"
echo ""
echo "To uninstall LazyNuGet:"
echo "  lazynuget-uninstall    - Remove LazyNuGet from your system"
echo ""
echo "Documentation: https://github.com/$REPO"
