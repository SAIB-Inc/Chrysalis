#!/usr/bin/env python3
"""Trusted native allocation desk entrypoint; no paid timing or candidate scorer."""
import argparse
from contextlib import contextmanager
import datetime as dt
import fcntl
import importlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from types import SimpleNamespace

HARNESS = Path('benchmarks/Chrysalis.Benchmarks')


def admission(raw):
    value = json.loads(raw)
    pins = ('target', 'candidateCommit', 'candidateTree')
    if (not isinstance(value, dict) or value.keys() != {*pins, 'kind', 'rows'}
            or any(not isinstance(value[k], str) or not re.fullmatch('[a-f0-9]{40}', value[k]) for k in pins)
            or value['kind'] not in ('direct', 'replay') or not isinstance(value['rows'], list) or not value['rows']):
        raise ValueError('Invalid Mac allocation admission')
    seen = set()
    for row in value['rows']:
        if (not isinstance(row, dict) or row.keys() - {'pairs', 'firstBaseline', 'maxBytes'} != {'id', 'kind'}
                or row['kind'] != value['kind'] or not isinstance(row['id'], str)
                or not re.fullmatch(r'[A-Za-z0-9_./-]+', row['id']) or row['id'] in seen
                or (row['kind'] == 'replay') != row['id'].startswith('replay/')
                or ('pairs' in row and (type(row['pairs']) is not int or row['pairs'] <= 0))
                or ('firstBaseline' in row and (row['firstBaseline'] is not True or row['kind'] != 'direct'))
                or ('maxBytes' in row and (not row.get('firstBaseline') or type(row['maxBytes']) not in (int, float)
                                           or not math.isfinite(row['maxBytes']) or row['maxBytes'] < 0))):
            raise ValueError('Mac admission requires distinct literal row ids, one job kind and valid counts/caps')
        seen.add(row['id'])
    return value


def requested_rows(gate, spec):
    config = json.loads((gate.GATE / 'gated.json').read_text())
    audit = json.loads((gate.REPO / HARNESS / 'audit-allocations.json').read_text())
    args = SimpleNamespace(mode='change', only=None, plan=None)
    floors = gate.select_rows(args, config, audit, allocation_platform='mac',
                              plan_entries=[{k: v for k, v in row.items() if k != 'pairs'} for row in spec['rows']])
    for row, floor in zip(spec['rows'], floors):
        if 'pairs' in row and row['pairs'] < floor['pairs']:
            raise ValueError(f'{row["id"]} requires at least {floor["pairs"]} catalogue processes/pairs')
        floor['pairs'] = row.get('pairs', floor['pairs'])
    return floors, {k: v for k, v in config.items() if not k.startswith('_')}


def git(repo, *args):
    return subprocess.check_output(['git', '-C', str(repo), *map(str, args)], text=True).strip()


@contextmanager
def trusted_runner(candidate, target):
    if git(candidate, 'rev-parse', target + '^{commit}') != target:
        raise ValueError('The admitted tooling commit is unavailable')
    out = candidate / 'benchmarks/out'
    out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=out, prefix='mac-alloc-target-') as directory:
        root = Path(directory) / 'target'
        git(candidate, 'worktree', 'add', '--detach', root, target)
        try:
            sys.path.insert(0, str(root / 'benchmarks/scripts'))
            try:
                yield importlib.import_module('gate')
            finally:
                sys.path.pop(0)
        finally:
            git(candidate, 'worktree', 'remove', '--force', root)


def collect(run, packet):
    for pattern in ('seal.json', 'seal.id', 'report.json', 'report.txt', 'input/**/*',
                    'data/index.json', 'data/*/pairs.json', 'data/*.log',
                    'data/*/pair-*/*/direct.json', 'data/*/pair-*/*/execution/console.log',
                    'build/*-staging*.log', 'build/target-probes.log'):
        for source in run.glob(pattern):
            if source.is_file():
                target = packet / source.relative_to(run)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(source, target)


