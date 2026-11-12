"""Bounded pyzipper adapter. All cryptography and decompression belong to pyzipper.

Used only for an explicitly selected password encoding or a ZIP structure proved
within a step-authorized media wrapper. No password or encoding discovery,
archive repair, prefix scanning, or extraction of unrelated files.
"""
import binascii
import hashlib
import importlib.metadata
import json
import os
import stat
import struct
import sys
import unicodedata

MAX_ENTRIES = 10000
MAX_CATALOG = 8 * 1024 * 1024
MAX_EXPANDED = 64 * 1024 ** 3
MAX_INPUT = 1024 * 1024
CODECS = {437: "cp437", 932: "cp932", 936: "gbk", 950: "big5", 65001: "utf-8"}


class Rejected(Exception):
    def __init__(self, code):
        self.code = code


def require(value, code="ArchivePasswordEncodingUnsupported"):
    if not value:
        raise Rejected(code)


def no_links(path):
    path = os.path.abspath(path)
    while True:
        if os.path.lexists(path):
            info = os.lstat(path)
            require(not stat.S_ISLNK(info.st_mode) and
                    not (getattr(info, "st_file_attributes", 0) & 0x400), "ArchiveUnsafeOutput")
        parent = os.path.dirname(path)
        if parent == path:
            break
        path = parent


def bounded_catalog(stream):
    size = os.fstat(stream.fileno()).st_size
    require(size >= 22)
    require(stream.read(4) in (b"PK\x03\x04", b"PK\x05\x06"))
    stream.seek(max(0, size - 65557))
    tail = stream.read(65557)
    ends = [i for i in range(len(tail) - 21) if tail[i:i+4] == b"PK\x05\x06"
            and i + 22 + struct.unpack_from("<H", tail, i+20)[0] == len(tail)]
    require(len(ends) == 1)
    i = ends[0]
    disk, central_disk, on_disk, count, length, offset, _ = struct.unpack_from("<4H2IH", tail, i+4)
    absolute_end = size - len(tail) + i
    require(disk == central_disk == 0 and on_disk == count and 0 < count <= MAX_ENTRIES)
    require(count != 65535 and length != 0xffffffff and offset != 0xffffffff)
    require(0 < length <= MAX_CATALOG and offset + length == absolute_end)
    # No ZIP64, split volumes, appended bytes, or a forged prefix offset in this adapter.
    stream.seek(0)
    return count, offset


class ArchiveWindow:
    """Seekable zero-copy view of one structurally verified ZIP, excluding its media wrapper."""
    def __init__(self, stream, start, end):
        self.stream, self.start, self.length, self.position = stream, start, end-start, 0
        self.metadata_budget = 384*1024

    def tell(self):
        return self.position

    def seekable(self):
        return True

    def seek(self, offset, whence=0):
        position = offset if whence == 0 else self.position+offset if whence == 1 else self.length+offset if whence == 2 else -1
        require(0 <= position <= self.length, "ArchiveIntegrityFailure")
        self.position = position
        return position

    def read(self, count=-1):
        count = self.length-self.position if count < 0 else min(count, self.length-self.position)
        if self.metadata_budget is not None:
            require(count <= self.metadata_budget, "ArchiveIntegrityFailure")
            self.metadata_budget -= count
        self.stream.seek(self.start+self.position)
        data = self.stream.read(count)
        require(len(data) == count, "ArchiveIntegrityFailure")
        self.position += count
        return data


