"""Exercise the same Bash child scan as the real container cleanup."""
from pathlib import Path
import runpy
import subprocess


PROC_CHILDREN = runpy.run_path(str(Path(__file__).with_name("node-proxy.py")))["PROC_CHILDREN"]


def child_pids(parent, proc="/proc"):
    output = subprocess.check_output(
        ["bash", "-c", PROC_CHILDREN + '\nproc_children "$1" "$2"',
         "chrysalis-proxy-test", str(proc), str(parent)], timeout=3,
    )
    return [int(pid) for pid in output.split()]