def verify_rules(gate, seal, spec, target_probes=None):
    rows, config = requested_rows(gate, spec)
    observed = seal['rows']
    if len(rows) != len(observed):
        raise ValueError('Sealed rows/counts differ from the admission')
    target_tree = gate.git('rev-parse', spec['target'] + '^{tree}')
    audit = json.loads((gate.REPO / HARNESS / 'audit-allocations.json').read_text())
    known = {row['id'] for row in gate.catalogue(config, audit)}
    gate.verify_mac_probe_inventory(rows, known, target_probes)
    if target_probes is not None:
        side = seal['macProbeInventorySide']
        if (side['commit'], side['treeId'], side['runtime']) != (spec['target'], target_tree, '11.0.0'):
            raise ValueError('Target probe inventory does not match the admitted source/runtime')
        if any('baseline' in row['sides'] and side != row['sides']['baseline'] for row in observed):
            raise ValueError('Target inventory was not produced by the paired baseline harness')
    counts = json.loads((gate.GATE / 'mac-direct-counts.json').read_text())
    for wanted, row in zip(rows, observed):
        pins = dict(candidate=(spec['candidateCommit'], spec['candidateTree']))
        if not wanted.get('firstBaseline'):
            pins['baseline'] = (spec['target'], target_tree)
        sides = wanted['sides'] = row['sides']
        if sides.keys() != pins.keys() or any((side['commit'], side['treeId'], side['runtime']) != (*pins[k], '11.0.0')
                                             for k, side in sides.items()):
            raise ValueError('Sealed side pins/runtime differ from the admission')
        if wanted['kind'] == 'direct':
            wanted['macInvocations'] = counts.get(wanted['id'], 128)
            wanted['corpusId'] = gate.corpus_identity(wanted, {})
        else:
            # Mac replay is never scored against a band. Its capture is indicative.
            wanted['corpusId'] = row['corpusId']
    gate.measurement_groups(rows)
    bands = gate.seal_bands(rows, allocation_platform='mac')
    if bands != seal['bands'] or any(a.get('bandKey') != b.get('bandKey') or ('bandKey' in a) != ('bandKey' in b)
                                     for a, b in zip(rows, observed)):
        raise ValueError('Sealed row band assignment or provenance differs from the target')
    if rows != observed or seal['ruleVersion'] != gate.stats.RULE_VERSION or seal['gated'] != config:
        raise ValueError('Sealed row rules, counts or order differ from the admission and target')


def verdict_exit(seal, result):
    for row in seal['rows']:
        got = result['rows'][row['id']]
        if row.get('firstBaseline'):
            if got['status'] != 'BASELINE' or 'candidateBatchBytes' not in got or 'maxBytes' not in row:
                return 1
        else:
            wanted = 'PASS' if row['kind'] == 'direct' else 'INDICATIVE'
            if got['status'] != wanted or 'meanDifferenceBytes' not in got:
                return 1
    return 0


def verify_packet(gate, packet, spec):
    seal = gate.read_seal(packet)
    if seal['allocationPlatform'] != 'mac' or seal['mode'] != 'change' or seal['timePlans']:
        raise ValueError('Not a native bytes-only seal')
    target_probes = (json.loads((packet / 'input/mac-probes.json').read_text())
                     if 'input/mac-probes.json' in seal['files'] else None)
    verify_rules(gate, seal, spec, target_probes)
    assets = {'Program.cs': gate.REPO / HARNESS / 'Program.cs',
              'mac-direct-counts.json': gate.GATE / 'mac-direct-counts.json',
              **{f'scripts/{name}': gate.REPO / 'benchmarks/scripts' / name
                 for name in ('gate_session.py', 'n2n_replay_server.py')}}
    for name, path in assets.items():
        if seal['files'].get(f'input/{name}') != gate.digest(path):
            raise ValueError('Sealed runner inputs differ from target tooling')
    if seal['macRunnerSha256'] != seal['files']['input/Program.cs']:
        raise ValueError('Missing target runner identity')
    result = gate.report(packet)
    if ((packet / 'report.txt').read_text() != result['text'] or json.loads((packet / 'report.json').read_text()) !=
            {key: val for key, val in result.items() if key != 'text'}):
        raise ValueError('Fetched verdict differs from independent rederivation')
    return verdict_exit(seal, result)


