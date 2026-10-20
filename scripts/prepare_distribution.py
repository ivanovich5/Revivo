"""Create a NEW staging folder; never change an existing installation.

python prepare_distribution.py PREVIOUS_STAGE NEW_STAGE
Only engines invoked by Revivo are retained. Direct DLL dependencies are kept
recursively. Validate metadata AND signature recovery before using the output.
This does not establish license clearance or detect dynamic native loads.
"""
import argparse
import json
from pathlib import Path
import shutil

from audit_distribution import pe_imports


def engine_files(folder, executable_names):
    available = {p.name.lower(): p for p in folder.iterdir() if p.is_file()}
    pending = list(executable_names)
    retained = set()
    external = set()
    while pending:
        name = pending.pop().lower()
        if name in retained:
            continue
        path = available.get(name)
        if path is None:
            raise FileNotFoundError(name)
        retained.add(name)
        for dependency in pe_imports(path):
            if dependency.lower() in available:
                pending.append(dependency)
            else:
                external.add(dependency)
    return [available[name] for name in sorted(retained)], sorted(external)


def prepare(source, destination, tsk_bin=None, photorec_bin=None):
    source = source.resolve(strict=True)
    destination = destination.resolve()
    if destination.exists() or destination.is_relative_to(source):
        raise ValueError('Destination must be new and outside the original stage')
    destination.mkdir(parents=True)
    for name in ('app', 'app-copia-092', 'app-reutilizar', 'python',
                 'platform-notices', 'third-party-source'):
        shutil.copytree(source / name, destination / name,
                        ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
    (destination / 'scripts').mkdir()
    for path in (source / 'scripts').iterdir():
        if path.is_file() and path.suffix in ('.py', '.ps1'):
            shutil.copy2(path, destination / 'scripts' / path.name)
    for name in ('LICENSE.txt', 'THIRD_PARTY.md', 'GUIA-BETA.txt'):
        shutil.copy2(source / name, destination / name)
    evidence = []
    for relative, names in (
        ('testdisk-7.2', ['photorec_win.exe', 'testdisk_win.exe']),
        ('sleuthkit-4.15.0-win32/bin', ['mmls.exe', 'fls.exe', 'icat.exe']),
    ):
        original = source / 'engines' / relative
        if relative.startswith('sleuthkit') and tsk_bin is not None:
            original = tsk_bin.resolve(strict=True)
        if relative.startswith('testdisk') and photorec_bin is not None:
            original = photorec_bin.resolve(strict=True)
        target = destination / 'engines' / relative
        target.mkdir(parents=True)
        retained, external = engine_files(original, names)
        for path in retained:
            shutil.copy2(path, target / path.name)
        evidence.append({'engine': relative, 'retained': [p.name for p in retained],
                         'external_imports': external})
    # ncurses reads terminfo at runtime; it is not a PE import.
    shutil.copytree(source / 'engines/testdisk-7.2/63',
                    destination / 'engines/testdisk-7.2/63')
    # Preserve upstream text notices, including those outside bin/.
    for path in (source / 'engines').rglob('*'):
        if path.is_file() and (path.suffix.lower() in ('.txt', '.md') or
                              any(word in path.name.upper() for word in
                                  ('COPYING', 'LICENSE', 'COPYRIGHT', 'AUTHORS'))):
            target = destination / 'engines' / path.relative_to(source / 'engines')
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
    (destination / 'platform-notices' / 'engine-selection.json').write_text(
        json.dumps(evidence, indent=2), encoding='utf-8')
    return evidence


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    parser.add_argument('--tsk-bin', type=Path, help='Separately built and tested TSK binaries')
    parser.add_argument('--photorec-bin', type=Path, help='PhotoRec/TestDisk and matched DLLs')
    args = parser.parse_args()
    print(json.dumps(prepare(args.source, args.destination, args.tsk_bin, args.photorec_bin), indent=2))
