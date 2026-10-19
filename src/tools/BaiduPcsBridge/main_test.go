package main

import (
	"encoding/json"
	"errors"
	"github.com/qjfoidnh/BaiduPCS-Go/baidupcs"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestSafeLocalRejectsTraversalAndLinks(t *testing.T) {
	root := t.TempDir()
	outside := t.TempDir()
	for _, p := range []string{"../escape", "a/../../escape", "C:/escape", `a\..\escape`} {
		if _, err := safeLocal(root, p); err == nil {
			t.Fatalf("accepted unsafe path %q", p)
		}
	}
	if err := os.Symlink(outside, filepath.Join(root, "link")); err == nil {
		if _, err := safeLocal(root, "link/escape"); err == nil {
			t.Fatal("followed symlink outside output root")
		}
	}
	want := filepath.Join(root, "游戏", "archive.part1.rar")
	if got, err := safeLocal(root, "游戏/archive.part1.rar"); err != nil || got != want {
		t.Fatalf("normal nested path: got %q, %v", got, err)
	}
}

func TestReceiptReplacementRetainsStageAndManifest(t *testing.T) {
	file := filepath.Join(t.TempDir(), "operation.json")
	state := &receipt{Fingerprint: "request-v1", Stage: "transferring", RemotePath: "/downloads/operation-1"}
	if err := saveReceipt(file, state); err != nil {
		t.Fatal(err)
	}
	state.Stage = "completed"
	state.Manifest = []entry{{ID: 123, Path: "/downloads/operation-1/a,b.rar", Size: 42}}
	if err := saveReceipt(file, state); err != nil {
		t.Fatal(err)
	}
	b, err := os.ReadFile(file)
	if err != nil {
		t.Fatal(err)
	}
	var got receipt
	if err := json.Unmarshal(b, &got); err != nil {
		t.Fatal(err)
	}
	if got.Stage != "completed" || len(got.Manifest) != 1 || got.Manifest[0].Path != state.Manifest[0].Path {
		t.Fatalf("receipt changed: %#v", got)
	}
	if info, err := os.Stat(file); err != nil || info.Mode().Perm()&0077 != 0 {
		t.Fatal("receipt is not private")
	}
}

func TestTransferRejectsInvalidSourcesBeforeAnyRemoteEffect(t *testing.T) {
	root := t.TempDir()
	t.Setenv("BAIDUPCS_GO_CONFIG_DIR", root)
	for _, test := range []struct{ url, code, want string }{
		{"https://example.org/s/1abcdef", "abcd", "BAIDU_SHARE_URL_INVALID"},
		{"https://pan.baidu.com/", "abcd", "BAIDU_SHARE_URL_INVALID"},
		{"https://pan.baidu.com/s/1abcdef", "", "BAIDU_ACCESS_CODE_REQUIRED"},
		{"https://pan.baidu.com/s/1abcdef", "too-long", "BAIDU_ACCESS_CODE_REQUIRED"},
	} {
		err := transferShare(request{OperationID: "test-id", URL: test.url, AccessCode: test.code, RemoteDirectory: "/downloads"}, nil)
		if err == nil || err.Error() != test.want {
			t.Fatalf("%s: got %v, want %s", test.url, err, test.want)
		}
	}
	files, err := os.ReadDir(filepath.Join(root, "operations"))
	if err != nil {
		t.Fatal(err)
	}
	if len(files) != 0 {
		t.Fatal("recorded transfer intent before source validation")
	}
}

func TestDirectoryPaginationRejectsRepeatsMissingPagesAndLimit(t *testing.T) {
	full := func(page int) ([]*baidupcs.FileDirectory, error) {
		return []*baidupcs.FileDirectory{{FsID: int64(page * 2)}, {FsID: int64(page*2 + 1)}}, nil
	}
	if _, err := completePages(full, 2, 2); err == nil || err.Error() != "BAIDU_DIRECTORY_PAGE_LIMIT" {
		t.Fatalf("page cap accepted partial directory: %v", err)
	}
	repeat := func(int) ([]*baidupcs.FileDirectory, error) {
		return []*baidupcs.FileDirectory{{FsID: 1}, {FsID: 2}}, nil
	}
	if _, err := completePages(repeat, 2, 3); err == nil || err.Error() != "BAIDU_DIRECTORY_PAGINATION_INVALID" {
		t.Fatalf("repeated IDs accepted: %v", err)
	}
	missing := func(page int) ([]*baidupcs.FileDirectory, error) {
		if page == 2 {
			return nil, errors.New("page unavailable")
		}
		return full(page)
	}
	if _, err := completePages(missing, 2, 3); err == nil || err.Error() != "page unavailable" {
		t.Fatalf("missing page accepted: %v", err)
	}
	complete := func(page int) ([]*baidupcs.FileDirectory, error) {
		if page == 3 {
			return []*baidupcs.FileDirectory{{FsID: 100}}, nil
		}
		return full(page)
	}
	if got, err := completePages(complete, 2, 3); err != nil || len(got) != 5 {
		t.Fatalf("complete directory rejected: %d items, %v", len(got), err)
	}
}

func TestDirectoryPageSchemaPreservesIntegerIdentityAndRejectsMalformedResponse(t *testing.T) {
	page := `{"errno":0,"list":[{"fs_id":9007199254740999,"path":"/root/dir","server_filename":"dir","isdir":1,"size":0},{"fs_id":7,"path":"/root/a,b.rar","server_filename":"a,b.rar","isdir":0,"size":42,"block_list":["original-md5"],"md5":"encoded-md5"}]}`
	got, err := decodeDirectoryPage(strings.NewReader(page), "/root")
	if err != nil || len(got) != 2 || got[0].FsID != 9007199254740999 || !got[0].Isdir || got[1].Filename != "a,b.rar" || got[1].MD5 != "original-md5" {
		t.Fatalf("metadata was lost: %#v %v", got, err)
	}
	for _, broken := range []string{`{}`, `{"errno":0}`, `{"errno":-9,"list":[]}`, `{"errno":0,"list":null}`, `{"errno":0,"list":[{"fs_id":1,"path":"/outside/file","server_filename":"file"}]}`} {
		if _, err := decodeDirectoryPage(strings.NewReader(broken), "/root"); err == nil {
			t.Fatalf("accepted incomplete or unsafe page %s", broken)
		}
	}
}
