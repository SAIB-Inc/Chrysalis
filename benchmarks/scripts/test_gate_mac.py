"""Desk admission and bytes-only evidence checks for the native gate."""
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
from unittest import TestCase
from unittest.mock import patch

import gate
import gate_mac


def spec(*ids, kind='direct'):
    return dict(kind=kind, target='a'*40, candidateCommit='b'*40, candidateTree='c'*40,
                rows=[dict(id=key, kind=kind) for key in ids])


class MacGateTests(TestCase):
    def test_uncatalogued_raw_blocks_is_paired_and_absent_probe_needs_first_baseline(self):
        want = spec('EncodingBench.RawBlocks', 'Typo.Absent')
        known = {'CoreBench.Sign'}
        inventory = ['CoreBench.Sign', 'EncodingBench.RawBlocks']
        rows, _ = gate_mac.requested_rows(gate, gate_mac.admission(json.dumps(want)))
        self.assertEqual(rows[0]['rule'], 'D39')
        with self.assertRaisesRegex(ValueError, 'Missing or malformed target probe inventory'):
            gate.verify_mac_probe_inventory(rows, known, None)
        with self.assertRaisesRegex(ValueError, 'Typo.Absent is not a target probe'):
            gate.verify_mac_probe_inventory(rows, known, inventory)
        want['rows'][1]['firstBaseline'] = True
        rows, _ = gate_mac.requested_rows(gate, want)
        self.assertEqual(rows[1]['rule'], 'baseline')
        gate.verify_mac_probe_inventory(rows, known, inventory)
        want['rows'][0]['firstBaseline'] = True
        rows, _ = gate_mac.requested_rows(gate, want)
        with self.assertRaisesRegex(ValueError, 'EncodingBench.RawBlocks resolves on the target'):
            gate.verify_mac_probe_inventory(rows, known, inventory)

    def test_catalogue_supplies_count_and_refuses_lower(self):
        want = spec('FieldReadBench.ReadEveryField', 'CoreBench.PrimitiveIntRead')
        rows, _ = gate_mac.requested_rows(gate, want)
        self.assertEqual([row['pairs'] for row in rows], [16, 3])
        want['rows'][0]['pairs'] = 3
        with self.assertRaisesRegex(ValueError, 'at least 16'):
            gate_mac.requested_rows(gate, want)
        want['rows'][0]['pairs'] = 20
        self.assertEqual(gate_mac.requested_rows(gate, want)[0][0]['pairs'], 20)

    def test_replay_cannot_masquerade_as_a_direct_gate(self):
        with self.assertRaisesRegex(ValueError, 'one job kind'):
            gate_mac.admission(json.dumps(spec('CoreBench.Sign', 'replay/tcp/steady')))
        with self.assertRaisesRegex(ValueError, 'literal row ids'):
            gate_mac.admission(json.dumps(spec('CoreBench.Sign', 'Ty*')))

    def test_indicative_endpoint_failure_does_not_block_direct_job(self):
        with tempfile.TemporaryDirectory() as home, patch.object(Path, 'home', return_value=Path(home)):
            tcp = spec('replay/tcp/steady', kind='replay')
            with gate_mac.tcp_slot(tcp):
                with self.assertRaisesRegex(ValueError, 'already in use'), gate_mac.tcp_slot(tcp):
                    self.fail('second TCP job should not run')
                with gate_mac.tcp_slot(spec('CoreBench.Sign')):
                    self.assertTrue(True)

    def test_truncated_selection_rejected(self):
        with self.assertRaisesRegex(ValueError, 'differ from the admission'):
            gate_mac.verify_rules(gate, dict(rows=[]), spec('CoreBench.Sign'))

    def test_target_bands_cannot_be_reassigned_between_rows(self):
        want = spec('FieldReadBench.ReadEveryField', 'TxBench.ScriptBuildFeeSign')
        rows, config = gate_mac.requested_rows(gate, want)
        for row in rows:
            row['sides'] = dict(baseline=dict(commit=want['target'], treeId='base', runtime='11.0.0'),
                                candidate=dict(commit=want['candidateCommit'], treeId=want['candidateTree'], runtime='11.0.0'))
            row['macInvocations'] = json.loads((gate.GATE/'mac-direct-counts.json').read_text())[row['id']]
            row['corpusId'] = gate.corpus_identity(row, {})
        gate.measurement_groups(rows)
        bands = gate.seal_bands(rows, allocation_platform='mac')
        seal = dict(rows=rows, bands=bands, ruleVersion=gate.stats.RULE_VERSION, gated=config)
        self.assertEqual({band['record']['absoluteNoiseBandBytes'] for band in bands.values()}, {0, 109.79789179378092})
        with patch.object(gate, 'git', return_value='base'):
            gate_mac.verify_rules(gate, seal, want)
            rows[0]['bandKey'], rows[1]['bandKey'] = rows[1]['bandKey'], rows[0]['bandKey']
            with self.assertRaisesRegex(ValueError, 'row band assignment'):
                gate_mac.verify_rules(gate, seal, want)

    def test_cap_log_requires_samples_and_consistency(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            log = root/'memory.log'
            header = 'host_bytes=34359738368 limit_kib=9437184 sample_seconds=1.0\n'
            sample = '2026-11-19T01:00:00+00:00 rss_kib=4000 peak_kib=4000 processes=2\n'
            footer = 'peak_kib=4000 samples=1 cap_hit=0\n'
            for text in (header + footer, header + sample + footer.replace('samples=1','samples=2'),
                         header + sample + 'SAMPLER FAILED; job group killed\n' + footer,
                         header + sample + footer.replace('4000','4001')):
                with self.subTest(text=text), self.assertRaises(ValueError):
                    log.write_text(text)
                    gate_mac.verify_memory(root)
            log.write_text(header + sample + footer)
            gate_mac.verify_memory(root)

    def test_first_baseline_uncapped_never_passes_and_desk_zero_cap_goes_red(self):
        row = dict(id='NewBench.Example', firstBaseline=True, kind='direct', pairs=3, macInvocations=128)
        data = [dict(name=row['id'], samples=[dict(bytes=32, invocations=128, batch=i) for i in range(gate.DIRECT_BATCH_COUNT)])]
        pairs = [dict(pair=i, candidate=dict(platform='mac', data=data)) for i in range(3)]
        seal = dict(rows=[row], allocationPlatform='mac')
        result = gate.first_baseline_result(row, pairs)
        self.assertEqual((result['status'], result['candidateBatchBytes']), ('BASELINE', [[32]*5]*3))
        self.assertEqual(gate.outcome(seal, {row['id']: result}), 'FIRST BASELINES REQUIRE DESK ACCEPTANCE')
        self.assertEqual(gate_mac.verdict_exit(seal, dict(rows={row['id']:result})), 1)
        row['maxBytes'] = 32
        capped = gate.first_baseline_result(row, pairs)
        self.assertEqual(capped['status'], 'BASELINE')
        self.assertEqual(gate_mac.verdict_exit(seal, dict(rows={row['id']:capped})), 0)
        row['maxBytes'] = 0
        result = gate.first_baseline_result(row, pairs)
        self.assertEqual(gate.outcome(seal, {row['id']: result}), 'FAIL')
        self.assertEqual(gate_mac.verdict_exit(seal, dict(rows={row['id']:result})), 1)
        self.assertIn('desk cap, not PC parity', result['reason'])
        self.assertEqual(gate.first_baseline_result(row, pairs[:-1])['status'], 'INCONCLUSIVE')
        self.assertEqual(gate.first_baseline_result(row, [dict(p, candidate=dict(platform='mac', data=[])) for p in pairs])['status'], 'INCONCLUSIVE')
        data[0]['samples'].pop()
        self.assertEqual(gate.first_baseline_result(row, pairs)['status'], 'INCONCLUSIVE')

    def test_duplicate_missing_and_non_integer_batch_ids_fail_closed(self):
        row = dict(id='NewBench.Example', kind='direct', pairs=3, macInvocations=128)
        for bad in ([0, 1, 2, 3, 0], [0, 1, 2, 4, 3], [False, 1, 2, 3, 4], [0, 1, 2, 3, 4.0]):
            data = [dict(name=row['id'], samples=[dict(bytes=32, invocations=128, batch=i) for i in bad])]
            pairs = [dict(pair=i, candidate=dict(platform='mac', data=data)) for i in range(3)]
            for requirements in ({}, {'maxBytes':32}):
                with self.subTest(bad=bad, requirements=requirements):
                    got = gate.first_baseline_result(dict(row, **requirements), pairs)
                    self.assertEqual(got['status'], 'INCONCLUSIVE')
                    self.assertIn('Mac batch IDs', got['reason'])

    def test_default_runner_still_uses_original_time_calibration(self):
        source = (gate.REPO / gate_mac.HARNESS / 'Program.cs').read_text()
        # The timed default enters with no fixed counts, exactly as before. This
        # assertion tests the contract without measuring or printing Mac timing.
        self.assertIn('case "direct":\n                DirectMeasurements.Run(remaining.FirstOrDefault() ?? "*",', source)
        self.assertIn('IReadOnlyDictionary<string, int>? fixedCounts = null', source)
        self.assertIn('} while (timer.Elapsed.TotalMilliseconds < 100 && count < 1 << 20);', source)
        self.assertIn('ns = timer.Elapsed.TotalNanoseconds / count / scale,', source)
        branch = source.split('case "mac-bytes":', 1)[1].split('case "validate":',1)[0]
        self.assertIn('RequireNativeMac()', branch)
        self.assertIn('OperatingSystem.IsMacOS()', source)
        self.assertIn('Architecture.Arm64', source)