def verify_memory(packet):
    lines = (packet / 'memory.log').read_text().splitlines()
    header = re.fullmatch(r'host_bytes=(\d+) limit_kib=(\d+) sample_seconds=1\.0', lines[0]) if lines else None
    footer = re.fullmatch(r'peak_kib=(\d+) samples=(\d+) cap_hit=0', lines[-1]) if lines else None
    if not header or not footer or len(lines) < 3 or int(header[1]) < 32 * 2**30 or int(header[2]) != 9 * 2**20:
        raise ValueError('Missing successful 9 GiB Mac memory-cap evidence')
    peak = 0
    for line in lines[1:-1]:
        sample = re.fullmatch(r'(\S+) rss_kib=(\d+) peak_kib=(\d+) processes=(\d+)', line)
        if not sample or dt.datetime.fromisoformat(sample[1]).utcoffset() != dt.timedelta(0):
            raise ValueError('Malformed Mac memory sample')
        peak = max(peak, int(sample[2]))
        if peak != int(sample[3]):
            raise ValueError('Inconsistent sampled Mac RSS peak')
    if int(footer[2]) != len(lines) - 2 or peak != int(footer[1]) or peak > int(header[2]):
        raise ValueError('Inconsistent Mac memory sample count or maximum')


@contextmanager
def tcp_slot(spec):
    if not any(r['id'].startswith('replay/tcp/') for r in spec['rows']):
        yield
        return
    with (Path.home() / 'mac-alloc-tcp.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise ValueError('Indicative TCP endpoint is already in use') from error
        yield


def run_measurement(gate, candidate, packet, spec):
    corpora, cache = (os.environ.get(k) for k in ('CHRYSALIS_CORPORA', 'NUGET_PACKAGES'))
    if not corpora or not cache or not all(Path(x).is_dir() for x in (corpora, cache)):
        raise ValueError('The Mac corpora and offline package cache are required')
    packet.mkdir(parents=True, exist_ok=True)
    if any(item.name != 'memory.log' for item in packet.iterdir()):
        raise ValueError('Use a fresh Mac allocation evidence ID')
    pin = gate.resolve_side(str(candidate), 'change')
    if (pin['commit'], pin['treeId']) != (spec['candidateCommit'], spec['candidateTree']):
        raise ValueError('The materialized unstaged candidate differs from its approved commit/tree')
    rows, _ = requested_rows(gate, spec)
    entries = [{**entry, 'pairs': row['pairs']} for entry, row in zip(spec['rows'], rows)]
    with tcp_slot(spec), tempfile.TemporaryDirectory(dir=packet.parent, prefix='mac-alloc-run-') as scratch:
        plan = Path(scratch) / 'rows.json'
        plan.write_text(json.dumps(entries))
        run = Path(scratch) / 'measure'
        run.mkdir()
        try:
            gate.run_command(SimpleNamespace(mode='change', only=None, plan=plan, baseline=spec['target'], candidate=str(candidate)),
                             output=run, local=dict(corporaRoot=corpora), allocation_platform='mac')
        except RuntimeError as error:
            for log in run.glob('data/*/pair-*/*/execution/console.log'):
                match = re.search(r'Mac direct probe class [A-Za-z0-9_.+]+ requires a public parameterless Setup\(\)', log.read_text())
                if match:
                    raise ValueError(match[0]) from error
            raise
        finally:
            collect(run, packet)
    return verify_packet(gate, packet, spec)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('run', 'verify'))
    parser.add_argument('candidate', type=Path)
    parser.add_argument('packet', type=Path)
    parser.add_argument('admission_json')
    args = parser.parse_args()
    try:
        candidate, packet = args.candidate.resolve(), args.packet.resolve()
        spec = admission(args.admission_json)
        if args.command == 'run' and (sys.platform != 'darwin' or os.environ.get('CHRYSALIS_MBP_MEMORY_CAP') != '1'):
            raise ValueError('Mac allocation requires mbp-run --memory-log')
        with trusted_runner(candidate, spec['target']) as gate:
            if args.command == 'verify':
                verify_memory(packet)
                return verify_packet(gate, packet, spec)
            return run_measurement(gate, candidate, packet, spec)
    except (ValueError, OSError, RuntimeError, subprocess.CalledProcessError, KeyError) as error:
        parser.exit(2, f'Mac allocation: {error}\n')


if __name__ == '__main__':
    sys.exit(main())
