"""Fetch and verify the pinned Plutus conformance suite and variant-E cost files."""
import errno
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tempfile

HEX40_RE = re.compile(r"^[0-9a-f]{40}$")
HEX64_RE = re.compile(r"^[0-9a-f]{64}$")
TIMEOUT = 300


class ConformanceError(Exception):
    def __init__(self, code, tree_id=None):
        super().__init__(code)
        self.code = code
        self.tree_id = tree_id


def fail(code, tree_id=None):
    raise ConformanceError(code, tree_id)


def valid_rel_path(value):
    if not isinstance(value, str) or not value or not value.isascii():
        return False
    if value.startswith("/") or value.endswith("/") or "\\" in value:
        return False
    if any(ord(ch) < 0x20 or ord(ch) == 0x7F for ch in value):
        return False
    parts = value.split("/")
    return not any(part in ("", ".", "..") or part.lower() == ".git" for part in parts)


def validate_pin(pin_path):
    report_id = None
    try:
        if pin_path.is_symlink() or not stat.S_ISREG(pin_path.lstat().st_mode):
            fail("pin")
        data = json.loads(pin_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, ValueError, RecursionError):
        fail("pin")
    if not isinstance(data, dict):
        fail("pin")
    tree = data.get("tree")
    if isinstance(tree, dict) and isinstance(tree.get("id"), str) and HEX40_RE.fullmatch(tree["id"]):
        report_id = tree["id"]
    source = data.get("source")
    commit = data.get("commit")
    blobs = data.get("blobs")
    sha256 = data.get("sha256")
    files = data.get("files")
    if (
        not isinstance(source, str)
        or not (source.startswith("https://") or source.startswith("file://"))
        or len(source) <= 8
        or any(ord(ch) < 0x20 or ord(ch) == 0x7F or ch.isspace() or 0xD800 <= ord(ch) <= 0xDFFF for ch in source)
    ):
        fail("pin", report_id)
    if not isinstance(commit, str) or not HEX40_RE.fullmatch(commit):
        fail("pin", report_id)
    if not isinstance(tree, dict) or not valid_rel_path(tree.get("path")) or report_id is None:
        fail("pin", report_id)
    if not isinstance(blobs, dict) or not blobs:
        fail("pin", report_id)
    for bpath, bid in blobs.items():
        if not valid_rel_path(bpath) or not isinstance(bid, str) or not HEX40_RE.fullmatch(bid):
            fail("pin", report_id)
    if not isinstance(sha256, str) or not HEX64_RE.fullmatch(sha256):
        fail("pin", report_id)
    if not isinstance(files, int) or isinstance(files, bool) or files < 1:
        fail("pin", report_id)
    select = data.get("select")
    if select is not None and (not isinstance(select, list) or not select or not all(valid_rel_path(entry) for entry in select)):
        fail("pin", report_id)
    folders = [f"{tree['path']}/{entry}" for entry in select] if select else [tree["path"]]
    return source, commit, tree["path"], report_id, dict(blobs), sha256, files, folders


def check_no_links(root, target, tree_id):
    try:
        root_st = root.lstat()
    except OSError:
        fail("link", tree_id)
    if stat.S_ISLNK(root_st.st_mode) or not stat.S_ISDIR(root_st.st_mode):
        fail("link", tree_id)
    if target != root:
        if root not in target.parents:
            fail("link", tree_id)
        ancestors = (target, *target.parents)
        chain = list(reversed(ancestors[: ancestors.index(root)]))
        for idx, current in enumerate(chain):
            try:
                st = current.lstat()
            except FileNotFoundError:
                continue
            except OSError:
                fail("link", tree_id)
            if stat.S_ISLNK(st.st_mode):
                fail("link", tree_id)
            if idx < len(chain) - 1 and not stat.S_ISDIR(st.st_mode):
                fail("link", tree_id)
    try:
        resolved_root = root.resolve()
        resolved_target = target.resolve()
    except (OSError, RuntimeError):
        fail("link", tree_id)
    if resolved_target != resolved_root and resolved_root not in resolved_target.parents:
        fail("link", tree_id)


def is_absent(path):
    try:
        path.lstat()
        return False
    except FileNotFoundError:
        return True
    except OSError:
        return False


def ensure_parent_dir(root, parent, tree_id, err_code):
    check_no_links(root, parent, tree_id)
    if not is_absent(parent):
        try:
            pst = parent.lstat()
        except OSError:
            fail(err_code, tree_id)
        if not stat.S_ISDIR(pst.st_mode):
            fail("link", tree_id)
    else:
        try:
            parent.mkdir(mode=0o755, exist_ok=True)
        except OSError:
            fail(err_code, tree_id)