def structured_catalog(stream, proof):
    """Recheck an internal C# proof independently before a mature reader sees any payload."""
    require(isinstance(proof, dict), "ArchiveIntegrityFailure")
    size = os.fstat(stream.fileno()).st_size
    require(proof.get("sourceLength") == size, "ArchiveIntegrityFailure")
    tail_size = min(size, 128*1024)
    stream.seek(size-tail_size)
    tail = stream.read(tail_size)
    require(hashlib.sha256(tail).hexdigest() == proof.get("tailSha256"), "ArchiveIntegrityFailure")
    candidates, seen = [], 0
    for i in range(len(tail)-21):
        if tail[i:i+4] != b"PK\x05\x06":
            continue
        seen += 1
        require(seen <= 4, "ArchiveIntegrityFailure")
        disk, cd_disk, disk_count, count, length, offset, comment = struct.unpack_from("<4H2IH", tail, i+4)
        trailing = len(tail)-(i+22+comment)
        if not 0 <= trailing <= 32*1024 or disk != 0 or cd_disk != 0:
            continue
        central_end = size-len(tail)+i
        is_zip64 = i >= 76 and tail[i-20:i-16] == b"PK\x06\x07"
        if is_zip64:
            z = i-76
            require(tail[z:z+4] == b"PK\x06\x06" and struct.unpack_from("<Q",tail,z+4)[0] == 44, "ArchiveIntegrityFailure")
            locator_disk, locator_offset, disks = struct.unpack_from("<IQI",tail,i-16)
            zip_disk, zip_cd_disk, zip_disk_count, zip_count, zip_length, zip_offset = struct.unpack_from("<II4Q",tail,z+16)
            require(locator_disk == zip_disk == zip_cd_disk == 0 and disks == 1 and zip_disk_count == zip_count, "ArchiveIntegrityFailure")
            require(disk_count in (65535,zip_count) and count in (65535,zip_count) and
                    length in (0xffffffff,zip_length) and offset in (0xffffffff,zip_offset), "ArchiveIntegrityFailure")
            count,length,offset = zip_count,zip_length,zip_offset
            central_end = size-len(tail)+z
            require(locator_offset == offset+length, "ArchiveIntegrityFailure")
        else:
            require(disk_count == count and count != 65535 and length != 0xffffffff and offset != 0xffffffff, "ArchiveIntegrityFailure")
        require(0 < count <= 128 and 0 < length <= 64*1024, "ArchiveIntegrityFailure")
        central_start = central_end-length
        prefix = central_start-offset
        require(prefix > 0 and central_start > prefix, "ArchiveIntegrityFailure")
        archive_end = size-trailing
        candidate = {"sourceLength":size,"zipOffset":prefix,"centralDirectoryOffset":central_start,
                     "centralDirectorySize":length,"entryCount":count,"archiveEnd":archive_end,"zip64":is_zip64}
        if all(proof.get(key) == value for key,value in candidate.items()):
            candidates.append(candidate)
    require(len(candidates) == 1, "ArchiveIntegrityFailure")
    selected = candidates[0]
    stream.seek(selected["centralDirectoryOffset"])
    central = stream.read(selected["centralDirectorySize"])
    require(len(central) == selected["centralDirectorySize"] and hashlib.sha256(central).hexdigest() == proof.get("centralSha256"), "ArchiveIntegrityFailure")
    window = ArchiveWindow(stream,selected["zipOffset"],selected["archiveEnd"])
    window.metadata_budget -= tail_size+len(central)
    return window, selected["entryCount"], selected["centralDirectoryOffset"]-selected["zipOffset"]


def output_name(info, codepage, allow_zip64=False):
    # pyzipper keeps original central/local names for its own header comparison.
    # Decode the output name separately; never replace info.filename/orig_filename.
    raw = info.orig_filename.encode("utf-8" if info.flag_bits & 0x800 else "cp437", "strict")
    require(len(raw) <= 1024, "ArchiveUnsafeOutput")
    extra = info.extra
    unicode_name = None
    while extra:
        require(len(extra) >= 4, "ArchiveIntegrityFailure")
        tag, length = struct.unpack_from("<HH", extra)
        require(length <= len(extra)-4, "ArchiveIntegrityFailure")
        require(tag != 0x0017)  # PKWARE strong encryption is outside this reader's scope.
        require(tag != 0x0001 or allow_zip64)
        content = extra[4:4+length]
        if tag == 0x7075 and length >= 5 and content[0] == 1 and struct.unpack_from("<I", content, 1)[0] == binascii.crc32(raw):
            require(unicode_name is None, "ArchiveIntegrityFailure")
            unicode_name = content[5:].decode("utf-8", "strict")
        extra = extra[4+length:]
    name = unicode_name if unicode_name is not None else raw.decode(
        "utf-8" if info.flag_bits & 0x800 else CODECS[codepage], "strict")
    name = name.replace("\\", "/")
    directory = name.endswith("/")
    if directory:
        name = name[:-1]
    require(bool(name) and "\ufffd" not in name and not any(ord(c) < 32 for c in name), "ArchiveUnsafeOutput")
    pieces = name.split("/")
    # Bound the parent-path collision index as well as the original catalog bytes.
    # Unicode extras can expand a short legacy name, so check the resolved name too.
    require(len(pieces) <= 32 and len(name.encode("utf-8", "strict")) <= 1024, "ArchiveUnsafeOutput")
    for part in pieces:
        require(part not in ("", ".", "..") and not part.endswith((" ", ".")) and
                not any(c in '<>:"|?*' for c in part), "ArchiveUnsafeOutput")
        stem = part.split(".")[0].upper()
        require(stem not in ("CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$") and
                not (len(stem) == 4 and stem[:3] in ("COM", "LPT") and stem[3].isdigit()), "ArchiveUnsafeOutput")
    return name, directory


