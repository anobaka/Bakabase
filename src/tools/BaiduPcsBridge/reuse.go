package main

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"os"
	"path"
	"path/filepath"
	"sort"
	"strconv"
	"strings"

	"github.com/qjfoidnh/BaiduPCS-Go/baidupcs"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsconfig"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsfunctions/pcsdownload"
)

type reuseSource struct {
	OperationID          string `json:"operationId"`
	OutputDirectory      string `json:"outputDirectory"`
	InventoryFingerprint string `json:"inventoryFingerprint"`
	DisableSourceRead    bool   `json:"disableSourceRead,omitempty"`
}
type reuseInventory struct {
	Type               string `json:"type"`
	UID                string `json:"uid"`
	FileCount          int    `json:"fileCount"`
	TotalBytes         int64  `json:"totalBytes"`
	CandidateFileCount int    `json:"candidateFileCount"`
	CandidateBytes     int64  `json:"candidateBytes"`
	Fingerprint        string `json:"inventoryFingerprint"`
}
type reuseStamp struct {
	ID                      string
	Mode                    uint32
	Size, Modified, Changed int64
	Links                   uint64
}

func reuseStat(name string) (reuseStamp, error) {
	var out reuseStamp
	info, err := os.Lstat(name)
	if err != nil {
		return out, err
	}
	if !info.Mode().IsRegular() && !info.IsDir() {
		return out, fail("BAIDU_UNSAFE_REMOTE_PATH")
	}
	out.ID, out.Changed, out.Links, err = reuseIdentity(name)
	if err != nil {
		return out, err
	}
	out.Mode = uint32(info.Mode())
	out.Size = info.Size()
	out.Modified = info.ModTime().UnixNano()
	return out, nil
}

type reuseMember struct {
	Entry entry
	Path  string
	Stamp reuseStamp
}

