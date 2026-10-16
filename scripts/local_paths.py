"""Explicit Windows extended paths; no registry or OS policy changes."""
import os
from pathlib import Path

def local_path(value):
    p = os.fspath(value)
    if os.name == 'nt':
        if not p.startswith('\\\\?\\'):
            p = os.path.abspath(p)
            p = '\\\\?\\UNC\\' + p[2:] if p.startswith('\\\\') else '\\\\?\\' + p
    return Path(p)

def display_path(value):
    p = str(value)
    if p.startswith('\\\\?\\UNC\\'):
        return '\\\\' + p[8:]
    return p[4:] if p.startswith('\\\\?\\') else p
