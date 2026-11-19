"""Collect the source tests, not the identical copy made by MSBuild."""
from pathlib import Path

if not (Path(__file__).resolve().parents[2] / "Chrysalis.Tx.Test.csproj").is_file():
    collect_ignore = ["test_capture.py"]
