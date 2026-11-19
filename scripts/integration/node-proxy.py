#!/usr/bin/env python3
"""Loopback-only bridge to an explicitly selected existing local Docker node.

Does not start a container, mount data, read credentials or change node configuration.
SIGINT/SIGTERM close all relay inputs and join their Docker children before returning.
"""
import argparse
import json
import os
import select
import signal
import socket
import subprocess
import threading
import time
import uuid


DOCKER_ENDPOINT = "unix:///var/run/docker.sock"
CONTAINER_RELAY_SETUP = r'''
writer= reader=
stop_owned_child() {
    local child=$1 status child_start
    local -a fields=() owner_arguments=()
    [[ $child =~ ^[0-9]+$ && $child -gt 1 ]] || return 0
    IFS= read -r status 2>/dev/null < "/proc/$child/stat" || return 0
    read -r -a fields <<< "${status##*) }"
    test "${fields[1]}" = "$$" || return 0
    child_start=${fields[19]}
    # This executing supervisor is alive; revalidate its start/nonce and its still-unreaped child.
    IFS= read -r status 2>/dev/null < "/proc/$$/stat" || return 0
    read -r -a fields <<< "${status##*) }"
    test "${fields[19]}" = "$self_start" || return 0
    mapfile -d '' -t owner_arguments < "/proc/$$/cmdline" 2>/dev/null || return 0
    test "${owner_arguments[3]}" = chrysalis-integration-proxy && test "${owner_arguments[5]}" = "$nonce" || return 0
    IFS= read -r status 2>/dev/null < "/proc/$child/stat" || return 0
    read -r -a fields <<< "${status##*) }"
    test "${fields[1]}" = "$$" && test "${fields[19]}" = "$child_start" || return 0
    kill -TERM "$child" 2>/dev/null || :
}
cleanup() {
    trap - EXIT HUP INT TERM
    test -z "$writer" || stop_owned_child "$writer"
    test -z "$reader" || stop_owned_child "$reader"
    wait 2>/dev/null || :
    exec 3>&- 3<&-
}
trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM
nonce=${2:-}
[[ $nonce =~ ^[0-9a-f]{32}$ ]] || exit 64
IFS= read -r status < "/proc/$$/stat" || exit 64
read -r -a fields <<< "${status##*) }"
self_start=${fields[19]}
printf 'CHRYSALIS_PROXY_READY %s %s %s\n' "$nonce" "$$" "$self_start" >&2
# No peer connection or cat exists unless the host received and acknowledged this identity.
IFS= read -r -t 5 acknowledgement || exit 64
test "$acknowledgement" = "CHRYSALIS_PROXY_ACK $nonce" || exit 64
'''
CONTAINER_RELAY = CONTAINER_RELAY_SETUP + r'''
exec 3<>/dev/tcp/127.0.0.1/"$1" || exit
cat <&0 >&3 & writer=$!
cat <&3 >&1 & reader=$!
wait -n "$writer" "$reader"
'''
PROC_CHILDREN = r'''
# Enumerate direct children without depending on CONFIG_PROC_CHILDREN.
proc_children() {
    local root=$1 parent=$2 entry child status seen_parent=
    local numbers='^[0-9]+( -?[0-9]+){17} [0-9]+$'
    local -a fields=()
    [[ -r $root && -x $root ]] || return 1
    for entry in "$root"/[0-9]*; do
        child=${entry##*/}
        [[ $child =~ ^[0-9]+$ ]] || continue
        if ! status=$(<"$entry/stat") 2>/dev/null; then
            [[ ! -d $entry ]] && continue  # Exited since the glob.
            return 1                       # A live unreadable entry is not evidence of emptiness.
        fi
        # comm may contain spaces, parentheses and newlines. Fields follow the last ') '.
        [[ $status == "$child ("*") "* ]] || return 1
        read -r -a fields <<< "${status##*) }"
        # Require the state and numeric layout through starttime before trusting PPid.
        [[ ${fields[0]} =~ ^[RSDZTtXxKWPI]$ && ${fields[*]:1:19} =~ $numbers ]] || return 1
        if [[ $child == "$parent" ]]; then seen_parent=1; fi
        if [[ ${fields[1]} == "$parent" ]]; then printf '%s\n' "$child"; fi
    done
    [[ -n $seen_parent ]]
}
'''
CONTAINER_RELAY_CLEANUP = PROC_CHILDREN + r'''
pid=$1 expected_start=$2 nonce=$3 action=$4
refuse() { printf 'CHRYSALIS_PROXY_CLEANUP_REFUSED %s\n' "$1" >&2; exit "$2"; }
[[ $pid =~ ^[0-9]+$ && $pid -gt 1 && $expected_start =~ ^[0-9]+$ && $nonce =~ ^[0-9a-f]{32}$ ]] || refuse invalid_identity_fields 64
require_relay() {
    local status
    local -a fields=() arguments=()
    test -d "/proc/$pid" || exit 0
    IFS= read -r status < "/proc/$pid/stat" || refuse proc_stat_unreadable 65
    read -r -a fields <<< "${status##*) }"
    test "${fields[19]}" = "$expected_start" || refuse wrong_start_time 65
    mapfile -d '' -t arguments < "/proc/$pid/cmdline" || refuse proc_cmdline_unreadable 65
    test "${arguments[0]##*/}" = bash || refuse not_bash 65
    test "${arguments[1]}" = -c || refuse not_command_shell 65
    test "${arguments[3]}" = chrysalis-integration-proxy || refuse wrong_relay_marker 65
    test "${arguments[5]}" = "$nonce" || refuse wrong_relay_nonce 65
}
# Numeric kill retains a tiny check-to-signal gap: reuse needs namespace PID-counter
# wrap between adjacent builtins. Accept that residual risk in this test-only helper
# rather than requiring image-dependent pidfds; revalidate immediately before signals.
require_relay
case "$action" in
    CHECK) refuse target_still_running 67 ;;
    TERM)
        require_relay
        if ! kill -TERM "$pid" 2>/dev/null; then
            test -d "/proc/$pid" || exit 0
            refuse signal_failed 68
        fi
        printf 'CHRYSALIS_PROXY_CLEANUP_SIGNAL_SENT TERM %s\n' "$pid" ;;
    KILL_CHILDREN)
        children=$(proc_children /proc "$pid") || refuse children_unreadable 65
        for child in $children; do
            IFS= read -r status 2>/dev/null < "/proc/$child/stat" || continue
            read -r -a fields <<< "${status##*) }"
            test "${fields[1]}" = "$pid" || continue
            child_start=${fields[19]}
            mapfile -d '' -t child_arguments < "/proc/$child/cmdline" 2>/dev/null || continue
            test "${child_arguments[0]##*/}" = cat || continue
            require_relay
            # A matching live supervisor/PPid means the child is unreaped and its PID held.
            IFS= read -r status 2>/dev/null < "/proc/$child/stat" || continue
            read -r -a fields <<< "${status##*) }"
            test "${fields[1]}" = "$pid" && test "${fields[19]}" = "$child_start" || continue
            kill -KILL "$child" 2>/dev/null || :
        done ;;
    KILL_EMPTY_RELAY)
        children=$(proc_children /proc "$pid") || refuse children_unreadable 65
        test -z "$children" || refuse owned_children_remain 66
        require_relay
        kill -KILL "$pid" 2>/dev/null || : ;;
    *) refuse invalid_cleanup_action 64 ;;
esac
'''


