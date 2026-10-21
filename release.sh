#!/bin/bash
# Publish a new Shastra Guard version. Usage: ./release.sh 1.5.2
# Expects ShastraGuard.cs already updated (CurrentVersion and AssemblyVersion) and committed or not.
set -e
V="$1"; [ -n "$V" ] || { echo "usage: ./release.sh <version>"; exit 1; }
grep -q "CurrentVersion = \"$V\"" ShastraGuard.cs || { echo "ShastraGuard.cs does not say CurrentVersion = \"$V\""; exit 1; }
git add -A && git commit -q -m "Shastra Guard $V" || true
git push -q origin main
SHA=$(git rev-parse HEAD)
printf '%s\r\n' "$V" > version.txt
printf '%s\r\n' "$SHA" > commit.txt
git add version.txt commit.txt && git commit -q -m "Release $V ($SHA)" && git push -q origin main
echo "Released $V from $SHA"
