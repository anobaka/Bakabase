#!/usr/bin/env python3
"""Preview or rewrite absolute path references in an offline AppData copy.

Mappings are a JSON object of old directory prefixes to new prefixes. JSON values
inside SQLite columns are decoded recursively; URLs and arbitrary prose are not
searched/replaced. Apply requires an external backup directory and exclusive
ownership of the data directory. Never run this against an active instance.
"""
import argparse
import collections
import contextlib
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import sys


WINDOWS_PATH = re.compile(r"^[A-Za-z]:[/\\]")


def split_standard(value, separator=","):
    """Match Bakabase StringExtensions.SplitWithEscapeChar, including unknown escapes."""
    parts, current, index = [], [], 0
    while index < len(value):
        char = value[index]
        if char == "\\" and index + 1 < len(value) and value[index + 1] in (separator, "\\"):
            current.append(value[index + 1])
            index += 2
            continue
        if char == separator:
            parts.append("".join(current)); current = []
        else:
            current.append(char)
        index += 1
    parts.append("".join(current))
    return parts


def join_standard(values, separator=","):
    return separator.join(v.replace("\\", "\\\\").replace(separator, "\\" + separator) for v in values)


class Rewriter:
    def __init__(self, mappings):
        self.mappings = []
        for old, new in mappings.items():
            old = old.replace("\\", "/").rstrip("/")
            new = new.replace("\\", "/").rstrip("/") or "/"
            if not (WINDOWS_PATH.match(old + "/") or old.startswith("/")):
                raise ValueError("Mapping sources must be absolute paths")
            if not new.startswith("/"):
                raise ValueError("Mapping targets must be absolute POSIX paths")
            self.mappings.append((old, new))
        self.mappings.sort(key=lambda item: len(item[0]), reverse=True)
        self.counts = collections.Counter()
        self.unmapped = collections.Counter()

    def path(self, value):
        normalized = value.replace("\\", "/")
        for old, new in self.mappings:
            case_insensitive = bool(WINDOWS_PATH.match(old + "/"))
            lhs, rhs = (normalized.casefold(), old.casefold()) if case_insensitive else (normalized, old)
            if lhs == rhs or lhs.startswith(rhs + "/"):
                result = new.rstrip("/") + normalized[len(old):]
                if result != value:
                    self.counts[old] += 1
                return result
        if WINDOWS_PATH.match(value):
            self.unmapped[normalized[:2].upper()] += 1
        return value

    def standard(self, value, value_type):
        if value_type in (2, 4):  # ListString, Link
            original = split_standard(value)
            mapped = [self.value(item) for item in original]
            return join_standard(mapped) if mapped != original else value
        if value_type in (8, 9):  # ListListString, ListTag: two independently escaped layers
            outer = split_standard(value, ";")
            mapped = [self.standard(item, 2) for item in outer]
            return join_standard(mapped, ";") if mapped != outer else value
        return self.value(value)

    def value(self, value, depth=0):
        if depth > 40:
            raise ValueError("Nested reference exceeds the supported depth")
        if isinstance(value, dict):
            result = {}
            lower_keys = {key.lower(): key for key in value}
            standard_type = value.get(lower_keys.get("valuetype", ""))
            for key, item in value.items():
                mapped_key = self.path(key)
                if mapped_key in result:
                    raise ValueError("Path mapping would merge two JSON object keys")
                result[mapped_key] = self.standard(item, standard_type) if key.lower() == "value" and isinstance(item, str) and isinstance(standard_type, int) else self.value(item, depth + 1)
            return result
        if isinstance(value, list):
            return [self.value(item, depth + 1) for item in value]
        if not isinstance(value, str):
            return value
        # Some columns/options contain JSON encoded inside another JSON string.
        if value.lstrip().startswith(("[", "{", '"')):
            try:
                decoded = json.loads(value)
            except (ValueError, TypeError):
                pass
            else:
                mapped = self.value(decoded, depth + 1)
                if mapped != decoded:
                    return json.dumps(mapped, ensure_ascii=False, separators=(",", ":"))
                return value
        # Legacy MediaLibrariesV2.Paths uses a pipe-separated list.
        if "|" in value and all(WINDOWS_PATH.match(v) or v.startswith("/") for v in value.split("|")):
            return "|".join(self.path(v) for v in value.split("|"))
        return self.path(value)


