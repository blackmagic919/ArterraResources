#!/usr/bin/env python3
"""Fetch one verified GitHub release into ignored build storage; never use /latest."""
import hashlib
import io
import json
from pathlib import Path
import shutil
import tarfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
lock = json.loads((ROOT / 'litenetlib-release.json').read_text())
destination = ROOT / '.cache' / 'litenetlib'
marker = destination / '.release-lock.json'
if marker.exists() and json.loads(marker.read_text()) == lock and (destination / 'LiteNetLib/LiteNetLib.csproj').exists():
    print(f"Using cached LiteNetLib {lock['tag']}")
    raise SystemExit(0)


def download(url):
    request = urllib.request.Request(url, headers={
        'User-Agent': 'ArterraDiscovery-build',
    })
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


# Resolve release metadata when updating the lock, not on every clean build.
# This is the archive endpoint used by GitHub's release tarball redirect.
archive_url = f"https://codeload.github.com/{lock['owner']}/{lock['repository']}/legacy.tar.gz/refs/tags/{lock['tag']}"
archive = download(archive_url)
if hashlib.sha256(archive).hexdigest() != lock['sha256']:
    raise RuntimeError('LiteNetLib archive checksum mismatch; review the release before updating the lock')

if destination.exists():
    shutil.rmtree(destination)
destination.mkdir(parents=True)
with tarfile.open(fileobj=io.BytesIO(archive), mode='r:gz') as source:
    for member in source.getmembers():
        parts = Path(member.name).parts
        if len(parts) < 2:
            continue
        relative = Path(*parts[1:])
        if relative.is_absolute() or '..' in relative.parts or member.issym() or member.islnk():
            raise RuntimeError(f'Unexpected archive path: {member.name}')
        target = destination / relative
        if member.isdir():
            target.mkdir(parents=True, exist_ok=True)
        elif member.isfile():
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.extractfile(member) as data, target.open('wb') as output:
                shutil.copyfileobj(data, output)
marker.write_text(json.dumps(lock, indent=2) + '\n')
print(f"Fetched and verified LiteNetLib {lock['tag']}")