def catalog(archive, count, central_start, codepage, allow_zip64=False):
    infos = archive.infolist()
    require(len(infos) == count and archive.start_dir == central_start, "ArchiveIntegrityFailure")
    members, names, ranges = [], {}, []
    total = 0
    for info in infos:
        name, directory = output_name(info, codepage, allow_zip64)
        require(info.file_size >= 0 and info.compress_size >= 0 and info.header_offset >= 0,
                "ArchiveIntegrityFailure")
        # Explicit password-byte compatibility admits only AES or traditional ZipCrypto
        # with stored/deflate payloads. Strong encryption and masked headers stay excluded.
        require(info.compress_type in (0, 8) and not (info.flag_bits & ~0x080f))
        require(info.volume == 0)
        encrypted = bool(info.flag_bits & 1)
        aes_encrypted = encrypted and info.wz_aes_version is not None
        if aes_encrypted:
            require(info.wz_aes_version in (1, 2) and info.wz_aes_vendor_id == b"AE" and
                    info.wz_aes_strength in (1, 2, 3))
            require(info.wz_aes_version != 2 or info.CRC == 0, "ArchiveIntegrityFailure")
        else:
            require(info.wz_aes_version is None and info.wz_aes_vendor_id is None and
                    info.wz_aes_strength is None)
        mode = (info.external_attr >> 16) & 0xf000
        require(mode in (0, stat.S_IFDIR if directory else stat.S_IFREG) and
                not (info.external_attr & 0x400), "ArchiveUnsafeOutput")
        require(not directory or info.file_size == 0 and not encrypted, "ArchiveUnsafeOutput")
        # Check local framing before the mature reader opens any payload; reject overlaps.
        archive.fp.seek(info.header_offset)
        local = archive.fp.read(30)
        require(len(local) == 30 and local[:4] == b"PK\x03\x04", "ArchiveIntegrityFailure")
        flags, method = struct.unpack_from("<HH", local, 6)
        crc, packed, unpacked = struct.unpack_from("<3I", local, 14)
        name_length, extra_length = struct.unpack_from("<HH", local, 26)
        require(not allow_zip64 or 30+name_length+extra_length <= 4096, "ArchiveIntegrityFailure")
        require(flags == info.flag_bits and method == (99 if aes_encrypted else info.compress_type), "ArchiveIntegrityFailure")
        begin = info.header_offset + 30 + name_length + extra_length
        end = begin + info.compress_size
        require(begin <= end <= central_start, "ArchiveIntegrityFailure")
        raw_name = archive.fp.read(name_length)
        expected_name = info.orig_filename.encode("utf-8" if info.flag_bits & 0x800 else "cp437", "strict")
        require(raw_name == expected_name, "ArchiveIntegrityFailure")
        local_extra = archive.fp.read(extra_length)
        aes = []
        zip64 = False
        while local_extra:
            require(len(local_extra) >= 4, "ArchiveIntegrityFailure")
            tag, length = struct.unpack_from("<HH", local_extra)
            require(length <= len(local_extra)-4, "ArchiveIntegrityFailure")
            require(tag != 0x0017)
            if tag == 0x0001:
                require(allow_zip64 and not zip64, "ArchivePasswordEncodingUnsupported")
                zip64 = True
                need_size,need_packed = unpacked == 0xffffffff,packed == 0xffffffff
                require(length == 8*(need_size+need_packed) and length > 0, "ArchiveIntegrityFailure")
                position = 4
                if need_size:
                    unpacked = struct.unpack_from("<Q",local_extra,position)[0]
                    position += 8
                if need_packed:
                    packed = struct.unpack_from("<Q",local_extra,position)[0]
            if tag == 0x9901:
                require(length == 7, "ArchiveIntegrityFailure")
                aes.append(struct.unpack_from("<H2sBH", local_extra, 4))
            local_extra = local_extra[4+length:]
        require((crc, packed, unpacked) == (info.CRC, info.compress_size, info.file_size) or
                flags & 8 and crc in (0, info.CRC) and packed in (0, info.compress_size) and
                unpacked in (0, info.file_size), "ArchiveIntegrityFailure")
        require(aes == ([(info.wz_aes_version,b"AE",info.wz_aes_strength,info.compress_type)] if aes_encrypted else []), "ArchiveIntegrityFailure")
        overhead = ({1:8,2:12,3:16}[info.wz_aes_strength] + 12) if aes_encrypted else 12 if encrypted else 0
        require(info.compress_size >= overhead and (info.compress_type != 0 or info.compress_size-overhead == info.file_size), "ArchiveIntegrityFailure")
        if flags & 8:
            # Readers use the central record for sizes/CRC. Independently validate the
            # trailing descriptor too, rather than accepting contradictory framing.
            archive.fp.seek(end)
            descriptor = archive.fp.read(min(24, central_start-end))
            width, format = (20, "<IQQ") if zip64 else (12, "<III")
            starts = (0, 4) if descriptor[:4] == b"PK\x07\x08" else (0,)
            matches = [start+width for start in starts if len(descriptor) >= start+width and
                       struct.unpack_from(format, descriptor, start) == (info.CRC, info.compress_size, info.file_size)]
            require(len(matches) == 1, "ArchiveIntegrityFailure")
            end += matches[0]
        ranges.append((info.header_offset, end))
        total += info.file_size
        require(total <= MAX_EXPANDED, "ArchiveExpansionLimit")
        parts = name.split("/")
        for depth in range(1, len(parts)+1):
            path = "/".join(parts[:depth])
            is_dir = depth < len(parts) or directory
            explicit = depth == len(parts)
            key = unicodedata.normalize("NFC", path).casefold()
            previous = names.get(key)
            require(previous is None or previous[0] == path and previous[1] == is_dir and not (explicit and previous[2]), "ArchiveUnsafeOutput")
            names[key] = (path, is_dir, explicit or bool(previous and previous[2]))
        members.append((info, name, directory))
    ranges.sort()
    require(ranges[0][0] == 0 and all(ranges[i][0] >= ranges[i-1][1] for i in range(1, len(ranges))), "ArchiveIntegrityFailure")
    return members, total


