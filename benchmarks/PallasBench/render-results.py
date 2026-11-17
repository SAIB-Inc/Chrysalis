import json, statistics, sys
from datetime import datetime
from pathlib import Path
from compare import compilation_during_window
root=Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent
data=json.loads((root/'comparison.json').read_text())
labels={'typed':'Every-field Chrysalis / typed Pallas decode','walk':'SAIB.Cbor reader / minicbor walk','replay':'N2C replay peer, not live-node throughput'}
lines=['# Comparison results', '',
'Informational only; no pass mark. Larger Chrysalis/Rust ratios mean Chrysalis took longer.', '',
'One counted PC session on '+datetime.fromisoformat(data['startedUtc'].replace('Z','+00:00')).strftime('%d %B %Y')+', starting '+data['startedUtc']+'.',
'PC loadavg (1 / 5 / 15 min): start '+ ' / '.join(f'{x:.2f}' for x in data['loadavgStart'])+'; end '+ ' / '.join(f'{x:.2f}' for x in data['loadavgEnd'])+'.',
data['platform']+' / '+data['machine']+', '+data['cpu']+'.',
'Five independent process pairs per workload, alternating AB/BA order. Raw samples and',
'versions are in `comparison.json`; this table is derived from those samples.', '',
'| Matched work | Blocks/iteration | Iterations/process | Chrysalis, µs/block | Rust, µs/block | Chrysalis/Rust | 95% interval |',
'|---|---:|---:|---:|---:|---:|---:|']
for mode,row in data['results'].items():
 divisor=row['blocksPerIteration']*row['iterationsPerProcess']*1000
 left=statistics.mean(row['chrysalisElapsedNs'])/divisor
 right=statistics.mean(row['rustElapsedNs'])/divisor
 ratio=row['ratioChrysalisOverRust']
 lo,hi=ratio['confidence95']
 lines.append(f"| {labels[mode]} | {row['blocksPerIteration']} | {row['iterationsPerProcess']} | {left:.2f} | {right:.2f} | {ratio['mean']:.2f}× | {lo:.2f}–{hi:.2f}× |")
lines += ['',
'Ratios/intervals use G8’s existing arithmetic mean of paired process ratios and',
'two-sided Student-t 95% interval, not the ratio of the displayed mean times.',
'The interval describes this noisy shared-PC session, not a cross-machine claim.', '',
'## Matched work', '',
'- Decode: all 30 pinned Conway blocks, 164 transactions; Chrysalis reads its 129',
'  concrete schema properties through G8’s linked every-field visitor, while Pallas',
'  eagerly decodes its typed block. Rust’s timed result is black-boxed and dropped.',
'- Reader: the same 30 block byte strings; both readers and the independent Python',
'  oracle count exactly 21,051 logical CBOR items per iteration, with complete input',
'  consumption. No ledger objects are constructed. See `README.md` for tag/string rules.',
'- N2C: both actual clients negotiate v16, intersect at slot 139055739, and own the',
'  exact same 29 successors through slot 139056433 (159 transactions per range).',
'  All '+str(data['peerConnections'])+' peer connections completed their recorded variable warmups plus',
'  '+str(data['results']['replay']['iterationsPerProcess'])+' measured ranges each ('+str(sum(data['completedRangesPerConnection']))+' complete ranges in total).',
'  Exact bytes, SHA-256, slot/header hash and transaction counts are checked outside',
'  timing after every range. Only complete ranges/frames may end with clean EOF;',
'  no arbitrary flush sleep or additional timed hand-off was added.', '',
'## Limits and substitution', '',
'Pair (3) is a **replay peer, not live-node throughput**. Point 62 named a live node;',
'with coordinator approval this substitutes a reproducible out-of-process Unix-socket',
'replay so both sides do matched work without a node being the limiting factor.',
'No live node or Docker was started. The stored 30-block corpus includes the anchor,',
'so there are exactly 29 stored successors, not 30; this count correction was approved.',
'The Python peer, scheduler, socket and per-block ownership costs remain in this',
'result. It must not be presented as the live-node ratio or the twofold goal’s verdict.', '',
'Pallas '+data['pallas']+'; direct minicbor '+data['minicbor']+'; SAIB.Cbor '+data['saibCbor']+';',
'.NET SDK '+data['dotnetSdk']+' / '+data['dotnetRuntime']+', '+('server' if data['serverGC'] else 'workstation')+' GC;',
data['rustc']+', release profile; Python '+data['python']+'. Full transitive Rust versions/checksums',
'are pinned in `Cargo.lock`. Official crates were downloaded once to the update’s',
'benchmark-only cache; no toolchain upgrade or system-wide installation was needed.',
'Rust uses Tokio’s default multi-thread runtime, matching the existing Pallas N2C',
'comparator (D13), not a current-thread runtime. Replay uses preprod magic 1.',
f"The N2C ratio depends on the Pallas client’s Tokio runtime: its current-thread client took 142.01 µs/block in the superseded session versus {statistics.mean(data['results']['replay']['rustElapsedNs'])/(data['results']['replay']['blocksPerIteration']*data['results']['replay']['iterationsPerProcess']*1000):.2f} µs/block for default multi-thread now, while Rust’s typed row stayed level, so {data['results']['replay']['ratioChrysalisOverRust']['mean']:.2f}× must not be read as ‘Chrysalis’s N2C is faster than Pallas’.",
'Earlier fixed-one-second-warmup figures are superseded: unsettled JIT. They and',
'functional red/smoke runs are retained in review evidence, not used in this table.',
'No previous figures were substituted; only the accepted five-pair session supplies this table.', '']
lines += ['## JIT and window qualification', '',
'C# repeats the actual operation/driver until the compiled-method count stays fixed',
'for '+str(data['results']['typed']['chrysalisJit'][0]['quietSeconds'])+' s, after at least '+str(data['results']['typed']['chrysalisJit'][0]['minimumSeconds'])+' s warming; the cap is '+str(data['results']['typed']['chrysalisJit'][0]['capSeconds'])+' s.',
'A cap failure produces an error and no counted sample. Tiering and PGO are not disabled.',
'Each accepted process window is at least 1 s. Every C# start/end snapshot records',
'JitInfo’s total compiled-method count, compiled IL bytes and compilation time ticks.',
'The approved information-only rule accepts at most '+str(data['compilationLimits']['timeFraction']*100)+'% recorded JIT time and',
str(data['compilationLimits']['ilBytes'])+' compiled IL bytes per window; exceeding either limit rejects the pair.',
'Equality is accepted. These are recorded compilation deltas, not a claim of zero JIT',
'or a direct measurement of all its timing effects. No separate Tier1 counter is claimed.',
'For replay the counters conservatively bracket the entire range sequence, including',
'its untimed checks/intersections; elapsed time sums only the receive/ownership timers.', '',
'| Work / process | Methods compiled | IL bytes compiled | Compilation ticks (100 ns) | JIT time / window, % |',
'|---|---:|---:|---:|---:|']
for mode,row in data['results'].items():
    for process,(sample,elapsed) in enumerate(zip(row['chrysalisJit'],row['chrysalisElapsedNs']),1):
        delta=compilation_during_window(sample,elapsed)
        lines.append(f"| {mode} / {process} | {delta['compiledMethods']} | {delta['ilBytes']} | {delta['compilationTicks']} | {delta['percent']:.6f} |")
lines += ['', 'The typed row retains G8’s unchanged opaque visitor, including its pre-existing',
          'tag/checksum defect; the separate helper repair is not part of X1.', '']
(root/'RESULTS.md').write_text('\n'.join(lines))