def safe_rmtree(path):
    try:
        st = path.lstat()
    except FileNotFoundError:
        return
    if stat.S_ISLNK(st.st_mode):
        path.unlink()
        return
    if not stat.S_ISDIR(st.st_mode):
        if os.name != "posix" and stat.S_ISREG(st.st_mode) and st.st_nlink == 1:
            os.chmod(path, stat.S_IMODE(st.st_mode) | 0o600)
        path.unlink()
        return
    os.chmod(path, stat.S_IMODE(st.st_mode) | 0o700)
    stack = [path]
    while stack:
        cur = stack.pop()
        with os.scandir(cur) as it:
            for entry in it:
                entry_path = Path(entry.path)
                est = entry.stat(follow_symlinks=False)
                if stat.S_ISLNK(est.st_mode):
                    entry_path.unlink()
                elif stat.S_ISDIR(est.st_mode):
                    os.chmod(entry_path, stat.S_IMODE(est.st_mode) | 0o700)
                    stack.append(entry_path)
                elif os.name != "posix" and stat.S_ISREG(est.st_mode) and est.st_nlink == 1:
                    os.chmod(entry_path, stat.S_IMODE(est.st_mode) | 0o600)
    shutil.rmtree(path)


def sha256_file(path):
    h = hashlib.sha256()
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0)
    fd = os.open(str(path), flags)
    try:
        st = os.fstat(fd)
        if not stat.S_ISREG(st.st_mode) or st.st_nlink != 1:
            return None
        while True:
            chunk = os.read(fd, 65536)
            if not chunk:
                break
            h.update(chunk)
        return h.hexdigest(), st.st_size
    finally:
        os.close(fd)


def verify_dir(root, expected_sha256, expected_files):
    try:
        if root.is_symlink():
            return None
        st = root.lstat()
        if not stat.S_ISDIR(st.st_mode):
            return None
        entries = []
        total_bytes = 0
        stack = [root]
        while stack:
            cur = stack.pop()
            with os.scandir(cur) as it:
                for entry in it:
                    if entry.name.isascii() and entry.name.lower() == ".git":
                        return None
                    entry_path = Path(entry.path)
                    est = entry.stat(follow_symlinks=False)
                    if stat.S_ISLNK(est.st_mode):
                        return None
                    if stat.S_ISDIR(est.st_mode):
                        stack.append(entry_path)
                    elif stat.S_ISREG(est.st_mode) and est.st_nlink == 1:
                        rel = entry_path.relative_to(root).as_posix()
                        if not rel.isascii():
                            return None
                        res = sha256_file(entry_path)
                        if res is None:
                            return None
                        fhash, fsize = res
                        entries.append((rel, fhash))
                        total_bytes += fsize
                    else:
                        return None
    except OSError:
        return None
    if len(entries) != expected_files:
        return None
    entries.sort(key=lambda item: item[0])
    manifest = hashlib.sha256()
    for rel, fhash in entries:
        manifest.update(f"{fhash}  {rel}\n".encode("utf-8"))
    if manifest.hexdigest() != expected_sha256:
        return None
    return len(entries), total_bytes


def run_git(cwd, env, *args):
    return subprocess.run(
        ["git", *args],
        cwd=str(cwd),
        env=env,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        encoding="utf-8",
        errors="replace",
        check=True,
        timeout=TIMEOUT,
    )


def fetch_tree(temp_dir, source, commit, tree_path, tree_id, blobs, folders):
    env = {k: v for k, v in os.environ.items() if not k.startswith("GIT_")}
    env["GIT_TERMINAL_PROMPT"] = "0"
    env["GIT_CONFIG_GLOBAL"] = "/dev/null"
    env["GIT_CONFIG_NOSYSTEM"] = "1"
    try:
        run_git(temp_dir, env, "init")
        run_git(temp_dir, env, "remote", "add", "origin", "--", source)
        run_git(temp_dir, env, "-c", "protocol.version=2", "fetch", "--depth", "1", "--filter=blob:none", "origin", commit)
    except (OSError, subprocess.SubprocessError):
        fail("fetch", tree_id)
    try:
        if run_git(temp_dir, env, "rev-parse", "FETCH_HEAD^{commit}").stdout.strip() != commit:
            fail("verify", tree_id)
        if run_git(temp_dir, env, "rev-parse", f"FETCH_HEAD:{tree_path}").stdout.strip() != tree_id:
            fail("verify", tree_id)
        if run_git(temp_dir, env, "cat-file", "-t", f"FETCH_HEAD:{tree_path}").stdout.strip() != "tree":
            fail("verify", tree_id)
        for bpath, bid in blobs.items():
            if run_git(temp_dir, env, "rev-parse", f"FETCH_HEAD:{bpath}").stdout.strip() != bid:
                fail("verify", tree_id)
            if run_git(temp_dir, env, "cat-file", "-t", f"FETCH_HEAD:{bpath}").stdout.strip() != "blob":
                fail("verify", tree_id)
    except (OSError, subprocess.SubprocessError):
        fail("verify", tree_id)
    try:
        run_git(temp_dir, env, "sparse-checkout", "set", "--no-cone", "--", *(f"/{folder}/" for folder in folders), *(f"/{bp}" for bp in blobs))
        run_git(temp_dir, env, "checkout", "--detach", "FETCH_HEAD")
    except (OSError, subprocess.SubprocessError):
        fail("fetch", tree_id)
    try:
        safe_rmtree(temp_dir / ".git")
        os.chmod(temp_dir, 0o755)
    except OSError:
        fail("fetch", tree_id)


