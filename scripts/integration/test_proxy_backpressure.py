"""Offline blocked-writer regression: real bash/cats, separated fake Docker/container groups."""
import contextlib
import ctypes
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
from unittest import mock
import uuid


PROXY = Path(__file__).with_name("node-proxy.py")


def exists(pid):
    return Path(f"/proc/{pid}").exists()


@contextlib.contextmanager
def reap_private_orphans():
    # A killed fake Docker client must not orphan its separately grouped local "container".
    libc = ctypes.CDLL(None, use_errno=True)
    previous = ctypes.c_int()
    if libc.prctl(37, ctypes.byref(previous), 0, 0, 0) != 0 or libc.prctl(36, 1, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), "Cannot enable subreaping for private regression children")
    try:
        yield
    finally:
        if libc.prctl(36, previous.value, 0, 0, 0) != 0:
            raise OSError(ctypes.get_errno(), "Cannot restore private regression subreaping")


def clean_private_groups(pids):
    for signum in (signal.SIGTERM, signal.SIGKILL):
        for pid in pids:
            if exists(pid):
                try:
                    os.killpg(pid, signum)
                except ProcessLookupError:
                    pass
        time.sleep(0.1)
    for pid in pids:
        try:
            os.waitpid(pid, 0)
        except ChildProcessError:
            pass


def fake_docker_files(root):
    record = root / "relay.json"
    cleanup = root / "cleanup.json"
    fake = root / "docker"
    fake.write_text(
        f"#!{sys.executable}\n"
        f"RECORD = {str(record)!r}\nCLEANUP = {str(cleanup)!r}\n" + r'''
import json, os, subprocess, sys, threading
arguments = sys.argv[1:]
offset = arguments.index("bash")
script = arguments[offset + 2]
parameters = arguments[offset + 3:]
if parameters[0] == "chrysalis-integration-proxy-cleanup":
    open(CLEANUP, "w").write(json.dumps({"arguments": parameters, "environment_names": sorted(os.environ)}))
    result = subprocess.run(["bash", "-c", script, *parameters], stdin=subprocess.DEVNULL, check=False)
    raise SystemExit(result.returncode)
child = subprocess.Popen(["bash", "-c", script, *parameters], stdin=subprocess.PIPE,
                         stdout=subprocess.PIPE, bufsize=0, start_new_session=True)
input_fd = child.stdin.fileno()
open(RECORD, "w").write(json.dumps({"relay_pid": child.pid, "docker_pid": os.getpid(), "parameters": parameters}))
def forward(source, destination):
    try:
        while data := os.read(source, 65536):
            while data:
                data = data[os.write(destination, data):]
    except (OSError, ValueError):
        pass
    finally:
        if destination == input_fd:
            child.stdin.close()
workers = [threading.Thread(target=forward, args=(0, input_fd), daemon=False),
           threading.Thread(target=forward, args=(child.stdout.fileno(), 1), daemon=False)]
for worker in workers:
    worker.start()
code = child.wait()
# Like Docker attach, do not exit until both output streams have been consumed.
for worker in workers:
    worker.join()
raise SystemExit(code if code >= 0 else 128 - code)
''')
    fake.chmod(0o755)
    return record, cleanup


