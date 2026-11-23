package main

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"slices"
	"strconv"
	"strings"
	"testing"

	"github.com/qjfoidnh/BaiduPCS-Go/baidupcs"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsfunctions/pcsdownload"
	"github.com/qjfoidnh/BaiduPCS-Go/requester/transfer"
)

type reuseCase struct {
	t                      *testing.T
	config, source, target string
	expected               []entry
	files                  []*baidupcs.FileDirectory
	contents               []string
	sourceRequest, r       request
	attempts               []int64
}

func newReuseCase(t *testing.T) *reuseCase {
	t.Helper()
	c := &reuseCase{t: t, config: t.TempDir(), source: t.TempDir(), target: t.TempDir(), contents: []string{"valid data", "right data", "partial original", "remote only"}}
	for i, n := range []string{"a", "b", "c", "d"} {
		fd := resumeFixture(n, c.contents[i])
		fd.FsID = int64(i + 1)
		c.files = append(c.files, fd)
		c.expected = append(c.expected, entry{fd.FsID, fd.Path, fd.Size, false})
	}
	if err := os.Mkdir(filepath.Join(c.source, "remote"), 0700); err != nil {
		t.Fatal(err)
	}
	c.write(c.src(0), c.contents[0])
	c.write(c.src(1), "wrong data")
	c.write(c.src(2), "part")
	c.write(c.src(2)+pcsdownload.DownloadSuffix, "not a usable range record")
	c.sourceRequest = request{Command: "inspect-reuse-source", ExpectedUID: "123", OperationID: "old-layout", RemotePath: "/remote", ExpectedManifest: c.expected, OutputDirectory: c.source}
	inv, m, err := inspectReuseSource(context.Background(), c.config, "123", c.sourceRequest)
	if err != nil || len(m) != 2 || inv.CandidateFileCount != 2 || inv.CandidateBytes != 20 || inv.FileCount != 4 {
		t.Fatal(inv, err)
	}
	c.r = c.sourceRequest
	c.r.Command = "download"
	c.r.OperationID = "fresh-layout"
	c.r.OutputDirectory = c.target
	c.r.ReuseSource = &reuseSource{OperationID: c.sourceRequest.OperationID, OutputDirectory: c.source, InventoryFingerprint: inv.Fingerprint}
	return c
}
func (c *reuseCase) src(i int) string { return filepath.Join(c.source, "remote", string(rune('a'+i))) }
func (c *reuseCase) dst(i int) string { return filepath.Join(c.target, "remote", string(rune('a'+i))) }
func (c *reuseCase) write(p, data string) {
	c.t.Helper()
	if err := os.WriteFile(p, []byte(data), 0600); err != nil {
		c.t.Fatal(err)
	}
}
func (c *reuseCase) meta(fd *baidupcs.FileDirectory) (*baidupcs.FileDirectory, error) { return fd, nil }
func (c *reuseCase) execute(fd *baidupcs.FileDirectory, local string, checkpoint func(int64)) error {
	c.attempts = append(c.attempts, fd.FsID)
	c.write(local, c.contents[int(fd.FsID)-1])
	checkpoint(fd.Size)
	return nil
}
func (c *reuseCase) run(ctx context.Context, metadata reuseMetadata, execute reuseExecute) ([]string, error) {
	c.t.Helper()
	return downloadWithReuse(ctx, c.config, c.r, c.expected, c.files, metadata, execute, func(done, total int64) {
		if total < 0 || done < 0 || done > total {
			c.t.Fatal("bad network progress")
		}
	}, verificationProgress{Context: ctx})
}
func (c *reuseCase) receipt() (string, reuseReceipt) {
	p := filepath.Join(c.config, "operations", reuseHash(c.r.OperationID)+".reuse-v1.json")
	r, e := readReuseReceipt(p)
	if e != nil {
		c.t.Fatal(e)
	}
	return p, r
}
func TestReusePreviewsAreReadOnlyAndRejectForeignAndAliasedMembers(t *testing.T) {
	for _, kind := range []string{"foreign", "unknown directory", "symlink", "hardlink", "changed member", "same length replacement", "changed sidecar", "unstable remote"} {
		t.Run(kind, func(t *testing.T) {
			c := newReuseCase(t)
			before, _ := os.ReadDir(c.config)
			if len(before) != 0 {
				t.Fatal("preview wrote mapping")
			}
			switch kind {
			case "foreign":
				c.write(filepath.Join(c.source, "extra"), "foreign")
			case "unknown directory":
				if err := os.Mkdir(filepath.Join(c.source, "unknown"), 0700); err != nil {
					t.Fatal(err)
				}
			case "symlink":
				if err := os.Symlink(c.src(0), c.src(3)); err != nil {
					t.Skip(err)
				}
			case "hardlink":
				if err := os.Link(c.src(0), c.src(3)); err != nil {
					t.Skip(err)
				}
			case "changed member":
				c.write(c.src(2), "longer")
			case "same length replacement":
				tmp := filepath.Join(t.TempDir(), "original")
				if err := os.Rename(c.src(0), tmp); err != nil {
					t.Fatal(err)
				}
				c.write(c.src(0), c.contents[0])
			case "changed sidecar":
				c.write(c.src(2)+pcsdownload.DownloadSuffix, "new invalid checkpoint")
			case "unstable remote":
				c.r.ExpectedManifest = append([]entry(nil), c.expected...)
				c.r.ExpectedManifest[0].ID = 999
			}
			out, err := c.run(context.Background(), c.meta, c.execute)
			if err == nil || len(out) > 0 || len(c.attempts) > 0 {
				t.Fatalf("source admission %s %v", kind, err)
			}
		})
	}
}
func TestReuseCopiesOnlyAuthenticatedMembersAndPublishesWithoutReplacing(t *testing.T) {
	c := newReuseCase(t)
	before, _, err := inspectReuseSource(context.Background(), c.config, "123", c.sourceRequest)
	if err != nil {
		t.Fatal(err)
	}
	out, err := c.run(context.Background(), c.meta, c.execute)
	if err != nil {
		t.Fatal(err)
	}
	if !slices.Equal(c.attempts, []int64{2, 3, 4}) || len(out) != 4 {
		t.Fatal("unexpected transfers", c.attempts)
	}
	for i, p := range out {
		if p != c.dst(i) {
			t.Fatal("wrong output")
		}
		data, err := os.ReadFile(p)
		if err != nil || string(data) != c.contents[i] {
			t.Fatal("bad result", err)
		}
	}
	after, _, err := inspectReuseSource(context.Background(), c.config, "123", c.sourceRequest)
	if err != nil || before != after {
		t.Fatal("modified source", err)
	}
	children, _ := os.ReadDir(c.target)
	if len(children) != 1 || children[0].Name() != "remote" {
		t.Fatal("private payload left", children)
	}
	orig, _ := os.Stat(c.src(0))
	copy, _ := os.Stat(c.dst(0))
	if os.SameFile(orig, copy) {
		t.Fatal("published original inode")
	}
	if _, err := c.run(context.Background(), c.meta, c.execute); err != nil || len(c.attempts) != 3 {
		t.Fatal("lost completed receipt", err)
	}
}
func TestReuseTransferCheckpointContinuesWithOldVolumeUnavailable(t *testing.T) {
	c := newReuseCase(t)
	ctx, cancel := context.WithCancel(context.Background())
	_, err := c.run(ctx, c.meta, func(fd *baidupcs.FileDirectory, p string, report func(int64)) error {
		if fd.FsID != 2 {
			t.Fatal("credited transfer")
		}
		c.write(p, "righ")
		writeCheckpoint(t, p, &transfer.DownloadInstanceInfoExport{TotalSize: fd.Size, GenBegin: 4, RangeGenMode: transfer.RangeGenMode_BlockSize, BlockSize: 4})
		cancel()
		return ctx.Err()
	})
	if !errors.Is(err, context.Canceled) {
		t.Fatal(err)
	}
	_, rec := c.receipt()
	if !rec.Prepared || rec.Published {
		t.Fatal("missing preparation checkpoint")
	}
	if err := os.Rename(c.source, c.source+"-unmounted"); err != nil {
		t.Fatal(err)
	}
	c.r.ReadOnlyFileIDs = []string{"1"}
	c.r.ReuseSource.DisableSourceRead = true
	out, err := c.run(context.Background(), c.meta, func(fd *baidupcs.FileDirectory, p string, report func(int64)) error {
		if fd.FsID == 2 {
			if count, valid := checkpointDownloadedBytes(p, fd.Size); !valid || count != 4 {
				t.Fatal("lost resumable range")
			}
		}
		return c.execute(fd, p, report)
	})
	if err != nil || len(out) != 4 || !slices.Equal(c.attempts, []int64{2, 3, 4}) {
		t.Fatal("resuming", err, c.attempts)
	}
}
func TestReuseCancellationBeforePreparationDoesNotRedownloadOrCreditChangedInputs(t *testing.T) {
	c := newReuseCase(t)
	calls := 0
	ctx, cancel := context.WithCancel(context.Background())
	_, err := c.run(ctx, func(fd *baidupcs.FileDirectory) (*baidupcs.FileDirectory, error) {
		calls++
		if calls == 2 {
			cancel()
		}
		return fd, nil
	}, c.execute)
	if !errors.Is(err, context.Canceled) || len(c.attempts) != 0 {
		t.Fatal("premature transfer", err)
	}
	_, rec := c.receipt()
	if rec.Prepared {
		t.Fatal("incomplete proof recorded")
	}
	c.write(c.src(0), "different!")
	_, err = c.run(context.Background(), c.meta, c.execute)
	if err == nil || err.Error() != "BAIDU_REUSE_SOURCE_CHANGED" || len(c.attempts) != 0 {
		t.Fatal(err)
	}
}
func TestReuseDoesNotTouchForeignDestinationOrNestedSource(t *testing.T) {
	for _, kind := range []string{"preexisting final", "intervening final", "replaced work", "hardlinked work", "nested", "same directory", "forged fingerprint", "concurrent"} {
		t.Run(kind, func(t *testing.T) {
			c := newReuseCase(t)
			ctx := context.Background()
			foreign := ""
			switch kind {
			case "preexisting final":
				os.MkdirAll(filepath.Dir(c.dst(0)), 0700)
				foreign = c.dst(0)
				c.write(foreign, "UNTOUCHED")
			case "nested":
				c.r.OutputDirectory = filepath.Join(c.source, "nested")
			case "same directory":
				c.r.OutputDirectory = c.source
			case "forged fingerprint":
				c.r.ReuseSource.InventoryFingerprint = strings.Repeat("a", 64)
			default:
				_, err := c.run(ctx, c.meta, func(*baidupcs.FileDirectory, string, func(int64)) error { return context.Canceled })
				if !errors.Is(err, context.Canceled) {
					t.Fatal(err)
				}
				_, rec := c.receipt()
				switch kind {
				case "intervening final":
					os.MkdirAll(filepath.Dir(c.dst(0)), 0700)
					foreign = c.dst(0)
					c.write(foreign, "UNTOUCHED")
				case "replaced work":
					p := filepath.Join(c.target, rec.Stage, "f-2")
					if err := os.Rename(p, p+".foreign"); err != nil {
						t.Fatal(err)
					}
					foreign = p
					c.write(p, "UNTOUCHED")
				case "hardlinked work":
					p := filepath.Join(c.target, rec.Stage, "f-2")
					foreign = filepath.Join(t.TempDir(), "alias")
					if err := os.Link(p, foreign); err != nil {
						t.Skip(err)
					}
				case "concurrent":
					_, intent, e := validateReuse(c.r, c.expected)
					if e != nil {
						t.Fatal(e)
					}
					s, e := openReuseSession(c.config, c.r, intent, true)
					if e != nil {
						t.Fatal(e)
					}
					defer s.close()
				}
			}
			before := []byte(nil)
			if foreign != "" {
				before, _ = os.ReadFile(foreign)
			}
			_, err := c.run(ctx, c.meta, c.execute)
			if err == nil || len(c.attempts) != 0 {
				t.Fatal("unsafe admission", err)
			}
			if foreign != "" {
				after, e := os.ReadFile(foreign)
				if e != nil || !slices.Equal(before, after) {
					t.Fatal("changed foreign file", e)
				}
			}
		})
	}
}
func TestReuseCapacityInspectionRevalidatesAndProtectsTheCreditedInode(t *testing.T) {
	c := newReuseCase(t)
	ctx := context.Background()
	_, err := c.run(ctx, c.meta, func(*baidupcs.FileDirectory, string, func(int64)) error { return context.Canceled })
	if !errors.Is(err, context.Canceled) {
		t.Fatal(err)
	}
	_, intent, e := validateReuse(c.r, c.expected)
	if e != nil {
		t.Fatal(e)
	}
	s, e := openReuseSession(c.config, c.r, intent, false)
	if e != nil {
		t.Fatal(e)
	}
	mappings, e := resolveDownloadPaths(ctx, c.config, "123", c.target, c.r, c.expected, false)
	if e != nil {
		t.Fatal(e)
	}
	if e = s.validateTarget(ctx, c.r, mappings); e != nil {
		t.Fatal(e)
	}
	ids, e := s.verifiedTargets(ctx, c.r, c.files, mappings, c.meta, verificationProgress{})
	if e != nil || !reflect.DeepEqual(ids, []string{"1"}) {
		t.Fatal(ids, e)
	}
	c.r.ReadOnlyFileIDs = ids
	c.write(s.working("1"), "bad data!!")
	_, e = c.run(ctx, c.meta, c.execute)
	if e == nil || len(c.attempts) != 0 {
		t.Fatal("protected member got overwritten", e)
	}
}
func TestReuseRejectsChangedOfficialEvidenceDuringCopy(t *testing.T) {
	c := newReuseCase(t)
	calls := map[int64]int{}
	_, err := c.run(context.Background(), func(fd *baidupcs.FileDirectory) (*baidupcs.FileDirectory, error) {
		calls[fd.FsID]++
		if fd.FsID == 1 && calls[fd.FsID] == 2 {
			clone := *fd
			clone.FsID = 111
			return &clone, nil
		}
		return fd, nil
	}, c.execute)
	if err == nil || err.Error() != "BAIDU_REMOTE_MANIFEST_CHANGED" || len(c.attempts) != 0 {
		t.Fatal(err)
	}
	_, rec := c.receipt()
	if rec.Prepared || rec.Files[strconv.Itoa(1)].SHA != "" {
		t.Fatal("unstable remote became accepted")
	}
}