def consume(archive, members, password, destination, progress):
    total = sum(info.file_size for info, _, _ in members)
    done = 0
    for info, name, directory in members:
        target = None if destination is None else os.path.join(destination, *name.split("/"))
        output = None
        try:
            with archive.open(info, "r", pwd=password) as source:
                if target is not None and not directory:
                    no_links(os.path.dirname(target))
                    os.makedirs(os.path.dirname(target), exist_ok=True)
                    output = open(target, "xb")
                written = 0
                while True:
                    data = source.read(128*1024)
                    if not data:
                        break
                    written += len(data)
                    require(written <= info.file_size and done + written <= MAX_EXPANDED, "ArchiveExpansionLimit")
                    if output is not None:
                        output.write(data)
                    progress(min(99, int((done+written)*100/max(1,total))))
                # EOF is required: this is where pyzipper authenticates AES and checks CRC.
                require(written == info.file_size and source._left == 0 and source._compress_left == 0,
                        "ArchiveIntegrityFailure")
                if source._decompressor is not None and hasattr(source._decompressor, "eof"):
                    require(source._decompressor.eof, "ArchiveIntegrityFailure")
                if target is not None and directory:
                    no_links(target)
                    os.makedirs(target, exist_ok=True)
                done += written
        finally:
            if output is not None:
                output.close()