def identifier(value):
    return '"' + value.replace('"', '""') + '"'


@contextlib.contextmanager
def own_directory(directory):
    # .NET FileShare.None on Unix uses the same advisory flock lock.
    import fcntl
    with (directory / ".bakabase.lock").open("a+b") as handle:
        try:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise RuntimeError("The data directory is in use; stop its instance first") from error
        yield


def rewrite_database(path, rewriter, apply):
    wal = Path(str(path) + "-wal")
    if not apply and wal.exists() and wal.stat().st_size:
        raise ValueError("Preview requires a checkpointed offline database; its WAL still contains data: " + str(path))
    uri = path.as_uri() + ("?mode=rw" if apply else "?mode=ro&immutable=1")
    connection = sqlite3.connect(uri, uri=True)
    changes = collections.Counter()
    try:
        if apply:
            connection.execute("BEGIN IMMEDIATE")
        tables = connection.execute("SELECT name,sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'").fetchall()
        property_types = dict(connection.execute("SELECT Id,Type FROM CustomProperties")) if any(t == "CustomProperties" for t, _ in tables) else {}
        for table, sql in tables:
            if not sql or "VIRTUAL TABLE" in sql.upper():
                continue
            columns = connection.execute("PRAGMA table_info(" + identifier(table) + ")").fetchall()
            text_columns = [col[1] for col in columns if any(t in col[2].upper() for t in ("TEXT", "CHAR", "CLOB"))]
            if not text_columns:
                continue
            primary_keys = [col[1] for col in sorted(columns, key=lambda col: col[5]) if col[5]]
            keys = primary_keys or ["rowid"]
            selected = list(dict.fromkeys(keys + text_columns + [col[1] for col in columns if col[1] in ("PropertyId", "ValueType")]))
            query = "SELECT " + ",".join(map(identifier, selected)) + " FROM " + identifier(table)
            # Rowid order stays stable even when a path is part of a composite primary key.
            if "WITHOUT ROWID" not in sql.upper():
                query += " ORDER BY rowid"
            rows = connection.execute(query)
            if "WITHOUT ROWID" in sql.upper():
                rows = rows.fetchall()
            for row in rows:
                values = dict(zip(selected, row))
                updates = {}
                for column in text_columns:
                    value = values[column]
                    if not isinstance(value, str):
                        continue
                    value_type = None
                    if (table == "ResourceCaches" and column in ("CoverPaths", "PlayableFilePaths")) or (table == "ReservedPropertyValues" and column == "CoverPaths"):
                        value_type = 2
                    elif table in ("CustomPropertyValues", "DataCardPropertyValues") and column == "Value":
                        property_type = property_types.get(values.get("PropertyId"))
                        value_type = 2 if property_type in (4, 10, 15, 16) else 4 if property_type == 9 else 1
                    elif table == "Enhancements" and column == "Value":
                        value_type = values.get("ValueType")
                    if table == "PlayHistories" and column == "Item" and value.startswith("FileSystem:"):
                        mapped = "FileSystem:" + rewriter.path(value[len("FileSystem:"):])
                    else:
                        mapped = rewriter.standard(value, value_type) if value_type else rewriter.value(value)
                    if mapped != value:
                        updates[column] = mapped
                        changes[table + "." + column] += 1
                if apply and updates:
                    statement = "UPDATE " + identifier(table) + " SET " + ",".join(identifier(col) + "=?" for col in updates)
                    statement += " WHERE " + " AND ".join(identifier(key) + " IS ?" for key in keys)
                    connection.execute(statement, list(updates.values()) + [values[key] for key in keys])
        if apply:
            if connection.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise RuntimeError("Database integrity check failed")
            connection.commit()
            connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        return dict(changes)
    except BaseException:
        connection.rollback()
        raise
    finally:
        connection.close()


def config_files(directory):
    # Media files, log history and backups are not application configuration.
    yield from sorted(directory.glob("*.json"))
    for name in ("configs", "downloader", "federation", "remote-access"):
        folder = directory / name
        if folder.exists():
            yield from sorted(folder.rglob("*.json"))