func TestReuseRecoversCrashAfterPublishAndAfterFinalCleanupStarted(t *testing.T) {
	for _, allLinked := range []bool{false, true} {
		t.Run(strconv.FormatBool(allLinked), func(t *testing.T) {
			c := newReuseCase(t)
			ctx := context.Background()
			_, err := c.run(ctx, c.meta, func(*baidupcs.FileDirectory, string, func(int64)) error { return context.Canceled })
			if !errors.Is(err, context.Canceled) {
				t.Fatal(err)
			}
			_, intent, _ := validateReuse(c.r, c.expected)
			s, err := openReuseSession(c.config, c.r, intent, true)
			if err != nil {
				t.Fatal(err)
			}
			os.MkdirAll(filepath.Dir(c.dst(0)), 0700)
			for i, fd := range c.files {
				if !allLinked && i > 0 {
					break
				}
				id := strconv.FormatInt(fd.FsID, 10)
				item := s.receipt.Files[id]
				if allLinked {
					c.write(s.working(id), c.contents[i])
					proof, err := verifyExistingFile(ctx, s.working(id), c.expected[i], func() (*baidupcs.FileDirectory, error) { return fd, nil })
					if err != nil {
						t.Fatal(err)
					}
					item.SHA = proof.Sha256
					item.Published = true
				}
				if err = os.Link(s.working(id), c.dst(i)); err != nil {
					t.Fatal(err)
				}
			}
			if allLinked {
				s.receipt.Published = true
				if err = os.Remove(s.working("1")); err != nil {
					t.Fatal(err)
				}
			}
			if err = s.save(); err != nil {
				t.Fatal(err)
			}
			s.close()
			out, err := c.run(ctx, c.meta, c.execute)
			if err != nil || len(out) != 4 {
				t.Fatal(err)
			}
			if allLinked && len(c.attempts) != 0 {
				t.Fatal("redownloaded a published file")
			}
		})
	}
}
func TestReuseUnusablePrivateCheckpointRestartsOnlyItsOwnedMember(t *testing.T) {
	c := newReuseCase(t)
	ctx := context.Background()
	_, err := c.run(ctx, c.meta, func(fd *baidupcs.FileDirectory, p string, report func(int64)) error {
		c.write(p, "r")
		c.write(p+pcsdownload.DownloadSuffix, "malformed")
		return context.Canceled
	})
	if !errors.Is(err, context.Canceled) {
		t.Fatal(err)
	}
	_, err = c.run(ctx, c.meta, func(fd *baidupcs.FileDirectory, p string, report func(int64)) error {
		if fd.FsID == 2 {
			for _, n := range []string{p, p + pcsdownload.DownloadSuffix} {
				info, e := os.Stat(n)
				if e != nil || info.Size() != 0 {
					t.Fatal("invalid target checkpoint survived", e)
				}
			}
		}
		return c.execute(fd, p, report)
	})
	if err != nil || !slices.Equal(c.attempts, []int64{2, 3, 4}) {
		t.Fatal(err)
	}
}