def copy_tree(src, dst, tree_id):
    try:
        stack = [src]
        while stack:
            cur = stack.pop()
            rel_dir = cur.relative_to(src)
            if rel_dir != Path("."):
                (dst / rel_dir).mkdir(mode=0o755, exist_ok=False)
            with os.scandir(cur) as it:
                for entry in it:
                    entry_path = Path(entry.path)
                    est = entry.stat(follow_symlinks=False)
                    if stat.S_ISLNK(est.st_mode):
                        fail("copy", tree_id)
                    if stat.S_ISDIR(est.st_mode):
                        stack.append(entry_path)
                    elif stat.S_ISREG(est.st_mode) and est.st_nlink == 1:
                        out_path = dst / entry_path.relative_to(src)
                        mode = 0o755 if (stat.S_IMODE(est.st_mode) & 0o111) else 0o644
                        in_flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0)
                        out_flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
                        with os.fdopen(os.open(str(entry_path), in_flags), "rb") as src_f:
                            fst = os.fstat(src_f.fileno())
                            if not stat.S_ISREG(fst.st_mode) or fst.st_nlink != 1:
                                fail("copy", tree_id)
                            with os.fdopen(os.open(str(out_path), out_flags, mode), "wb") as dst_f:
                                shutil.copyfileobj(src_f, dst_f)
                    else:
                        fail("copy", tree_id)
        os.chmod(dst, 0o755)
    except ConformanceError:
        raise
    except OSError:
        fail("copy", tree_id)


def quarantine_and_remove(target, parent, prefix, tree_id, err_code, cleanup_dirs):
    try:
        tmp_dir = Path(tempfile.mkdtemp(prefix=prefix, dir=str(parent)))
        cleanup_dirs.append(tmp_dir)
        tst = target.lstat()
        if stat.S_ISDIR(tst.st_mode):
            os.chmod(target, stat.S_IMODE(tst.st_mode) | 0o700)
        os.replace(target, tmp_dir / "entry")
    except OSError:
        fail(err_code, tree_id)
    try:
        safe_rmtree(tmp_dir)
    except OSError:
        fail(err_code, tree_id)
    cleanup_dirs.remove(tmp_dir)


def cleanup_temp_dirs(cleanup_dirs):
    for d in reversed(cleanup_dirs):
        try:
            safe_rmtree(d)
        except OSError:
            pass
    cleanup_dirs.clear()


def fetch_verified_temp(parent, err_code, pin, cleanup_dirs):
    source, commit, tree_path, tree_id, blobs, expected_sha256, expected_files, folders = pin
    try:
        tmp_dir = Path(tempfile.mkdtemp(prefix=f".fetch-{tree_id}-", dir=str(parent)))
    except OSError:
        fail(err_code, tree_id)
    cleanup_dirs.append(tmp_dir)
    fetch_tree(tmp_dir, source, commit, tree_path, tree_id, blobs, folders)
    verified = verify_dir(tmp_dir, expected_sha256, expected_files)
    if verified is None:
        fail("verify", tree_id)
    return tmp_dir, verified