def docker_environment():
    allowed = ("PATH", "HOME", "TMP", "TEMP", "SystemRoot", "LD_LIBRARY_PATH")
    return {name: os.environ[name] for name in allowed if name in os.environ}


class Relay(threading.Thread):
    def __init__(self, client, container, port, stopping):
        super().__init__(daemon=False)
        self.client = client
        self.container = container
        self.port = port
        self.stopping = stopping
        self.child = None
        self.failed = False
        self.nonce = uuid.uuid4().hex
        self.container_identity = None
        self.used_out_of_band_cleanup = False
        self.cleanup_actions = []
        self.cleanup_results = []
        self.discarded_output_bytes = 0
        self.cleanup_deadline = None

    def stop(self):
        try:
            self.client.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass

    def run(self):
        ready = False
        try:
            if self.stopping.is_set():
                return
            command = [
                "docker", "--host", DOCKER_ENDPOINT, "exec", "-i", self.container,
                "bash", "-c", CONTAINER_RELAY, "chrysalis-integration-proxy", str(self.port), self.nonce,
            ]
            self.child = subprocess.Popen(
                command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0,
                env=docker_environment(), start_new_session=True,
            )
            self.read_container_identity()
            ready = True
            self.pump()
        except (OSError, ValueError, TimeoutError):
            self.failed = not ready
        finally:
            self.client.close()
            if self.child is not None:
                self.finish_child()

    def read_container_identity(self):
        control = bytearray()
        os.set_blocking(self.child.stderr.fileno(), False)
        deadline = time.monotonic() + 5
        while b"\n" not in control:
            remaining = deadline - time.monotonic()
            if remaining <= 0 or not select.select([self.child.stderr], [], [], remaining)[0]:
                raise TimeoutError("Container relay identity was not received before payload forwarding")
            data = os.read(self.child.stderr.fileno(), 4096)
            if not data or len(control) + len(data) > 4096:
                raise ValueError("Invalid container relay identity control message")
            control.extend(data)
        fields = bytes(control).split(b"\n", 1)[0].decode("ascii").split()
        if (len(fields) != 4 or fields[0] != "CHRYSALIS_PROXY_READY" or fields[1] != self.nonce
                or not fields[2].isdecimal() or int(fields[2]) <= 1 or not fields[3].isdecimal()):
            raise ValueError("Container relay identity did not match its private nonce")
        self.container_identity = (int(fields[2]), int(fields[3]))
        self.child.stdin.write(("CHRYSALIS_PROXY_ACK " + self.nonce + "\n").encode("ascii"))

    def pump(self):
        child = self.child
        self.client.setblocking(False)
        os.set_blocking(child.stdin.fileno(), False)
        os.set_blocking(child.stdout.fileno(), False)
        to_node = bytearray()
        to_client = bytearray()
        client_eof = stdout_eof = stdin_closed = False
        limit = 256 * 1024
        while not self.stopping.is_set():
            if client_eof and not to_node and not stdin_closed:
                child.stdin.close()
                stdin_closed = True
            if stdout_eof and not to_client:
                return
            readers = ([] if client_eof or len(to_node) >= limit else [self.client])
            if not stdout_eof and len(to_client) < limit:
                readers.append(child.stdout)
            writers = [self.client] if to_client else []
            if to_node and not stdin_closed:
                writers.append(child.stdin)
            ready_read, ready_write, _ = select.select(readers, writers, [], 0.1)
            if self.client in ready_read:
                data = self.client.recv(65536)
                to_node.extend(data)
                client_eof = not data
            if child.stdout in ready_read:
                data = os.read(child.stdout.fileno(), 65536)
                to_client.extend(data)
                stdout_eof = not data
            if self.client in ready_write:
                del to_client[:self.client.send(to_client)]
            if not stdin_closed and child.stdin in ready_write:
                del to_node[:os.write(child.stdin.fileno(), to_node)]

    def cleanup_container(self, action):
        if self.container_identity is None:
            return False
        pid, start = self.container_identity
        self.cleanup_actions.append(action)
        command = [
            "docker", "--host", DOCKER_ENDPOINT, "exec", self.container,
            "bash", "-c", CONTAINER_RELAY_CLEANUP, "chrysalis-integration-proxy-cleanup",
            str(pid), str(start), self.nonce, action,
        ]
        started = time.monotonic()
        timeout = 5 if self.cleanup_deadline is None else min(5, max(0, self.cleanup_deadline - started))
        try:
            if timeout <= 0:
                raise subprocess.TimeoutExpired(command, timeout)
            result = subprocess.run(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                    stderr=subprocess.PIPE, env=docker_environment(), timeout=timeout,
                                    start_new_session=True, check=False)
            diagnostic = {"action": action, "pid": pid, "returncode": result.returncode,
                          "stdout": result.stdout.decode("utf-8", errors="replace")[:512],
                          "stderr": result.stderr.decode("utf-8", errors="replace")[:512]}
        except subprocess.TimeoutExpired as error:
            diagnostic = {"action": action, "pid": pid, "returncode": None, "error": "timeout",
                          "stdout": (error.stdout or b"").decode("utf-8", errors="replace")[:512],
                          "stderr": (error.stderr or b"").decode("utf-8", errors="replace")[:512]}
        except OSError as error:
            diagnostic = {"action": action, "pid": pid, "returncode": None, "error": type(error).__name__, "stdout": "", "stderr": ""}
        diagnostic["elapsed_seconds"] = round(time.monotonic() - started, 3)
        self.cleanup_results.append(diagnostic)
        # Only our local Docker/identity-control diagnostics; never raw peer/HTTP data or environment values.
        print("Container cleanup " + json.dumps(diagnostic), flush=True)
        return diagnostic["returncode"] == 0

    def release_output_after_signal(self):
        # Docker attach can remain alive draining queued stdout after the container process
        # has exited. Discard at most 256 KiB, then close our own abandoned output pipe.
        # Container termination is still verified independently; host EOF is not evidence.
        if self.child.stdout.closed:
            return
        fd = self.child.stdout.fileno()
        os.set_blocking(fd, False)
        budget = 256 * 1024
        while budget > 0:
            try:
                data = os.read(fd, min(65536, budget))
            except BlockingIOError:
                break
            if not data:
                break
            self.discarded_output_bytes += len(data)
            budget -= len(data)
        self.child.stdout.close()

    def finish_child(self):
        self.child.stdin.close()
        try:
            try:
                self.child.wait(timeout=5)
            except subprocess.TimeoutExpired:
                pass
            if self.container_identity is not None:
                # Every identified exit path needs an independent CHECK, including host exit 0.
                self.cleanup_deadline = time.monotonic() + 20
                container_gone = self.cleanup_container("CHECK")
                if not container_gone:
                    self.used_out_of_band_cleanup = True
                    for action in ("TERM", "KILL_CHILDREN", "KILL_EMPTY_RELAY"):
                        if not self.cleanup_container(action):
                            break
                        self.release_output_after_signal()
                        if self.cleanup_container("CHECK"):
                            container_gone = True
                            break
                if not container_gone:
                    self.failed = True
                elif self.child.poll() is None:
                    # Also safe without a signal when CHECK independently proved it gone.
                    self.release_output_after_signal()
            else:
                # No acknowledged identity means no forwarding was permitted; do not claim success.
                self.failed = True
            try:
                self.child.wait(timeout=3)
            except subprocess.TimeoutExpired:
                self.failed = True
            if self.child.poll() is None:
                # Host termination is a last resort and never counts as container cleanup.
                self.failed = True
                try:
                    os.killpg(self.child.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                try:
                    self.child.wait(timeout=1)
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(self.child.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    try:
                        self.child.wait(timeout=1)
                    except subprocess.TimeoutExpired:
                        self.failed = True
        finally:
            self.child.stdout.close()
            self.child.stderr.close()
        method = "container-out-of-band" if self.used_out_of_band_cleanup else "stdin-eof"
        print(f"Joined relay docker_pid={self.child.pid} exit={self.child.returncode} cleanup={method} actions={self.cleanup_actions} discarded_output_bytes={self.discarded_output_bytes}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--container", required=True)
    parser.add_argument("--listen-port", type=int, default=3001)
    parser.add_argument("--node-port", type=int, default=3001)
    args = parser.parse_args()
    if not all(0 < port <= 65535 for port in (args.listen_port, args.node_port)):
        parser.error("Both ports must be in the range 1..65535")
    stopping = threading.Event()
    signal.signal(signal.SIGINT, lambda signum, frame: stopping.set())
    signal.signal(signal.SIGTERM, lambda signum, frame: stopping.set())
    signal.signal(signal.SIGHUP, lambda signum, frame: stopping.set())
    relays = []
    with socket.socket() as listener:
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind(("127.0.0.1", args.listen_port))
        listener.listen()
        listener.settimeout(0.2)
        print(f"Loopback bridge listening on port {args.listen_port}", flush=True)
        try:
            while not stopping.is_set():
                try:
                    client, _ = listener.accept()
                except socket.timeout:
                    continue
                relay = Relay(client, args.container, args.node_port, stopping)
                relays.append(relay)
                relay.start()
        finally:
            stopping.set()
            for relay in relays:
                relay.stop()
            for relay in relays:
                relay.join()
    return int(any(relay.failed for relay in relays))


if __name__ == "__main__":
    raise SystemExit(main())