func reuseRoot(name string) error {
	if !filepath.IsAbs(name) || filepath.Clean(name) != name || filepath.Dir(name) == name {
		return fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	for p := name; ; p = filepath.Dir(p) {
		s, err := os.Lstat(p)
		if err != nil && !os.IsNotExist(err) {
			return storageFail("BAIDU_OUTPUT_DIRECTORY_FAILED", "inspectDirectory", err)
		}
		if err == nil && (!s.IsDir() || s.Mode()&os.ModeSymlink != 0) {
			return fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		if filepath.Dir(p) == p {
			break
		}
	}
	return nil
}
func reuseDirsDisjoint(src, dst string) error {
	// Canonical names alone do not distinguish two bind mounts of the same directory.
	for _, pair := range [][2]string{{src, dst}, {dst, src}} {
		a, err := os.Stat(pair[0])
		if err != nil && !os.IsNotExist(err) {
			return err
		}
		for p := pair[1]; ; p = filepath.Dir(p) {
			if pair[0] == p {
				return fail("BAIDU_REUSE_TARGET_CONFLICT")
			}
			b, err := os.Stat(p)
			if err != nil && !os.IsNotExist(err) {
				return err
			}
			if err == nil && a != nil && os.SameFile(a, b) {
				return fail("BAIDU_REUSE_TARGET_CONFLICT")
			}
			if p == filepath.Dir(p) {
				break
			}
		}
	}
	return nil
}
func reuseHash(v any) string {
	data, _ := json.Marshal(v)
	digest := sha256.Sum256(data)
	return hex.EncodeToString(digest[:])
}
func reuseLayout(root, remote string, entries []entry, paths map[string]string) (map[string]bool, error) {
	dirs := map[string]bool{root: true}
	used := map[string]bool{}
	for _, e := range entries {
		if e.Path != remote && !strings.HasPrefix(e.Path, strings.TrimRight(remote, "/")+"/") {
			return nil, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
		rel, ok := paths[e.Path]
		if !ok || rel == "" {
			return nil, fail("BAIDU_LOCAL_PATH_MAPPING_INVALID")
		}
		p, err := safeLocal(root, rel)
		if err != nil {
			return nil, err
		}
		if p == root || used[p] {
			return nil, fail("BAIDU_LOCAL_PATH_MAPPING_CONFLICT")
		}
		used[p] = true
		if e.IsDirectory {
			dirs[p] = true
		} else {
			if used[p+pcsdownload.DownloadSuffix] {
				return nil, fail("BAIDU_LOCAL_PATH_MAPPING_CONFLICT")
			}
			used[p+pcsdownload.DownloadSuffix] = true
		}
		for d := filepath.Dir(p); d != root; d = filepath.Dir(d) {
			dirs[d] = true
		}
	}
	return dirs, nil
}
func inspectReuseSource(ctx context.Context, config, uid string, r request) (reuseInventory, map[int64]reuseMember, error) {
	var zero reuseInventory
	if uid == "" || uid == "0" || r.ExpectedUID != uid || r.OperationID == "" || !path.IsAbs(r.RemotePath) || path.Clean(r.RemotePath) != r.RemotePath || !sameManifest(r.ExpectedManifest, r.ExpectedManifest) {
		return zero, nil, fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	if err := reuseRoot(r.OutputDirectory); err != nil {
		return zero, nil, err
	}
	r.Command = "inspect-reuse-source"
	paths, err := resolveDownloadPaths(ctx, config, uid, r.OutputDirectory, r, r.ExpectedManifest, false)
	if err != nil {
		return zero, nil, err
	}
	return inventoryReuseTree(ctx, uid, r, paths)
}
func inventoryReuseTree(ctx context.Context, uid string, r request, paths map[string]string) (reuseInventory, map[int64]reuseMember, error) {
	root := r.OutputDirectory
	out := reuseInventory{Type: "result", UID: uid}
	allowedDirs, err := reuseLayout(root, r.RemotePath, r.ExpectedManifest, paths)
	if err != nil {
		return out, nil, err
	}
	ordered := append([]entry(nil), r.ExpectedManifest...)
	sort.Slice(ordered, func(i, j int) bool { return ordered[i].ID < ordered[j].ID })
	allow := map[string]bool{}
	present := map[string]reuseStamp{}
	members := map[int64]reuseMember{}
	for _, e := range ordered {
		if e.IsDirectory {
			continue
		}
		p := filepath.Join(root, filepath.FromSlash(paths[e.Path]))
		if e.Size < 0 || out.TotalBytes > (1<<63-1)-e.Size {
			return out, nil, fail("BAIDU_MANIFEST_FAILED")
		}
		out.TotalBytes += e.Size
		out.FileCount++
		allow[p] = true
		allow[p+pcsdownload.DownloadSuffix] = true
	}
	if out.FileCount == 0 {
		return out, nil, fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	count := 0
	err = filepath.WalkDir(root, func(p string, d os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectReuseSource", walkErr)
		}
		if err := ctx.Err(); err != nil {
			return err
		}
		count++
		if count > 30000 {
			return fail("BAIDU_INSPECTION_LIMIT")
		}
		if d.Type()&os.ModeSymlink != 0 {
			return fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		if (d.IsDir() && !allowedDirs[p]) || (!d.IsDir() && !allow[p]) {
			return fail("BAIDU_OUTPUT_UNEXPECTED_FILE")
		}
		stamp, err := reuseStat(p)
		if err != nil {
			return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectReuseSource", err)
		}
		if !d.IsDir() && stamp.Links != 1 {
			return fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		present[p] = stamp
		return nil
	})
	if err != nil {
		return out, nil, err
	}
	for _, e := range ordered {
		if e.IsDirectory {
			continue
		}
		p := filepath.Join(root, filepath.FromSlash(paths[e.Path]))
		if s, ok := present[p]; ok && os.FileMode(s.Mode).IsRegular() && s.Size == e.Size {
			members[e.ID] = reuseMember{e, p, s}
			out.CandidateBytes += e.Size
			out.CandidateFileCount++
		}
	}
	out.Fingerprint = reuseHash([]any{uid, r.OperationID, root, r.RemotePath, ordered, paths, present})
	return out, members, nil
}

type reuseOwned struct {
	DataID, SideID, SHA string
	Prepared, Published bool
}
type reuseReceipt struct {
	Version                        int
	Intent, RootID, Stage, StageID string
	Prepared, Published            bool
	Files                          map[string]*reuseOwned
}
type reuseSession struct {
	root, configPath, stage string
	receipt                 reuseReceipt
	lock                    *os.File
}

func (s *reuseSession) save() error { return writePrivateReceipt(s.configPath, s.receipt) }

func reuseConflict() error { return fail("BAIDU_REUSE_TARGET_CONFLICT") }
func readReuseReceipt(p string) (reuseReceipt, error) {
	var v reuseReceipt
	st, err := os.Lstat(p)
	if err != nil {
		return v, err
	}
	if !st.Mode().IsRegular() || st.Size() > 8<<20 {
		return v, reuseConflict()
	}
	data, err := os.ReadFile(p)
	if err != nil {
		return v, err
	}
	if json.Unmarshal(data, &v) != nil {
		return v, reuseConflict()
	}
	return v, nil
}
func openReuseSession(config string, r request, intent string, write bool) (*reuseSession, error) {
	dir := filepath.Join(config, "operations")
	if err := reuseRoot(dir); err != nil {
		return nil, err
	}
	s := &reuseSession{root: r.OutputDirectory, configPath: filepath.Join(dir, reuseHash(r.OperationID)+".reuse-v1.json")}
	if write {
		if err := os.MkdirAll(dir, 0700); err != nil {
			return nil, err
		}
		f, err := os.OpenFile(s.configPath+".lock", os.O_RDWR|os.O_CREATE, 0600)
		if err != nil {
			return nil, err
		}
		if err = reuseLock(f); err != nil {
			f.Close()
			return nil, fail("BAIDU_REUSE_OPERATION_BUSY")
		}
		s.lock = f
	}
	success := false
	defer func() {
		if !success && s.lock != nil {
			s.lock.Close()
		}
	}()
	rec, err := readReuseReceipt(s.configPath)
	if os.IsNotExist(err) {
		names, readErr := os.ReadDir(s.root)
		if readErr != nil && !os.IsNotExist(readErr) {
			return nil, readErr
		}
		if len(names) != 0 {
			return nil, reuseConflict()
		}
		if !write {
			return nil, nil
		}
		if err = os.MkdirAll(s.root, 0700); err != nil {
			return nil, err
		}
		rootID, _, _, err := reuseIdentity(s.root)
		if err != nil {
			return nil, err
		}
		var random [16]byte
		if _, err = rand.Read(random[:]); err != nil {
			return nil, err
		}
		rec = reuseReceipt{Version: 1, Intent: intent, RootID: rootID, Stage: ".bakabase-baidu-reuse-" + hex.EncodeToString(random[:]), Files: map[string]*reuseOwned{}}
		s.receipt = rec
		if err = s.save(); err != nil {
			return nil, err
		}
	} else if err != nil {
		return nil, err
	}
	if rec.Version != 1 || rec.Intent != intent || rec.Files == nil || !strings.HasPrefix(rec.Stage, ".bakabase-baidu-reuse-") || len(rec.Stage) != len(".bakabase-baidu-reuse-")+32 || filepath.Base(rec.Stage) != rec.Stage {
		return nil, reuseConflict()
	}
	if _, err = hex.DecodeString(strings.TrimPrefix(rec.Stage, ".bakabase-baidu-reuse-")); err != nil {
		return nil, reuseConflict()
	}
	rootID, _, _, err := reuseIdentity(s.root)
	if err != nil || rootID != rec.RootID {
		return nil, reuseConflict()
	}
	s.receipt = rec
	s.stage = filepath.Join(s.root, rec.Stage)
	if !rec.Published {
		st, err := os.Lstat(s.stage)
		if os.IsNotExist(err) && rec.StageID == "" && write {
			if err = os.Mkdir(s.stage, 0700); err != nil {
				return nil, err
			}
			st, err = os.Lstat(s.stage)
		}
		if err != nil || !st.IsDir() || st.Mode()&os.ModeSymlink != 0 {
			return nil, reuseConflict()
		}
		id, _, _, err := reuseIdentity(s.stage)
		if err != nil {
			return nil, err
		}
		if rec.StageID == "" {
			names, err := os.ReadDir(s.stage)
			if !write || err != nil || len(names) != 0 {
				return nil, reuseConflict()
			}
			s.receipt.StageID = id
			if err = s.save(); err != nil {
				return nil, err
			}
		} else if rec.StageID != id {
			return nil, reuseConflict()
		}
	}
	success = true
	return s, nil
}
func (s *reuseSession) close() {
	if s != nil && s.lock != nil {
		s.lock.Close()
	}
}
func (s *reuseSession) working(id string) string { return filepath.Join(s.stage, "f-"+id) }
func (s *reuseSession) reclaimSpace(paths map[string]string, entries []entry) error {
	for _, e := range entries {
		if e.IsDirectory {
			continue
		}
		id := strconv.FormatInt(e.ID, 10)
		item := s.receipt.Files[id]
		if item == nil || !item.Published {
			return reuseConflict()
		}
		target, err := safeLocal(s.root, paths[e.Path])
		if err != nil {
			return err
		}
		final, _, _, err := reuseIdentity(target)
		if err != nil || final != item.DataID {
			return reuseConflict()
		}
		working := s.working(id)
		for _, p := range []struct{ name, id string }{{working, item.DataID}, {working + pcsdownload.DownloadSuffix, item.SideID}} {
			if st, err := os.Lstat(p.name); os.IsNotExist(err) {
				continue
			} else if err != nil || !st.Mode().IsRegular() {
				return reuseConflict()
			}
			found, _, _, err := reuseIdentity(p.name)
			if err != nil || found != p.id {
				return reuseConflict()
			}
			if err = os.Remove(p.name); err != nil {
				return err
			}
		}
	}
	if st, err := os.Lstat(s.stage); err == nil {
		id, _, _, err := reuseIdentity(s.stage)
		if err != nil || id != s.receipt.StageID || !st.IsDir() {
			return reuseConflict()
		}
		if err = os.Remove(s.stage); err != nil {
			return err
		}
	} else if !os.IsNotExist(err) {
		return err
	}
	return syncReuseDir(s.root)
}

func validateReuse(r request, entries []entry) (request, string, error) {
	old := r.ReuseSource
	if old == nil || old.OperationID == "" || old.OperationID == r.OperationID || r.OperationID == "" || r.ExpectedUID == "" || r.ExpectedUID == "0" || !sameManifest(r.ExpectedManifest, entries) || !path.IsAbs(r.RemotePath) || path.Clean(r.RemotePath) != r.RemotePath {
		return r, "", fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	finger, err := hex.DecodeString(old.InventoryFingerprint)
	if err != nil || len(finger) != sha256.Size {
		return r, "", fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	if !filepath.IsAbs(old.OutputDirectory) || filepath.Clean(old.OutputDirectory) != old.OutputDirectory || filepath.Dir(old.OutputDirectory) == old.OutputDirectory {
		return r, "", fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	if err = reuseRoot(r.OutputDirectory); err != nil {
		return r, "", err
	}
	sorted := append([]entry(nil), entries...)
	sort.Slice(sorted, func(i, j int) bool { return sorted[i].ID < sorted[j].ID })
	intent := reuseHash([]any{r.ExpectedUID, r.RemotePath, r.OperationID, r.OutputDirectory, old.OperationID, old.OutputDirectory, old.InventoryFingerprint, sorted})
	r.ReuseSource = nil
	r.OperationID = old.OperationID
	r.OutputDirectory = old.OutputDirectory
	r.Command = "inspect-reuse-source"
	return r, intent, nil
}
func (s *reuseSession) validateTarget(ctx context.Context, r request, paths map[string]string) error {
	dirs, err := reuseLayout(s.root, r.RemotePath, r.ExpectedManifest, paths)
	if err != nil {
		return err
	}
	if dirs[s.stage] {
		return reuseConflict()
	}
	targets := map[string]string{}
	allowedStage := map[string]string{}
	partnerPaths := map[string]string{}
	for _, e := range r.ExpectedManifest {
		if e.IsDirectory {
			continue
		}
		p := filepath.Join(s.root, filepath.FromSlash(paths[e.Path]))
		if p == s.stage {
			return reuseConflict()
		}
		id := strconv.FormatInt(e.ID, 10)
		if owned := s.receipt.Files[id]; owned != nil {
			if owned.SHA != "" {
				targets[p] = owned.DataID
				partnerPaths[p] = s.working(id)
				partnerPaths[s.working(id)] = p
			}
			allowedStage[s.working(id)] = owned.DataID
			allowedStage[s.working(id)+pcsdownload.DownloadSuffix] = owned.SideID
		}
	}
	count := 0
	return filepath.WalkDir(s.root, func(p string, d os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectReuseTarget", walkErr)
		}
		if err := ctx.Err(); err != nil {
			return err
		}
		count++
		if count > 60000 {
			return fail("BAIDU_INSPECTION_LIMIT")
		}
		if d.Type()&os.ModeSymlink != 0 {
			return fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		if d.IsDir() {
			if p == s.stage {
				id, _, _, err := reuseIdentity(p)
				if err != nil || id != s.receipt.StageID {
					return reuseConflict()
				}
				return nil
			}
			if !dirs[p] {
				return reuseConflict()
			}
			return nil
		}
		st, err := reuseStat(p)
		if err != nil {
			return err
		}
		want := targets[p]
		private := filepath.Dir(p) == s.stage
		if private {
			want = allowedStage[p]
		}
		if want == "" || st.ID != want {
			return reuseConflict()
		}
		// Two links exist only between our own final output and its private payload during publication.
		if st.Links != 1 {
			if st.Links != 2 || !s.receipt.Prepared {
				return reuseConflict()
			}
			partner := partnerPaths[p]
			if partner == "" {
				return reuseConflict()
			}
			partnerID, _, _, err := reuseIdentity(partner)
			if err != nil || partnerID != want {
				return reuseConflict()
			}
		}
		return nil
	})
}
func (s *reuseSession) ensureWorking(id string, side bool) (string, error) {
	item := s.receipt.Files[id]
	if item == nil {
		item = &reuseOwned{}
		s.receipt.Files[id] = item
	}
	name := s.working(id)
	saved := &item.DataID
	if side {
		name += pcsdownload.DownloadSuffix
		saved = &item.SideID
	}
	st, err := reuseStat(name)
	if err == nil {
		if *saved == "" || st.ID != *saved || st.Links != 1 || !os.FileMode(st.Mode).IsRegular() {
			return "", reuseConflict()
		}
		return name, nil
	}
	if !os.IsNotExist(err) {
		return "", err
	}
	if !side && (*saved != "" && item.SHA != "" || item.Published) {
		return "", reuseConflict()
	}
	f, err := os.OpenFile(name, os.O_CREATE|os.O_EXCL|os.O_RDWR, 0600)
	if err != nil {
		return "", err
	}
	err = f.Close()
	if err != nil {
		return "", err
	}
	*saved, _, _, err = reuseIdentity(name)
	if err != nil {
		return "", err
	}
	if err = s.save(); err != nil {
		return "", err
	}
	return name, nil
}

type reuseMetadata func(*baidupcs.FileDirectory) (*baidupcs.FileDirectory, error)
type reuseExecute func(*baidupcs.FileDirectory, string, func(int64)) error

func metadataFor(fd *baidupcs.FileDirectory, fn reuseMetadata) func() (*baidupcs.FileDirectory, error) {
	return func() (*baidupcs.FileDirectory, error) { return fn(fd) }
}

// A confirmed zero length fixes the entire byte sequence even if the provider
// omits its block_list for empty objects. Keep the same two observations and local identity fence.
func verifyReuseMember(ctx context.Context, local string, e entry, metadata func() (*baidupcs.FileDirectory, error), progress verificationProgress) (verifiedExistingFile, error) {
	if e.Size != 0 {
		return verifyExistingFileTo(ctx, local, e, metadata, progress)
	}
	original, err := existingMemberInfo(local, 0)
	if err != nil {
		return verifiedExistingFile{}, err
	}
	for range 2 {
		if err = ctx.Err(); err != nil {
			return verifiedExistingFile{}, err
		}
		fd, err := metadata()
		if err != nil {
			return verifiedExistingFile{}, fail("BAIDU_MANIFEST_FAILED")
		}
		if fd == nil || fd.Isdir || fd.FsID != e.ID || fd.Size != 0 || fd.Path != e.Path {
			return verifiedExistingFile{}, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
	}
	after, err := existingMemberInfo(local, 0)
	if err != nil {
		return verifiedExistingFile{}, err
	}
	if !sameLocalFile(original, after) {
		return verifiedExistingFile{}, errLocalFileChanged
	}
	digest := sha256.Sum256(nil)
	return verifiedExistingFile{local, 0, hex.EncodeToString(digest[:])}, nil
}

// Verify pathname, open handle, link count and the recorded identity before changing a staging file.
func openOwnedReuseFile(path, expected string) (*os.File, error) {
	f, err := os.OpenFile(path, os.O_RDWR, 0)
	if err != nil {
		return nil, err
	}
	file, err := f.Stat()
	name, statErr := os.Lstat(path)
	st, identityErr := reuseStat(path)
	if err != nil || statErr != nil || identityErr != nil || st.ID != expected || st.Links != 1 || !sameLocalFile(file, name) {
		f.Close()
		return nil, reuseConflict()
	}
	return f, nil
}
func truncateOwnedReuseFile(path, id string) error {
	f, err := openOwnedReuseFile(path, id)
	if err != nil {
		return err
	}
	err = f.Truncate(0)
	closed := f.Close()
	if err != nil {
		return err
	}
	return closed
}
func (s *reuseSession) prepare(ctx context.Context, config string, r, source request, files []*baidupcs.FileDirectory, meta reuseMetadata, progress verificationProgress) error {
	if s.receipt.Prepared {
		return nil
	}
	inventory, candidates, err := inspectReuseSource(ctx, config, r.ExpectedUID, source)
	if err != nil {
		return err
	}
	if inventory.Fingerprint != r.ReuseSource.InventoryFingerprint {
		return fail("BAIDU_REUSE_SOURCE_CHANGED")
	}
	for _, fd := range files {
		if err := ctx.Err(); err != nil {
			return err
		}
		if fd.Isdir {
			continue
		}
		id := strconv.FormatInt(fd.FsID, 10)
		item := s.receipt.Files[id]
		if item != nil && item.Prepared {
			continue
		}
		dst, err := s.ensureWorking(id, false)
		if err != nil {
			return err
		}
		item = s.receipt.Files[id]
		if member, ok := candidates[fd.FsID]; ok {
			stamp, err := reuseStat(member.Path)
			if err != nil || stamp != member.Stamp {
				return fail("BAIDU_REUSE_SOURCE_CHANGED")
			}
			f, err := openOwnedReuseFile(dst, item.DataID)
			if err != nil {
				return err
			}
			if err = f.Truncate(0); err != nil {
				f.Close()
				return err
			}
			activity := progress
			activity.Digest = f
			proof, verifyErr := verifyReuseMember(ctx, member.Path, member.Entry, metadataFor(fd, meta), activity)
			if verifyErr == nil {
				verifyErr = f.Sync()
			}
			closeErr := f.Close()
			if closeErr != nil {
				return closeErr
			}
			stamp, err = reuseStat(member.Path)
			if err != nil || stamp != member.Stamp {
				return fail("BAIDU_REUSE_SOURCE_CHANGED")
			}
			if verifyErr == nil {
				item.SHA = proof.Sha256
			} else {
				var mismatch *existingChecksumMismatch
				if !errors.As(verifyErr, &mismatch) && verifyErr.Error() != "BAIDU_EXISTING_CHECKSUM_UNAVAILABLE" {
					return verifyErr
				}
				// A failed candidate never contributes checkpoint bytes on the new disk.
				current, err := reuseStat(dst)
				if err != nil || current.ID != item.DataID || current.Links != 1 {
					return reuseConflict()
				}
				if err = truncateOwnedReuseFile(dst, item.DataID); err != nil {
					return err
				}
			}
		}
		item.Prepared = true
		if err = s.save(); err != nil {
			return err
		}
	}
	after, _, err := inspectReuseSource(ctx, config, r.ExpectedUID, source)
	if err != nil {
		return err
	}
	if after.Fingerprint != inventory.Fingerprint {
		return fail("BAIDU_REUSE_SOURCE_CHANGED")
	}
	s.receipt.Prepared = true
	return s.save()
}
func (s *reuseSession) verifiedTargets(ctx context.Context, r request, files []*baidupcs.FileDirectory, paths map[string]string, metadata reuseMetadata, progress verificationProgress) ([]string, error) {
	ids := []string{}
	for _, fd := range files {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		if fd.Isdir {
			continue
		}
		id := strconv.FormatInt(fd.FsID, 10)
		member := s.receipt.Files[id]
		if member == nil {
			continue
		}
		p := s.working(id)
		if member.Published {
			p = filepath.Join(s.root, filepath.FromSlash(paths[fd.Path]))
		}
		st, err := reuseStat(p)
		if os.IsNotExist(err) && member.SHA == "" {
			continue
		}
		if err != nil || st.ID != member.DataID {
			return nil, reuseConflict()
		}
		if st.Size != fd.Size {
			if member.SHA != "" {
				return nil, reuseConflict()
			}
			continue
		}
		verified, err := verifyReuseMember(ctx, p, entry{fd.FsID, fd.Path, fd.Size, false}, metadataFor(fd, metadata), progress)
		if err != nil {
			var mismatch *existingChecksumMismatch
			if member.SHA == "" && (errors.As(err, &mismatch) || err.Error() == "BAIDU_EXISTING_CHECKSUM_UNAVAILABLE") {
				continue
			}
			return nil, err
		}
		if member.SHA != "" && member.SHA != verified.Sha256 {
			return nil, reuseConflict()
		}
		ids = append(ids, id)
	}
	return ids, nil
}
func downloadWithReuse(ctx context.Context, config string, r request, entries []entry, files []*baidupcs.FileDirectory, meta reuseMetadata, execute reuseExecute, report func(int64, int64), progress verificationProgress) ([]string, error) {
	source, intent, err := validateReuse(r, entries)
	if err != nil {
		return nil, err
	}
	// Once preparation has committed, only the verified new-volume files and the pinned
	// original intent are required. An offline source mount cannot prevent a normal retry.
	previous, readErr := readReuseReceipt(filepath.Join(config, "operations", reuseHash(r.OperationID)+".reuse-v1.json"))
	if readErr != nil && !os.IsNotExist(readErr) {
		return nil, readErr
	}
	if readErr == nil && previous.Intent != intent {
		return nil, reuseConflict()
	}
	if !previous.Prepared {
		if r.ReuseSource.DisableSourceRead {
			return nil, fail("BAIDU_REUSE_SOURCE_UNAVAILABLE")
		}
		if err = reuseRoot(source.OutputDirectory); err != nil {
			return nil, err
		}
		if err = reuseDirsDisjoint(source.OutputDirectory, r.OutputDirectory); err != nil {
			return nil, err
		}
	}
	paths, err := resolveDownloadPaths(ctx, config, r.ExpectedUID, r.OutputDirectory, r, entries, true)
	if err != nil {
		return nil, err
	}
	s, err := openReuseSession(config, r, intent, true)
	if err != nil {
		return nil, err
	}
	defer s.close()
	if err = s.validateTarget(ctx, r, paths); err != nil {
		return nil, err
	}
	if err = s.prepare(ctx, config, r, source, files, meta, progress); err != nil {
		return nil, err
	}
	readOnly := map[string]bool{}
	for _, id := range r.ReadOnlyFileIDs {
		if id == "" || readOnly[id] {
			return nil, fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
		}
		readOnly[id] = true
	}
	var operating []*baidupcs.FileDirectory
	stagePaths := map[string]string{}
	protected := []string{}
	var preparedBytes int64
	for _, fd := range files {
		if fd.Isdir {
			continue
		}
		id := strconv.FormatInt(fd.FsID, 10)
		item := s.receipt.Files[id]
		if item == nil || !item.Prepared {
			return nil, reuseConflict()
		}
		if !item.Published && fd.Size != 0 {
			operating = append(operating, fd)
			stagePaths[fd.Path] = "f-" + id
			if item.SHA != "" || readOnly[id] {
				protected = append(protected, id)
				preparedBytes += fd.Size
			}
		}
		delete(readOnly, id)
	}
	if len(readOnly) != 0 {
		return nil, fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	if len(operating) > 0 {
		_, err = downloadManifestFilesWithReadOnly(s.stage, r.RemotePath, operating, stagePaths, protected, meta,
			func(fd *baidupcs.FileDirectory, local string, completed func(int64)) error {
				id := strconv.FormatInt(fd.FsID, 10)
				work, err := s.ensureWorking(id, false)
				if err != nil {
					return err
				}
				if work != local {
					return reuseConflict()
				}
				checkpoint, err := s.ensureWorking(id, true)
				if err != nil {
					return err
				}
				bytes, valid := checkpointDownloadedBytes(work, fd.Size)
				if !valid || bytes == fd.Size {
					// Never feed an unverified or already-exhausted range map into the upstream writer.
					item := s.receipt.Files[id]
					if err = truncateOwnedReuseFile(checkpoint, item.SideID); err != nil {
						return err
					}
					if err = truncateOwnedReuseFile(work, item.DataID); err != nil {
						return err
					}
				}
				return execute(fd, local, completed)
			}, func(done, total int64) {
				if total > preparedBytes {
					report(max(0, done-preparedBytes), total-preparedBytes)
				}
			}, progress)
		if err != nil {
			return nil, err
		}
	}
	// No final pathname is ever opened for write. A hard link on the destination volume
	// publishes the verified inode without replacement; an interrupted link can be recognized on retry.
	results := []string{}
	for _, fd := range files {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		final, err := safeLocal(s.root, paths[fd.Path])
		if err != nil {
			return nil, err
		}
		if fd.Isdir {
			if err = os.MkdirAll(final, 0700); err != nil {
				return nil, err
			}
			continue
		}
		id := strconv.FormatInt(fd.FsID, 10)
		item := s.receipt.Files[id]
		work := s.working(id)
		verifyPath := work
		if item.Published {
			verifyPath = final
		}
		st, err := reuseStat(verifyPath)
		if err != nil || st.ID != item.DataID {
			return nil, reuseConflict()
		}
		proof, err := verifyReuseMember(ctx, verifyPath, entry{fd.FsID, fd.Path, fd.Size, false}, metadataFor(fd, meta), progress)
		if err != nil {
			return nil, err
		}
		if item.SHA != "" && item.SHA != proof.Sha256 {
			return nil, reuseConflict()
		}
		if !item.Published {
			item.SHA = proof.Sha256
			if err = s.save(); err != nil {
				return nil, err
			}
			if err = os.MkdirAll(filepath.Dir(final), 0700); err != nil {
				return nil, err
			}
			if _, err = safeLocal(s.root, paths[fd.Path]); err != nil {
				return nil, err
			}
			if err = os.Link(work, final); err != nil {
				if !os.IsExist(err) {
					return nil, storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "publishVerifiedFile", err)
				}
				current, _, _, err := reuseIdentity(final)
				if err != nil || current != item.DataID {
					return nil, reuseConflict()
				}
			}
			if err = syncReuseDir(filepath.Dir(final)); err != nil {
				return nil, err
			}
			item.Published = true
			if err = s.save(); err != nil {
				return nil, err
			}
		}
		results = append(results, final)
	}
	s.receipt.Published = true
	if err = s.save(); err != nil {
		return nil, err
	}
	if err = s.validateTarget(ctx, r, paths); err != nil {
		return nil, err
	}
	if err = s.reclaimSpace(paths, entries); err != nil {
		return nil, err
	}
	return results, nil
}
func inspectReuseDestination(ctx context.Context, r request, pcs *baidupcs.BaiduPCS) ([]string, error) {
	_, intent, err := validateReuse(r, r.ExpectedManifest)
	if err != nil {
		return nil, err
	}
	config := os.Getenv(pcsconfig.EnvConfigDir)
	var files []*baidupcs.FileDirectory
	if r.VerifyRemote || r.Command == "inspect-retained-writes" {
		actual, metas, err := manifestLimited(pcs, r.RemotePath, 10000)
		if err != nil {
			return nil, err
		}
		if !sameManifest(actual, r.ExpectedManifest) {
			return nil, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
		files = metas
	}
	s, err := openReuseSession(config, r, intent, false)
	if err != nil {
		return nil, err
	}
	if r.ReuseSource.DisableSourceRead && (s == nil || !s.receipt.Prepared) {
		return nil, fail("BAIDU_REUSE_SOURCE_UNAVAILABLE")
	}
	if s == nil {
		return []string{}, nil
	}
	paths, err := resolveDownloadPaths(ctx, config, r.ExpectedUID, r.OutputDirectory, r, r.ExpectedManifest, false)
	if err != nil {
		return nil, err
	}
	if err = s.validateTarget(ctx, r, paths); err != nil {
		return nil, err
	}
	if r.Command == "verify-existing" && !s.receipt.Published {
		return nil, fail("BAIDU_DOWNLOAD_INCOMPLETE")
	}
	if r.Command != "inspect-retained-writes" {
		return nil, nil
	}
	return s.verifiedTargets(ctx, r, files, paths, func(fd *baidupcs.FileDirectory) (*baidupcs.FileDirectory, error) {
		return pcs.FilesDirectoriesMeta(fd.Path)
	}, bridgeVerificationProgress(ctx))
}
