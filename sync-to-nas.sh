#!/bin/bash
# Sync local TazUO repo to NAS share for Windows builds
# Excludes .git, build artifacts, and SQLite DBs

SRC="/home/crameep/LocalProjects/TazUo/"
DEST="/home/crameep/claudecode/TazUO/"

rsync -av --delete \
  --exclude='.git/' \
  --exclude='bin/' \
  --exclude='obj/' \
  --exclude='*.db' \
  --exclude='*.db-shm' \
  --exclude='*.db-wal' \
  --exclude='.beads/' \
  --exclude='.choo-choo-ralph/' \
  "$SRC" "$DEST"

echo "Sync complete: $SRC -> $DEST"
