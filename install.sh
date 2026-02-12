#!/bin/bash
# LazyNuGet Quick Install Script
# Downloads and installs the latest release from GitHub
# Supports Linux and macOS (x64 and ARM64)
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

REPO="nickprotop/lazynuget"

echo "Installing LazyNuGet from latest release..."
echo ""

# 1. Detect OS and architecture
OS=$(uname -s)
ARCH=$(uname -m)

case "$OS" in
    Linux)
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
        INSTALL_DIR="$HOME/.local"
        ;;
    Darwin)
        case "$ARCH" in
            x86_64)
                BINARY_NAME="lazynuget-osx-x64"
                ;;
            arm64)
                BINARY_NAME="lazynuget-osx-arm64"
                ;;
            *)
                echo "Error: Unsupported architecture: $ARCH"
                echo "Supported: x86_64, arm64"
                exit 1
                ;;
        esac
        # Prefer /usr/local/bin on macOS, fall back to ~/.local/bin
        if [ -w /usr/local/bin ]; then
            INSTALL_DIR="/usr/local"
        else
            INSTALL_DIR="$HOME/.local"
        fi
        ;;
    *)
        echo "Error: Unsupported operating system: $OS"
        echo "Supported: Linux, macOS (Darwin)"
        echo "For Windows, use install.ps1 instead."
        exit 1
        ;;
esac

echo "Detected OS: $OS ($ARCH)"
echo "Binary to download: $BINARY_NAME"
echo "Install directory: $INSTALL_DIR/bin"
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

# 6. macOS: Clear Gatekeeper quarantine flag
if [ "$OS" = "Darwin" ]; then
    xattr -d com.apple.quarantine "$INSTALL_DIR/bin/lazynuget" 2>/dev/null || true
    echo "✓ Cleared macOS Gatekeeper quarantine flag"
fi

# 7. Download and install uninstaller
echo "Downloading uninstaller..."
if curl -L -f -o "/tmp/lazynuget-uninstall" "$UNINSTALL_URL" 2>/dev/null; then
    chmod +x "/tmp/lazynuget-uninstall"
    mv "/tmp/lazynuget-uninstall" "$INSTALL_DIR/bin/lazynuget-uninstall"
    echo "✓ Installed uninstaller to $INSTALL_DIR/bin/lazynuget-uninstall"
else
    echo "Warning: Could not download uninstaller (non-critical)"
fi

# 8. Add to PATH if needed
PATH_ADDED=false
if ! echo "$PATH" | grep -q "$INSTALL_DIR/bin"; then
    echo ""

    # Determine the shell config file
    SHELL_RC=""
    if [ "$OS" = "Darwin" ]; then
        # macOS: prefer .zshrc (default shell since Catalina), then .bash_profile
        if [ -f "$HOME/.zshrc" ]; then
            SHELL_RC="$HOME/.zshrc"
        elif [ -f "$HOME/.bash_profile" ]; then
            SHELL_RC="$HOME/.bash_profile"
        fi
    else
        # Linux: prefer .bashrc, then .zshrc
        if [ -f "$HOME/.bashrc" ]; then
            SHELL_RC="$HOME/.bashrc"
        elif [ -f "$HOME/.zshrc" ]; then
            SHELL_RC="$HOME/.zshrc"
        fi
    fi

    if [ -n "$SHELL_RC" ]; then
        echo "export PATH=\"$INSTALL_DIR/bin:\$PATH\"" >> "$SHELL_RC"
        echo "✓ Added $INSTALL_DIR/bin to PATH in $SHELL_RC"
        PATH_ADDED=true
    else
        echo "Warning: Could not detect shell config file"
        echo "         Add manually: export PATH=\"$INSTALL_DIR/bin:\$PATH\""
    fi
else
    echo "✓ $INSTALL_DIR/bin is already in PATH"
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
    echo "  $INSTALL_DIR/bin/lazynuget"
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