def missing_cover_caches(directory, runtime_directory, apply):
    """Invalidate derived covers whose referenced files were absent in the input copy."""
    database = directory / "bakabase_insideworld.db"
    connection = sqlite3.connect(database.as_uri() + ("?mode=rw" if apply else "?mode=ro&immutable=1"), uri=True)
    try:
        if not connection.execute("SELECT 1 FROM sqlite_master WHERE name='ResourceCaches'").fetchone():
            return 0
        affected = []
        root = runtime_directory.rstrip("/") + "/"
        for resource_id, raw in connection.execute("SELECT ResourceId,CoverPaths FROM ResourceCaches WHERE (CachedTypes & 1) != 0 AND CoverPaths IS NOT NULL"):
            try:
                paths = json.loads(raw)
            except ValueError:
                paths = split_standard(raw)
            if not isinstance(paths, list):
                continue
            missing = False
            for path in paths:
                if not isinstance(path, str) or not path.startswith(root):
                    continue
                local = (directory / path[len(root):]).resolve()
                if directory in local.parents and not local.is_file():
                    missing = True
            if missing:
                affected.append(resource_id)
        if apply:
            with connection:
                connection.executemany("UPDATE ResourceCaches SET CoverPaths=NULL,CachedTypes=CachedTypes & ~1 WHERE ResourceId=?", [(i,) for i in affected])
            connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        return len(affected)
    finally:
        connection.close()


def execute(directory, mappings, apply=False, backup=None, invalidate_missing_covers=False, runtime_directory="/data"):
    directory = directory.resolve(strict=True)
    if not (directory / "bakabase_insideworld.db").is_file():
        raise ValueError("Select an existing Bakabase data copy")
    if apply:
        if backup is None:
            raise ValueError("Applying requires --backup-dir outside the data directory")
        backup = backup.resolve()
        if backup == directory or directory in backup.parents or backup in directory.parents:
            raise ValueError("Backup and data directories must not contain one another")
        backup.mkdir(parents=True, exist_ok=False)
    rewriter = Rewriter(mappings)
    report = {"dataDirectory": str(directory), "applied": apply, "databases": {}, "configurations": []}
    databases = sorted(directory.glob("*.db"))
    configurations = [p for p in config_files(directory) if not p.name.startswith(".bakabase-")]
    for path in databases + configurations:
        if path.is_symlink():
            raise ValueError("Refusing to rewrite symlink: " + str(path))
    # Preserve all originals before the first write. This is an offline utility;
    # unexpected failure leaves the complete backup for recovery, never deletes it.
    guard = own_directory(directory) if apply else contextlib.nullcontext()
    with guard:
        if apply:
            for path in databases + configurations:
                target = backup / path.relative_to(directory)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)
                if path.suffix == ".db":
                    for suffix in ("-wal", "-shm"):
                        sidecar = Path(str(path) + suffix)
                        if sidecar.exists():
                            shutil.copy2(sidecar, Path(str(target) + suffix))
        for path in databases:
            changes = rewrite_database(path, rewriter, apply)
            if changes:
                report["databases"][path.name] = changes
        for path in configurations:
            value = json.loads(path.read_text(encoding="utf-8-sig"))
            mapped = rewriter.value(value)
            if mapped != value:
                report["configurations"].append(str(path.relative_to(directory)))
                if apply:
                    temporary = path.with_name(path.name + ".remap-tmp")
                    temporary.write_text(json.dumps(mapped, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
                    os.chmod(temporary, path.stat().st_mode & 0o777)
                    os.replace(temporary, path)
        if invalidate_missing_covers:
            report["missingDerivedCoverCaches"] = missing_cover_caches(directory, runtime_directory, apply)
    report["replacementsByPrefix"] = dict(rewriter.counts)
    report["unmappedWindowsReferences"] = dict(rewriter.unmapped)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-dir", required=True, type=Path)
    parser.add_argument("--mapping-file", required=True, type=Path)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--backup-dir", type=Path)
    parser.add_argument("--invalidate-missing-covers", action="store_true")
    parser.add_argument("--runtime-data-dir", default="/data")
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    report = execute(args.data_dir, json.loads(args.mapping_file.read_text()), args.apply, args.backup_dir,
                     args.invalidate_missing_covers, args.runtime_data_dir)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
