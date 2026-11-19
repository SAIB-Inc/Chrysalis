#!/usr/bin/env python3
"""Offline proxy regressions using a local fake Docker client, never a container."""
import importlib.util
import json
import os
from pathlib import Path
import select
import signal
import socket
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import uuid

from proxy_test_support import child_pids


PROXY = Path(__file__).with_name("node-proxy.py")
UNTRUSTED = (
    "CHRYSALIS_WALLET_KEY_FILE", "CHRYSALIS_BLOCKFROST_KEY_FILE",
    "CHRYSALIS_IT_FUNDED", "DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_CONFIG",
)


def process_exists(pid):
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False


class ProxyLifecycleTests(unittest.TestCase):
    def run_interrupted_proxy(self, interrupt):
        with tempfile.TemporaryDirectory(prefix="chrysalis-proxy-safety-") as directory:
            root = Path(directory)
            record = root / "fake-child.json"
            docker = root / "docker"
            docker.write_text(
                f"#!{sys.executable}\n"
                "import json, os, signal, sys, time\n"
                "if 'chrysalis-integration-proxy-cleanup' in sys.argv:\n"
                "    index = sys.argv.index('chrysalis-integration-proxy-cleanup')\n"
                "    pid = int(sys.argv[index + 1])\n"
                "    action = sys.argv[index + 4]\n"
                "    raise SystemExit(0 if not os.path.exists('/proc/' + str(pid)) else (67 if action == 'CHECK' else 65))\n"
                "signal.signal(signal.SIGINT, signal.SIG_IGN)\n"
                f"open({str(record)!r}, 'w').write(json.dumps({{'pid': os.getpid(), 'pgid': os.getpgrp(), "
                f"'arguments': sys.argv[1:], 'untrusted_names': [name for name in {UNTRUSTED!r} if name in os.environ]}}))\n"
                "nonce = sys.argv[-1]\n"
                "start = open('/proc/self/stat').read().rsplit(') ', 1)[1].split()[19]\n"
                "os.write(2, f'CHRYSALIS_PROXY_READY {nonce} {os.getpid()} {start}\\n'.encode())\n"
                "ack = bytearray()\n"
                "while not ack.endswith(b'\\n'):\n"
                "    part = os.read(0, 1)\n"
                "    if not part: raise SystemExit(64)\n"
                "    ack.extend(part)\n"
                "assert bytes(ack) == ('CHRYSALIS_PROXY_ACK ' + nonce + '\\n').encode()\n"
                "while True:\n"
                "    data = os.read(0, 65536)\n"
                "    if not data: break\n"
                "    os.write(1, data)\n"
                "time.sleep(0.5)  # Require the parent to join, not merely close stdin.\n"
            )
            docker.chmod(0o755)
            with socket.socket() as port_holder:
                port_holder.bind(("127.0.0.1", 0))
                port = port_holder.getsockname()[1]
            environment = {
                "PATH": str(root) + os.pathsep + os.defpath,
                "HOME": directory,
                **dict.fromkeys(UNTRUSTED, "synthetic-not-a-real-credential"),
            }
            proxy = subprocess.Popen(
                [sys.executable, str(PROXY), "--container", "offline-fake-node", "--listen-port", str(port)],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=environment, start_new_session=True,
            )
            child = None
            try:
                self.assertTrue(select.select([proxy.stdout], [], [], 5)[0], "Proxy did not announce readiness")
                self.assertIn(b"listening", proxy.stdout.readline())
                with socket.create_connection(("127.0.0.1", port), timeout=5) as client:
                    client.sendall(b"public-proxy-probe")
                    self.assertEqual(b"public-proxy-probe", client.recv(65536))
                    child = json.loads(record.read_text())
                    self.assertNotEqual(proxy.pid, child["pgid"], "Docker child must not share the terminal signal group")
                    os.killpg(proxy.pid, interrupt)
                    self.assertEqual(0, proxy.wait(timeout=10), "Interrupt must perform orderly shutdown")
                    self.assertFalse(process_exists(child["pid"]), "Proxy exited before joining its Docker child")
                    self.assertEqual(b"", client.recv(1))
                    self.assertEqual([], child["untrusted_names"], "Docker inherited credential/remote-context configuration")
                    self.assertEqual(["--host", "unix:///var/run/docker.sock"], child["arguments"][:2])
            finally:
                if proxy.poll() is None:
                    proxy.kill()
                    proxy.wait(timeout=5)
                if child is None and record.exists():
                    child = json.loads(record.read_text())
                if child is not None and process_exists(child["pid"]):
                    os.kill(child["pid"], signal.SIGTERM)
                proxy.stdout.close()
                proxy.stderr.close()

    def test_sigint_joins_active_relay_and_isolates_environment(self):
        self.run_interrupted_proxy(signal.SIGINT)

    def test_sigterm_joins_active_relay_and_isolates_environment(self):
        self.run_interrupted_proxy(signal.SIGTERM)

    def test_sighup_joins_active_relay_and_isolates_environment(self):
        self.run_interrupted_proxy(signal.SIGHUP)

    def test_container_shell_eof_reaps_both_cats(self):
        spec = importlib.util.spec_from_file_location("integration_node_proxy", PROXY)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            listener.settimeout(5)

            def echo():
                client, _ = listener.accept()
                with client:
                    while data := client.recv(65536):
                        client.sendall(data)

            server = threading.Thread(target=echo, daemon=False)
            server.start()
            nonce = uuid.uuid4().hex
            child = subprocess.Popen(
                ["bash", "-c", module.CONTAINER_RELAY, "chrysalis-integration-proxy", str(listener.getsockname()[1]), nonce],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                env=module.docker_environment(),
            )
            try:
                identity = child.stderr.readline().decode("ascii").split()
                self.assertEqual(["CHRYSALIS_PROXY_READY", nonce, str(child.pid)], identity[:3])
                child.stdin.write(("CHRYSALIS_PROXY_ACK " + nonce + "\n").encode("ascii"))
                child.stdin.write(b"public-shell-probe")
                child.stdin.flush()
                self.assertEqual(b"public-shell-probe", child.stdout.read(len(b"public-shell-probe")))
                children = child_pids(child.pid)
                self.assertEqual(2, len(children), "Both directions must have a live cat before shutdown")
                child.stdin.close()
                self.assertEqual(0, child.wait(timeout=5))
                self.assertTrue(all(not process_exists(pid) for pid in children), "Container relay left a cat behind")
            finally:
                if child.poll() is None:
                    child.terminate()
                    child.wait(timeout=5)
                child.stdin.close()
                child.stdout.close()
                child.stderr.close()
                server.join(timeout=5)
                self.assertFalse(server.is_alive())


if __name__ == "__main__":
    unittest.main()