class ProxyBackpressureTests(unittest.TestCase):
    def test_zero_host_exit_independently_checks_live_identified_relay(self):
        spec = importlib.util.spec_from_file_location("proxy_zero_exit", PROXY)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        for allow_cleanup in (True, False):
            with self.subTest(allow_cleanup=allow_cleanup):
                nonce = uuid.uuid4().hex
                shell = subprocess.Popen(
                    ["bash", "-c", module.CONTAINER_RELAY_SETUP + "IFS= read -r -t 30 hold || :\n",
                     "chrysalis-integration-proxy", "1", nonce],
                    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                    env=module.docker_environment(), start_new_session=True,
                )
                host = subprocess.Popen(["true"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.PIPE, start_new_session=True)
                left, right = socket.socketpair()
                relay = module.Relay(left, "offline-no-container", 1, threading.Event())
                relay.child = host
                actions = []
                try:
                    self.assertTrue(select.select([shell.stderr], [], [], 5)[0])
                    identity = shell.stderr.readline().decode("ascii").split()
                    self.assertEqual(["CHRYSALIS_PROXY_READY", nonce, str(shell.pid)], identity[:3])
                    shell.stdin.write(("CHRYSALIS_PROXY_ACK " + nonce + "\n").encode("ascii"))
                    shell.stdin.flush()
                    relay.container_identity = (shell.pid, int(identity[3]))
                    relay.nonce = nonce
                    self.assertEqual(0, host.wait(timeout=5))
                    self.assertIsNone(shell.poll())

                    def local_cleanup(action):
                        actions.append(action)
                        relay.cleanup_actions.append(action)
                        if not allow_cleanup:
                            return False
                        result = subprocess.run(
                            ["bash", "-c", module.CONTAINER_RELAY_CLEANUP, "chrysalis-integration-proxy-cleanup",
                             str(shell.pid), identity[3], nonce, action], stdin=subprocess.DEVNULL,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=module.docker_environment(), timeout=5,
                        )
                        if action != "CHECK" and result.returncode == 0:
                            shell.wait(timeout=5)  # This test owns/reaps the stand-in container supervisor.
                        return result.returncode == 0

                    with mock.patch.object(relay, "cleanup_container", side_effect=local_cleanup):
                        relay.finish_child()
                    print(f"host_exit=0 allow_cleanup={allow_cleanup} cleanup_queries={actions} failed={relay.failed} identified_shell_alive={shell.poll() is None}", flush=True)
                    self.assertIn("CHECK", actions, "Zero host exit bypassed independent container verification")
                    if allow_cleanup:
                        self.assertIn("TERM", actions)
                        self.assertEqual("CHECK", actions[-1])
                        self.assertFalse(relay.failed)
                        self.assertIsNotNone(shell.poll())
                    else:
                        self.assertTrue(relay.failed, "A live identified relay must never be reported as successfully cleaned")
                        self.assertIsNone(shell.poll())
                finally:
                    if shell.poll() is None:
                        shell.terminate()
                    shell.wait(timeout=5)
                    shell.stdin.close()
                    shell.stdout.close()
                    shell.stderr.close()
                    host.wait(timeout=5)
                    host.stdin.close()
                    host.stdout.close()
                    host.stderr.close()
                    left.close()
                    right.close()

    def test_cleanup_refuses_wrong_start_nonce_and_unrelated_exec(self):
        spec = importlib.util.spec_from_file_location("proxy_identity_safety", PROXY)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        nonce = uuid.uuid4().hex
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            listener.listen()
            listener.settimeout(5)
            relay = subprocess.Popen(
                ["bash", "-c", module.CONTAINER_RELAY, "chrysalis-integration-proxy", str(listener.getsockname()[1]), nonce],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=module.docker_environment(),
            )
            unrelated = subprocess.Popen(
                ["bash", "-c", "IFS= read -r -t 20 ignored || :", "unrelated-private-exec"],
                stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=module.docker_environment(),
            )
            peer = None
            try:
                identity = relay.stderr.readline().decode("ascii").split()
                self.assertEqual(["CHRYSALIS_PROXY_READY", nonce, str(relay.pid)], identity[:3])
                start = int(identity[3])
                relay.stdin.write(("CHRYSALIS_PROXY_ACK " + nonce + "\n").encode("ascii"))
                relay.stdin.flush()
                peer, _ = listener.accept()
                deadline = time.monotonic() + 2
                while True:
                    cats = Path(f"/proc/{relay.pid}/task/{relay.pid}/children").read_text().split()
                    if len(cats) == 2 or time.monotonic() >= deadline:
                        break
                    time.sleep(0.01)
                self.assertEqual(2, len(cats))

                def cleanup(pid, ticks, token, action):
                    return subprocess.run(
                        ["bash", "-c", module.CONTAINER_RELAY_CLEANUP, "chrysalis-integration-proxy-cleanup",
                         str(pid), str(ticks), token, action], stdin=subprocess.DEVNULL,
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=module.docker_environment(), timeout=3,
                    ).returncode

                self.assertEqual(65, cleanup(relay.pid, start + 1, nonce, "TERM"))
                self.assertIsNone(relay.poll())
                self.assertEqual(65, cleanup(relay.pid, start, uuid.uuid4().hex, "TERM"))
                self.assertIsNone(relay.poll())
                other_start = Path(f"/proc/{unrelated.pid}/stat").read_text().rsplit(") ", 1)[1].split()[19]
                self.assertEqual(65, cleanup(unrelated.pid, other_start, nonce, "TERM"))
                self.assertIsNone(unrelated.poll())
                # Also exercise the guarded escalation: only this relay's cat children die.
                self.assertEqual(0, cleanup(relay.pid, start, nonce, "KILL_CHILDREN"))
                relay.wait(timeout=5)
                self.assertTrue(all(not exists(int(pid)) for pid in cats))
                self.assertIsNone(unrelated.poll())
                print("wrong_start_refused=True wrong_nonce_refused=True unrelated_exec_untouched=True owned_cats_reaped=True", flush=True)
            finally:
                relay.stdin.close()
                unrelated.stdin.close()
                for process in (relay, unrelated):
                    if process.poll() is None:
                        process.terminate()
                    process.wait(timeout=5)
                    process.stdout.close()
                    process.stderr.close()
                if peer is not None:
                    peer.close()

    def test_nonreading_host_client_drains_output_after_container_cleanup(self):
        spec = importlib.util.spec_from_file_location("proxy_output_backpressure", PROXY)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.CONTAINER_RELAY = module.CONTAINER_RELAY_SETUP + r'''
cat <&0 >/dev/null & writer=$!
cat /dev/zero & reader=$!
wait "$reader"
'''
        with reap_private_orphans(), tempfile.TemporaryDirectory(prefix="chrysalis-output-backpressure-") as directory:
            root = Path(directory)
            record, _ = fake_docker_files(root)
            stopping = threading.Event()
            relay = client = None
            owned = []
            with mock.patch.dict(os.environ, {"PATH": directory + os.pathsep + os.defpath, "HOME": directory}, clear=True):
                try:
                    with socket.socket() as listener:
                        listener.bind(("127.0.0.1", 0))
                        listener.listen()
                        client = socket.socket()
                        client.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4096)
                        client.connect(listener.getsockname())
                        connection, _ = listener.accept()
                        connection.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 4096)
                        relay = module.Relay(connection, "offline-private-container", 3001, stopping)
                        relay.start()
                    deadline = time.monotonic() + 5
                    cats = []
                    while time.monotonic() < deadline:
                        if relay.container_identity is not None:
                            shell = relay.container_identity[0]
                            cats = Path(f"/proc/{shell}/task/{shell}/children").read_text().split()
                            if len(cats) == 2 and any("write" in Path(f"/proc/{pid}/wchan").read_text() for pid in cats):
                                break
                        time.sleep(0.01)
                    else:
                        self.fail("Host non-reader did not establish blocked output")
                    state = json.loads(record.read_text())
                    owned = [state["relay_pid"], state["docker_pid"]]
                    print(f"host_client_reads=0 blocked_output=True shell={shell} cats={cats}", flush=True)
                    stopping.set()
                    relay.stop()
                    relay.join(timeout=40)
                    self.assertFalse(relay.is_alive())
                    self.assertFalse(relay.failed, "Container exit alone cannot join a Docker client with undrained output")
                    self.assertTrue(relay.used_out_of_band_cleanup)
                    self.assertGreater(relay.discarded_output_bytes, 0)
                    self.assertEqual("CHECK", relay.cleanup_actions[-1])
                    self.assertFalse(any(exists(int(pid)) for pid in [str(shell), *cats, str(state["docker_pid"])]))
                    print(f"container_reaped=True docker_joined=True discarded_output_bytes={relay.discarded_output_bytes} host_client_still_open=True", flush=True)
                finally:
                    stopping.set()
                    if relay is not None:
                        relay.stop()
                        relay.join(timeout=40)
                    if not owned and record.exists():
                        state = json.loads(record.read_text())
                        owned = [state["relay_pid"], state["docker_pid"]]
                    clean_private_groups(owned)
                    if client is not None:
                        client.close()

    def test_blocked_writer_cleans_exact_container_relay_before_peer_closes(self):
        with reap_private_orphans(), tempfile.TemporaryDirectory(prefix="chrysalis-proxy-backpressure-") as directory:
            root = Path(directory)
            record, cleanup = fake_docker_files(root)
            with socket.socket() as peer_listener, socket.socket() as port_holder:
                peer_listener.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4096)
                peer_listener.bind(("127.0.0.1", 0))
                peer_listener.listen()
                peer_listener.settimeout(8)
                port_holder.bind(("127.0.0.1", 0))
                proxy_port = port_holder.getsockname()[1]
                port_holder.close()
                environment = {
                    "PATH": directory + os.pathsep + os.defpath,
                    "HOME": directory,
                    "CHRYSALIS_WALLET_KEY_FILE": "synthetic-never-opened",
                    "CHRYSALIS_BLOCKFROST_KEY_FILE": "synthetic-never-opened",
                    "DOCKER_HOST": "synthetic-never-used",
                }
                proxy = subprocess.Popen(
                    [sys.executable, str(PROXY), "--container", "offline-private-container",
                     "--listen-port", str(proxy_port), "--node-port", str(peer_listener.getsockname()[1])],
                    stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=environment, start_new_session=True,
                )
                client = peer = None
                owned = []
                try:
                    self.assertTrue(select.select([proxy.stdout], [], [], 5)[0])
                    self.assertIn(b"listening", proxy.stdout.readline())
                    client = socket.create_connection(("127.0.0.1", proxy_port), timeout=5)
                    client.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 4096)
                    peer, _ = peer_listener.accept()  # Intentionally never read, even during cleanup.
                    peer.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4096)
                    state = json.loads(record.read_text())
                    shell = state["relay_pid"]
                    owned = [shell, state["docker_pid"]]
                    self.assertNotEqual(os.getpgid(shell), os.getpgid(state["docker_pid"]))
                    client.setblocking(False)
                    sent = 0
                    stalled = False
                    deadline = time.monotonic() + 8
                    while time.monotonic() < deadline:
                        if not select.select([], [client], [], 1)[1]:
                            stalled = True
                            break
                        try:
                            sent += client.send(b"x" * 65536)
                        except BlockingIOError:
                            continue
                    self.assertTrue(stalled, "The private non-reading peer did not establish writer backpressure")
                    cats = [int(pid) for pid in Path(f"/proc/{shell}/task/{shell}/children").read_text().split()]
                    self.assertEqual(2, len(cats))
                    print(f"blocked_writer=True sent_bytes={sent} shell={shell} cats={cats} peer_still_open=True", flush=True)
                    os.killpg(proxy.pid, signal.SIGTERM)
                    code = proxy.wait(timeout=20)
                    remaining = [pid for pid in [shell, *cats] if exists(pid)]
                    self.assertEqual([], remaining, "Container relay survived host cleanup while the peer was still open")
                    self.assertEqual(0, code, "A successfully recovered blocked writer must shut down cleanly")
                    self.assertFalse(exists(state["docker_pid"]))
                    command = json.loads(cleanup.read_text())
                    self.assertEqual(str(shell), command["arguments"][1])
                    self.assertEqual(state["parameters"][-1], command["arguments"][3])
                    for name in ("CHRYSALIS_WALLET_KEY_FILE", "CHRYSALIS_BLOCKFROST_KEY_FILE", "DOCKER_HOST"):
                        self.assertNotIn(name, command["environment_names"])
                    print("out_of_band_cleanup=True remaining_shell_and_cats=[] peer_still_open=True", flush=True)
                finally:
                    if proxy.poll() is None:
                        os.killpg(proxy.pid, signal.SIGTERM)
                        try:
                            proxy.wait(timeout=20)
                        except subprocess.TimeoutExpired:
                            os.killpg(proxy.pid, signal.SIGKILL)
                            proxy.wait(timeout=5)
                    if record.exists() and not owned:
                        state = json.loads(record.read_text())
                        owned = [state["relay_pid"], state["docker_pid"]]
                    clean_private_groups(owned)
                    if peer is not None:
                        peer.close()
                    if client is not None:
                        client.close()
                    proxy.stdout.close()
                    proxy.stderr.close()


if __name__ == "__main__":
    unittest.main()