func TestReuseDeniedSourceReadWithoutCheckpointLeavesBothTreesUntouched(t *testing.T) {
	c := newReuseCase(t)
	c.r.ReuseSource.DisableSourceRead = true
	_, err := c.run(context.Background(), c.meta, c.execute)
	if err == nil || err.Error() != "BAIDU_REUSE_SOURCE_UNAVAILABLE" || len(c.attempts) != 0 {
		t.Fatal(err)
	}
	for _, p := range []string{c.target, c.config} {
		names, e := os.ReadDir(p)
		if e != nil || len(names) != 0 {
			t.Fatal("initialized despite denied source read")
		}
	}
}

func TestReuseEmptyRemoteMemberWithoutBlockListCreatesVerifiedEmptyPayload(t *testing.T) {
	c := newReuseCase(t)
	fd := resumeFixture("empty", "")
	fd.FsID = 99
	fd.BlockList = nil
	c.files = append(c.files, fd)
	c.expected = append(c.expected, entry{fd.FsID, fd.Path, 0, false})
	c.sourceRequest.ExpectedManifest = c.expected
	c.r.ExpectedManifest = c.expected
	inv, _, err := inspectReuseSource(context.Background(), c.config, "123", c.sourceRequest)
	if err != nil {
		t.Fatal(err)
	}
	c.r.ReuseSource.InventoryFingerprint = inv.Fingerprint
	out, err := c.run(context.Background(), c.meta, c.execute)
	if err != nil || len(out) != 5 {
		t.Fatal(err)
	}
	p := out[4]
	st, err := os.Stat(p)
	if err != nil || !st.Mode().IsRegular() || st.Size() != 0 {
		t.Fatal("empty payload was not published", err)
	}
	if slices.Contains(c.attempts, int64(99)) {
		t.Fatal("transferred empty payload")
	}
}