def run(request):
    require(importlib.metadata.version("pyzipper") == "0.4.0", "ArchiveCompatibilityDependencyMissing")
    import pyzipper
    # pyzipper deliberately clips returned bytes to the declared uncompressed size.
    # Count its mature decompressor's bytes before that clipping, so an AE-2 member
    # with an altered size cannot authenticate a silently truncated output.
    class CountedDecompressor:
        def __init__(self, decoder, limit):
            self.decoder, self.limit, self.total = decoder, limit, 0
        def __getattr__(self, name):
            return getattr(self.decoder, name)
        def checked(self, data):
            self.total += len(data)
            require(self.total <= self.limit, "ArchiveIntegrityFailure")
            return data
        def decompress(self, *args):
            return self.checked(self.decoder.decompress(*args))
        def flush(self, *args):
            return self.checked(self.decoder.flush(*args))
    class BoundedMember(pyzipper.zipfile_aes.AESZipExtFile):
        def get_decompressor(self, method):
            decoder = super().get_decompressor(method)
            return None if decoder is None else CountedDecompressor(decoder, self._zinfo.file_size)
    class BoundedArchive(pyzipper.AESZipFile):
        zipextfile_cls = BoundedMember
    require(request.get("operation") in ("list", "test", "extract"))
    codepage = request.get("passwordCodePage")
    names = request.get("fileNameCodePage") or 437
    require(codepage in CODECS and names in CODECS)
    password = request.get("password")
    require(password is None or isinstance(password, str) and len(password) <= 2048)
    try:
        encoded = None if password is None else password.encode(CODECS[codepage], "strict")
    except UnicodeEncodeError:
        raise Rejected("ArchivePasswordNotRepresentable")
    path = request["path"]
    no_links(path)
    require(stat.S_ISREG(os.stat(path).st_mode))
    with open(path, "rb") as stream:
        before = os.fstat(stream.fileno())
        proof = request.get("structure")
        if proof is None:
            count, central = bounded_catalog(stream)
            archive_source = stream
        else:
            archive_source, count, central = structured_catalog(stream, proof)
        with BoundedArchive(archive_source, "r") as archive:
            members, total = catalog(archive, count, central, names, allow_zip64=proof is not None)
            if request["operation"] == "list":
                return {"entries": [{"path": name, "size": info.file_size} for info,name,directory in members if not directory]}
            if isinstance(archive_source, ArchiveWindow):
                archive_source.metadata_budget = None
            last = [-1]
            def progress(value):
                if value != last[0]:
                    last[0] = value
                    print(json.dumps({"progress": value}), flush=True)
            # Validate the entire exact input before creating any output directory.
            consume(archive, members, encoded, None, progress)
            if request["operation"] == "extract":
                destination = os.path.abspath(request["destination"])
                no_links(destination)
                require(not os.path.exists(destination) or os.path.isdir(destination) and not os.listdir(destination), "ArchiveUnsafeOutput")
                require(os.path.commonpath([os.path.abspath(path), destination]) != destination, "ArchiveUnsafeOutput")
                os.makedirs(destination, exist_ok=True)
                # Reading to EOF authenticates again while writing into the private staging tree.
                consume(archive, members, encoded, destination, progress)
            after = os.fstat(stream.fileno())
            require((before.st_size,before.st_mtime_ns,before.st_ino) == (after.st_size,after.st_mtime_ns,after.st_ino), "ArchiveIntegrityFailure")
    return {"fileCount": sum(not directory for _,_,directory in members), "totalBytes": total}


def main():
    try:
        data = sys.stdin.buffer.read(MAX_INPUT+1)
        require(len(data) <= MAX_INPUT)
        result = run(json.loads(data))
        print(json.dumps({"ok": True, **result}, ensure_ascii=True), flush=True)
    except Rejected as error:
        print(json.dumps({"ok": False, "code": error.code}), flush=True)
        sys.exit(2)
    except Exception as error:
        # Known error categories only: never return a password, source name, or raw exception.
        message = str(error).lower()
        code = "ArchivePasswordRejected" if isinstance(error, RuntimeError) and ("password" in message or "encrypted" in message) else "ArchiveIntegrityFailure"
        if isinstance(error, (ImportError, ModuleNotFoundError)):
            code = "ArchiveCompatibilityDependencyMissing"
        if isinstance(error, OSError):
            code = "ArchiveStorageUnavailable"
        print(json.dumps({"ok": False, "code": code}), flush=True)
        sys.exit(2)


if __name__ == "__main__":
    main()