def prepare_from_cache(corpora_env, dest, dest_parent, pin, cleanup_dirs):
    _, _, _, tree_id, _, expected_sha256, expected_files, _ = pin
    try:
        import fcntl
    except ImportError:
        fail("corpora-missing", tree_id)

    if not corpora_env or not os.path.isabs(corpora_env):
        fail("corpora-missing", tree_id)
    corpora_root = Path(corpora_env)
    try:
        cst = corpora_root.lstat()
    except OSError:
        fail("corpora-missing", tree_id)
    if stat.S_ISLNK(cst.st_mode):
        fail("link", tree_id)
    if not stat.S_ISDIR(cst.st_mode):
        fail("corpora-missing", tree_id)

    cache_parent = corpora_root / "plutus-conformance"
    lock_path = cache_parent / f"{tree_id}.lock"
    cache_dir = cache_parent / tree_id
    ensure_parent_dir(corpora_root, cache_parent, tree_id, "corpora-missing")
    check_no_links(corpora_root, lock_path, tree_id)
    check_no_links(corpora_root, cache_dir, tree_id)

    lock_fd = None
    try:
        try:
            open_flags = os.O_CREAT | os.O_RDWR | getattr(os, "O_NOFOLLOW", 0)
            lock_fd = os.open(str(lock_path), open_flags, 0o644)
            lst = os.fstat(lock_fd)
            if not stat.S_ISREG(lst.st_mode):
                fail("link", tree_id)
        except OSError as exc:
            if exc.errno == getattr(errno, "ELOOP", None):
                fail("link", tree_id)
            fail("corpora-missing", tree_id)
        try:
            fcntl.flock(lock_fd, fcntl.LOCK_EX)
        except OSError:
            fail("corpora-missing", tree_id)
        check_no_links(corpora_root, cache_dir, tree_id)
        verified_c = None if is_absent(cache_dir) else verify_dir(cache_dir, expected_sha256, expected_files)
        if verified_c is not None:
            status = "cache"
        else:
            cache_existed = not is_absent(cache_dir)
            tmp_fetch, _ = fetch_verified_temp(cache_parent, "corpora-missing", pin, cleanup_dirs)
            if cache_existed:
                quarantine_and_remove(cache_dir, cache_parent, f".stale-{tree_id}-", tree_id, "copy", cleanup_dirs)
                status = "replaced"
            else:
                status = "fetched"
            try:
                os.replace(tmp_fetch, cache_dir)
            except OSError:
                fail("copy", tree_id)
            cleanup_dirs.remove(tmp_fetch)
        try:
            tmp_dest = Path(tempfile.mkdtemp(prefix=f".copy-{tree_id}-", dir=str(dest_parent)))
        except OSError:
            fail("copy", tree_id)
        cleanup_dirs.append(tmp_dest)
        copy_tree(cache_dir, tmp_dest, tree_id)
        verified_d = verify_dir(tmp_dest, expected_sha256, expected_files)
        if verified_d is None:
            fail("copy", tree_id)
        try:
            os.replace(tmp_dest, dest)
        except OSError:
            fail("copy", tree_id)
        cleanup_dirs.remove(tmp_dest)
        return status, verified_d
    finally:
        cleanup_temp_dirs(cleanup_dirs)
        if lock_fd is not None:
            try:
                fcntl.flock(lock_fd, fcntl.LOCK_UN)
            except OSError:
                pass
            try:
                os.close(lock_fd)
            except OSError:
                pass


def run(pin_path):
    script_path = Path(__file__)
    pin = validate_pin(pin_path)
    _, _, _, tree_id, _, expected_sha256, expected_files, _ = pin
    try:
        if stat.S_ISLNK(script_path.lstat().st_mode):
            fail("link", tree_id)
        base_dir = script_path.resolve().parent
    except ConformanceError:
        raise
    except (OSError, RuntimeError):
        fail("link", tree_id)
    dest_parent = base_dir / "plutus-conformance"
    dest = dest_parent / tree_id
    ensure_parent_dir(base_dir, dest_parent, tree_id, "missing-dir")
    check_no_links(base_dir, dest, tree_id)

    cleanup_dirs = []
    try:
        if not is_absent(dest):
            verified = verify_dir(dest, expected_sha256, expected_files)
            if verified is not None:
                print(f"conformance: ok {tree_id} ({verified[0]} files, {verified[1]} bytes)", flush=True)
                return 0
            quarantine_and_remove(dest, dest_parent, f".stale-{tree_id}-", tree_id, "missing-dir", cleanup_dirs)

        if "CHRYSALIS_CORPORA" in os.environ:
            status, verified_d = prepare_from_cache(os.environ["CHRYSALIS_CORPORA"], dest, dest_parent, pin, cleanup_dirs)
        else:
            tmp_dest, verified_d = fetch_verified_temp(dest_parent, "missing-dir", pin, cleanup_dirs)
            try:
                os.replace(tmp_dest, dest)
            except OSError:
                fail("copy", tree_id)
            cleanup_dirs.remove(tmp_dest)
            status = "fetched"
        print(f"conformance: {status} {tree_id} ({verified_d[0]} files, {verified_d[1]} bytes)", flush=True)
        return 0
    finally:
        cleanup_temp_dirs(cleanup_dirs)


def main():
    try:
        for pin_path in sorted(Path(__file__).parent.glob("conformance*.json")) or fail("pin"):
            run(pin_path)
        return 0
    except ConformanceError as exc:
        suffix = f" {exc.tree_id}" if exc.tree_id else ""
        print(f"conformance: fail {exc.code}{suffix}", flush=True)
        return 1


if __name__ == "__main__":
    sys.exit(main())
