#!/bin/bash
# LazyNuGet Uninstallation Script
# Supports Linux and macOS
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

# Detect OS and set install directory
OS=$(uname -s)
case "$OS" in
    Darwin)
        # macOS: check both install locations
        if [ -f "/usr/local/bin/lazynuget" ]; then
            INSTALL_DIR="/usr/local"
        else
            INSTALL_DIR="$HOME/.local"
        fi
        ;;
    *)
        INSTALL_DIR="$HOME/.local"
        ;;
esac

BINARY_PATH="$INSTALL_DIR/bin/lazynuget"
UNINSTALL_PATH="$INSTALL_DIR/bin/lazynuget-uninstall"

echo "LazyNuGet Uninstaller"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Check if LazyNuGet is installed
if [ ! -f "$BINARY_PATH" ]; then
    echo "LazyNuGet is not installed (binary not found at $BINARY_PATH)"
    exit 0
fi

echo "This will remove:"
echo "  • Binary: $BINARY_PATH"
[ -f "$UNINSTALL_PATH" ] && echo "  • Uninstaller: $UNINSTALL_PATH"
echo ""
echo "Note: LazyNuGet settings are stored in ~/.config/LazyNuGet/ (Linux)"
echo "      or ~/Library/Application Support/LazyNuGet/ (macOS)."
echo "      These are not removed automatically."
echo ""

read -p "Continue with uninstallation? [y/N] " -n 1 -r < /dev/tty
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    echo "Uninstallation cancelled."
    exit 0
fi

echo ""

# Remove binary
if [ -f "$BINARY_PATH" ]; then
    rm -f "$BINARY_PATH"
    echo "✓ Removed binary: $BINARY_PATH"
else
    echo "⊘ Binary not found: $BINARY_PATH"
fi

# Remove uninstaller
if [ -f "$UNINSTALL_PATH" ]; then
    rm -f "$UNINSTALL_PATH"
    echo "✓ Removed uninstaller: $UNINSTALL_PATH"
else
    echo "⊘ Uninstaller not found: $UNINSTALL_PATH"
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✓ LazyNuGet uninstalled successfully"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# Check if PATH entry still exists
if [ "$OS" = "Darwin" ]; then
    # macOS: check .zshrc first, then .bash_profile
    if [ -f "$HOME/.zshrc" ]; then
        SHELL_CONFIG="$HOME/.zshrc"
    elif [ -f "$HOME/.bash_profile" ]; then
        SHELL_CONFIG="$HOME/.bash_profile"
    fi
else
    if [ -f "$HOME/.bashrc" ]; then
        SHELL_CONFIG="$HOME/.bashrc"
    elif [ -f "$HOME/.zshrc" ]; then
        SHELL_CONFIG="$HOME/.zshrc"
    fi
fi

if [ -n "$SHELL_CONFIG" ]; then
    HAS_PATH=$(grep -c "export PATH.*${INSTALL_DIR}/bin" "$SHELL_CONFIG" 2>/dev/null || true)

    if [ "$HAS_PATH" -gt 0 ]; then
        echo "Note: PATH modification remains in $SHELL_CONFIG:"
        echo "      • export PATH=\"$INSTALL_DIR/bin:\$PATH\""
        echo "      This is harmless but can be manually removed if desired."
        echo ""
    fi
fi
