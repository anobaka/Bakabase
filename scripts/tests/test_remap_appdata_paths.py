import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest


spec = importlib.util.spec_from_file_location("remap", Path(__file__).parents[1] / "remap_appdata_paths.py")
remap = importlib.util.module_from_spec(spec)
spec.loader.exec_module(remap)


class RemapTests(unittest.TestCase):
    def test_nested_json_paths_delimiters_and_longest_prefix(self):
        rewriter = remap.Rewriter({"Y:/": "/Volumes/nas", "Y:/USE": "/Volumes/nas/Use", "C:/Data": "/data"})
        original = {"paths": 'Y:\\USE\\目录|Y:/TBD/Book', "nested": json.dumps(["C:/Data/covers/a.jpg"]),
                    "url": "https://example.test/C:/Data/a", "prose": "See Y:/TBD/Book", "near": "C:/Database/a"}
        result = rewriter.value(original)
        self.assertEqual("/Volumes/nas/Use/目录|/Volumes/nas/TBD/Book", result["paths"])
        self.assertEqual(["/data/covers/a.jpg"], json.loads(result["nested"]))
        for key in ("url", "prose", "near"):
            self.assertEqual(original[key], result[key])

    def test_key_collisions_are_rejected(self):
        with self.assertRaises(ValueError):
            remap.Rewriter({"Y:/": "/nas"}).value({"Y:/a": 1, "/nas/a": 2})

    def test_standard_values_preserve_escaped_commas_and_map_every_item(self):
        values = ["Y:/a,b/image.jpg", "Z:/next,c/image.jpg", "plain\\text", "https://example.test/a,b"]
        serialized = remap.join_standard(values)
        mapped = remap.Rewriter({"Y:/": "/nas-y", "Z:/": "/nas-z"}).standard(serialized, 2)
        self.assertEqual(["/nas-y/a,b/image.jpg", "/nas-z/next,c/image.jpg", *values[2:]], remap.split_standard(mapped))
        self.assertEqual(["a\\x", "tail\\"], remap.split_standard("a\\x,tail\\"))
        nested = remap.join_standard([serialized, remap.join_standard(["Y:/semi;colon", "unrelated"])], ";")
        rewritten = remap.Rewriter({"Y:/": "/nas-y", "Z:/": "/nas-z"}).standard(nested, 8)
        self.assertEqual("/nas-y/semi;colon", remap.split_standard(remap.split_standard(rewritten, ";")[1])[0])

    def test_preview_apply_backup_and_idempotence(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            data = base / "data"
            data.mkdir()
            database = data / "bakabase_insideworld.db"
            with sqlite3.connect(database) as connection:
                connection.execute("CREATE TABLE Resources(Id INTEGER PRIMARY KEY, Path TEXT, Options TEXT)")
                connection.execute("INSERT INTO Resources VALUES(1, ?, ?)", ("Y:/book", '["C:/Data/a.jpg"]'))
            (data / "configs").mkdir()
            config = data / "configs" / "file-system.json"
            config.write_text(json.dumps({"destination": "Y:/book"}))
            before = database.read_bytes()
            mapping = {"Y:/": "/nas", "C:/Data": "/data"}
            preview = remap.execute(data, mapping)
            self.assertEqual(before, database.read_bytes())
            self.assertFalse((data / ".bakabase.lock").exists())
            self.assertEqual({"Resources.Path": 1, "Resources.Options": 1}, preview["databases"][database.name])
            backup = base / "backup"
            applied = remap.execute(data, mapping, True, backup)
            self.assertEqual(before, (backup / database.name).read_bytes())
            with sqlite3.connect(database) as connection:
                self.assertEqual(("/nas/book", '["/data/a.jpg"]'), connection.execute("SELECT Path,Options FROM Resources").fetchone())
            self.assertEqual({"destination": "/nas/book"}, json.loads(config.read_text()))
            self.assertEqual(preview["replacementsByPrefix"], applied["replacementsByPrefix"])
            self.assertEqual({}, remap.execute(data, mapping)["databases"])

    def test_database_collision_rolls_back(self):
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "test.db"
            with sqlite3.connect(database) as connection:
                connection.execute("CREATE TABLE Items(Id INTEGER PRIMARY KEY, Path TEXT UNIQUE)")
                connection.executemany("INSERT INTO Items(Path) VALUES(?)", [("Y:/same",), ("/nas/same",)])
            with self.assertRaises(sqlite3.IntegrityError):
                remap.rewrite_database(database, remap.Rewriter({"Y:/": "/nas"}), True)
            with sqlite3.connect(database) as connection:
                self.assertEqual([("Y:/same",), ("/nas/same",)], connection.execute("SELECT Path FROM Items ORDER BY Id").fetchall())

    def test_real_column_schema_decodes_standard_values_but_not_raw_path_commas(self):
        with tempfile.TemporaryDirectory() as temporary:
            database = Path(temporary) / "test.db"
            value = remap.join_standard(["Y:/one,two/a.jpg", "Z:/three,four/b.jpg"])
            with sqlite3.connect(database) as connection:
                connection.executescript("CREATE TABLE CustomProperties(Id INTEGER PRIMARY KEY,Type INTEGER);"
                                         "INSERT INTO CustomProperties VALUES(1,10);"
                                         "CREATE TABLE CustomPropertyValues(Id INTEGER PRIMARY KEY,PropertyId INTEGER,Value TEXT);"
                                         "CREATE TABLE ResourcesV2(Id INTEGER PRIMARY KEY,Path TEXT);"
                                         "CREATE TABLE PlayHistories(Id INTEGER PRIMARY KEY,Item TEXT);")
                connection.execute("INSERT INTO CustomPropertyValues VALUES(1,1,?)", (value,))
                connection.execute("INSERT INTO ResourcesV2 VALUES(1,?)", ("Y:/one,two/a.jpg",))
                connection.execute("INSERT INTO PlayHistories VALUES(1,?)", ("FileSystem:Y:/one,two/a.jpg",))
            remap.rewrite_database(database, remap.Rewriter({"Y:/": "/nas-y", "Z:/": "/nas-z"}), True)
            with sqlite3.connect(database) as connection:
                mapped = connection.execute("SELECT Value FROM CustomPropertyValues").fetchone()[0]
                self.assertEqual(["/nas-y/one,two/a.jpg", "/nas-z/three,four/b.jpg"], remap.split_standard(mapped))
                self.assertEqual("/nas-y/one,two/a.jpg", connection.execute("SELECT Path FROM ResourcesV2").fetchone()[0])
                self.assertEqual("FileSystem:/nas-y/one,two/a.jpg", connection.execute("SELECT Item FROM PlayHistories").fetchone()[0])

    def test_busy_instance_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            with remap.own_directory(Path(temporary)):
                with self.assertRaises(RuntimeError):
                    with remap.own_directory(Path(temporary)):
                        self.fail("Lock should not be shared")

    def test_only_missing_derived_covers_are_invalidated(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary).resolve()
            (directory / "existing.jpg").write_bytes(b"image")
            with sqlite3.connect(directory / "bakabase_insideworld.db") as connection:
                connection.execute("CREATE TABLE ResourceCaches(ResourceId INTEGER PRIMARY KEY,CoverPaths TEXT,CachedTypes INTEGER)")
                connection.executemany("INSERT INTO ResourceCaches VALUES(?,?,?)", [
                    (1, '["/data/missing.jpg"]', 3), (2, '["/data/existing.jpg"]', 3),
                    (3, '["https://example.test/image.jpg"]', 1), (4, "/data/missing2.jpg", 3)])
            self.assertEqual(2, remap.missing_cover_caches(directory, "/data", False))
            self.assertEqual(2, remap.missing_cover_caches(directory, "/data", True))
            with sqlite3.connect(directory / "bakabase_insideworld.db") as connection:
                self.assertEqual([(1, 2), (2, 3), (3, 1), (4, 2)], connection.execute("SELECT ResourceId,CachedTypes FROM ResourceCaches ORDER BY ResourceId").fetchall())


if __name__ == "__main__":
    unittest.main()
