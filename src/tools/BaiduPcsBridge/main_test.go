package main

import (
	"bytes"
	"context"
	"crypto/md5"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"os"
	"path"
	"path/filepath"
	"slices"
	"strings"
	"syscall"
	"testing"
	"time"
	"unicode/utf8"

	"github.com/golang/protobuf/proto"
	"github.com/qjfoidnh/BaiduPCS-Go/baidupcs"
	"github.com/qjfoidnh/BaiduPCS-Go/baidupcs/pcserror"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsconfig"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsfunctions/pcsdownload"
	"github.com/qjfoidnh/BaiduPCS-Go/requester"
	"github.com/qjfoidnh/BaiduPCS-Go/requester/transfer"
)

type fixtureTransport func(*http.Request) (*http.Response, error)

func TestTransferRecoveryRequiresExplicitMissingMetadata(t *testing.T) {
	metadata := pcserror.NewPCSErrorInfo(baidupcs.OperationFilesDirectoriesMeta)
	metadata.ErrCode = 31066
	metadata.SetRemoteError()
	if !isMissingTransferTarget(metadata) {
		t.Fatal("explicit missing metadata was not recognized")
	}
	metadata.SetNetError(errors.New("connection reset"))
	if isMissingTransferTarget(metadata) {
		t.Fatal("network failure was treated as missing metadata")
	}
	metadata.SetRemoteError()
	metadata.ErrCode = 31045
	if isMissingTransferTarget(metadata) {
		t.Fatal("account failure was treated as a missing directory")
	}
	metadata.ErrCode = 31066
	metadata.Operation = "list"
	if isMissingTransferTarget(metadata) {
		t.Fatal("unrelated operation error was treated as missing metadata")
	}
}

func (f fixtureTransport) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }

func fixtureClient(f fixtureTransport) *requester.HTTPClient {
	client := requester.NewHTTPClient()
	client.SetKeepAlive(true) // Initialize upstream before replacing its transport; never contact a real host.
	client.Transport = f
	return client
}

func fixtureResponse(r *http.Request, finalURL, body string) *http.Response {
	if finalURL != "" {
		copyRequest := r.Clone(r.Context())
		copyRequest.URL, _ = url.Parse(finalURL)
		r = copyRequest
	}
	return &http.Response{StatusCode: http.StatusOK, Header: make(http.Header),
		Request: r, Body: io.NopCloser(strings.NewReader(body))}
}

const shareFixtureLocals = `{"loginstate":1,"bdstoken":"fixture-token","uk":123,"share_uk":"456","shareid":789}`

func TestSharePageNormalFormsKeepAccountCookiesAndHeaders(t *testing.T) {
	for _, html := range []string{
		`<script>locals.mset(` + shareFixtureLocals + `);</script>`,
		`<script>window.locals=` + shareFixtureLocals + `;</script>`,
	} {
		calls := 0
		client := fixtureClient(func(r *http.Request) (*http.Response, error) {
			calls++
			if r.Method != http.MethodGet || r.URL.String() != "https://pan.baidu.com/s/1fixture" {
				t.Fatal("unexpected share request")
			}
			wantReferer := "https://pan.baidu.com/disk/home"
			if calls == 2 {
				wantReferer = "https://pan.baidu.com/share/init?surl=fixture"
			}
			if r.Header.Get("Referer") != wantReferer || r.Header.Get("User-Agent") != baidupcs.NetdiskUA {
				t.Fatal("upstream request headers changed")
			}
			if c, err := r.Cookie("BDUSS"); err != nil || c.Value != "fixture-session" {
				t.Fatal("account cookie jar was not reused")
			}
			return fixtureResponse(r, "", html), nil
		})
		origin, _ := url.Parse("https://pan.baidu.com")
		client.Jar.SetCookies(origin, []*http.Cookie{{Name: "BDUSS", Value: "fixture-session"}})
		for _, first := range []bool{true, false} {
			tokens, err := accessSharePage(client, "1fixture", first)
			if err != nil || tokens["shareid"] != "789" || tokens["share_uk"] != "456" || tokens["bdstoken"] != "fixture-token" {
				t.Fatalf("normal share was rejected: %v", err)
			}
		}
		if calls != 2 {
			t.Fatalf("extra page request: %d", calls)
		}
	}
}

func TestTransferRejectsModernErrorOrIncompletePageBeforeVerificationAndMutation(t *testing.T) {
	for _, tc := range []struct{ name, final, body, code string }{
		{"cancelled", "https://pan.baidu.com/wap/error?errortype=1&surl=fixture", `<title>百度网盘分享</title><script>locals.mset({"loginstate":1,"bdstoken":"fixture-token","uk":123});</script><script src="https://nd-static.bdstatic.com/m-static/m-share/js/error-new_dbeba1d.js"></script>`, "BAIDU_SHARE_CANCELLED"},
		{"expired", "https://pan.baidu.com/share/error?errortype=10", shareFixtureLocals, "BAIDU_SHARE_EXPIRED"},
		{"unknown error", "https://pan.baidu.com/wap/error?errortype=999", shareFixtureLocals, "BAIDU_SHARE_UNAVAILABLE"},
		{"deleted", "https://pan.baidu.com/wap/error?errortype=0", shareFixtureLocals, "BAIDU_SHARE_DELETED"},
		{"blocked desktop share", "", `<div class="platform-non-found"></div><script>locals.mset({"share_page_type":"error","errortype":3,"errno":115});</script>`, "BAIDU_SHARE_BLOCKED"},
		{"foreign error", "https://example.org/wap/error?errortype=1", shareFixtureLocals, "BAIDU_SHARE_PAGE_INVALID"},
		{"incomplete locals", "", `window.locals={"loginstate":1,"bdstoken":"fixture-token","uk":123};`, "BAIDU_SHARE_PAGE_INVALID"},
		{"invalid identity", "", `window.locals={"loginstate":1,"bdstoken":"fixture-token","shareid":"wrong","share_uk":"456"};`, "BAIDU_SHARE_PAGE_INVALID"},
		{"legacy unavailable", "", `<div class="platform-non-found"></div>`, "BAIDU_SHARE_UNAVAILABLE"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			root := t.TempDir()
			t.Setenv("BAIDUPCS_GO_CONFIG_DIR", root)
			remoteMutationStarted = false
			calls := 0
			client := fixtureClient(func(r *http.Request) (*http.Response, error) {
				calls++
				if r.Method != http.MethodGet || r.URL.Path != "/s/1fixture" {
					t.Fatal("verification/list/mkdir request followed unavailable share")
				}
				return fixtureResponse(r, tc.final, tc.body), nil
			})
			err := transferShare(request{OperationID: "fixture-operation", URL: "https://pan.baidu.com/s/1fixture", AccessCode: "abcd", RemoteDirectory: "/downloads"}, baidupcs.NewPCSWithClient(250528, client))
			if err == nil || err.Error() != tc.code {
				t.Fatalf("got %v, expected %s", err, tc.code)
			}
			if calls != 1 || remoteMutationStarted {
				t.Fatal("unavailable share produced more requests or remote effects")
			}
			files, err := os.ReadDir(filepath.Join(root, "operations"))
			if err != nil || len(files) != 0 {
				t.Fatal("recorded transfer intent for unavailable share")
			}
		})
	}
}

func TestOfficialStructuredShareErrorsStopBeforeAnyFurtherRequest(t *testing.T) {
	for _, tc := range []struct{ errorType, code string }{
		{"0", "BAIDU_SHARE_DELETED"},
		{"1", "BAIDU_SHARE_CANCELLED"},
		{"2", "BAIDU_SHARE_TEMPORARILY_UNAVAILABLE"},
		{"3", "BAIDU_SHARE_BLOCKED"},
		{"5", "BAIDU_SHARE_NOT_FOUND"},
		{"10", "BAIDU_SHARE_EXPIRED"},
		{"11", "BAIDU_SHARE_VISIT_LIMIT_INVALIDATED"},
		{"12", "BAIDU_SHARE_TEMPORARILY_UNAVAILABLE"},
		{"15", "BAIDU_SHARE_TEMPORARILY_UNAVAILABLE"},
		{"17", "BAIDU_SHARE_ACCESS_RESTRICTED"},
		{"123", "BAIDU_SHARE_VISITOR_LIMIT_REACHED"},
		{"124", "BAIDU_SHARE_FROZEN"},
		{"999", "BAIDU_SHARE_UNAVAILABLE"},
	} {
		for _, variant := range []string{"redirect", "numeric locals", "string locals", "later locals"} {
			t.Run(tc.errorType+"/"+variant, func(t *testing.T) {
				calls := 0
				remoteMutationStarted = false
				client := fixtureClient(func(r *http.Request) (*http.Response, error) {
					calls++
					if calls != 1 || r.Method != http.MethodGet || r.URL.Path != "/s/1fixture" {
						t.Fatal("share error caused verification, listing or transfer")
					}
					final := ""
					value := tc.errorType
					prefix := `<div class="platform-non-found"></div><script>`
					switch variant {
					case "redirect":
						final = "https://pan.baidu.com/share/error?errortype=" + tc.errorType
					case "string locals":
						value = `"` + tc.errorType + `"`
					case "later locals":
						prefix += `window.locals=` + shareFixtureLocals + `;`
					}
					body := prefix + `locals.mset({"share_page_type":"error","errortype":` + value + `,"errno":115});</script>`
					return fixtureResponse(r, final, body), nil
				})
				err := probeShare(request{URL: "https://pan.baidu.com/s/1fixture", AccessCode: "abcd"}, client)
				if err == nil || err.Error() != tc.code || calls != 1 || remoteMutationStarted {
					t.Fatalf("got %v, calls=%d; expected %s without remote effects", err, calls, tc.code)
				}
			})
		}
	}
}

func TestShareErrorLocalsRequireExplicitPageTypeAndExactErrorType(t *testing.T) {
	for _, fields := range []string{
		`"errortype":null`, `"errortype":false`, `"errortype":3.1`, `"errortype":3.0`,
		`"errortype":3e0`, `"errortype":[]`, `"errortype":{}`, `"errortype":"03"`, `"errno":115`,
	} {
		client := fixtureClient(func(r *http.Request) (*http.Response, error) {
			return fixtureResponse(r, "", `<script>window.locals={"share_page_type":"error",`+fields+`};</script>`), nil
		})
		_, err := accessSharePageContext(client, "1fixture", true, false)
		if err == nil || err.Error() != "BAIDU_SHARE_UNAVAILABLE" {
			t.Fatalf("ambiguous structured reason must stay unknown: %v", err)
		}
	}
	for _, fields := range []string{`"errortype":3`, `"errno":115`, `"share_page_type":"share","errortype":3`} {
		client := fixtureClient(func(r *http.Request) (*http.Response, error) {
			body := `<title>分享已过期，无法访问</title><script>window.locals=` +
				strings.TrimSuffix(shareFixtureLocals, "}") + `,` + fields + `};</script>`
			return fixtureResponse(r, "", body), nil
		})
		if _, err := accessSharePage(client, "1fixture", true); err != nil {
			t.Fatalf("title or unrelated error fields must not invalidate normal share: %v", err)
		}
	}
	for _, final := range []string{
		"http://pan.baidu.com/s/1fixture", "https://pan.baidu.com.example.org/s/1fixture",
		"https://example.org/s/1fixture", "https://fixture@pan.baidu.com/s/1fixture",
	} {
		client := fixtureClient(func(r *http.Request) (*http.Response, error) {
			return fixtureResponse(r, final, `<script>locals.mset({"share_page_type":"error","errortype":3});</script>`), nil
		})
		_, err := accessSharePageContext(client, "1fixture", true, false)
		if err == nil || err.Error() != "BAIDU_SHARE_PAGE_INVALID" {
			t.Fatalf("untrusted page must not classify a share as blocked: %v", err)
		}
	}
}

func TestShareVerificationRequiresExplicitValidResponse(t *testing.T) {
	for _, tc := range []struct{ name, body, code string }{
		{"success", `{"errno":0,"randsk":"fixture-proof"}`, ""},
		{"wrong access code", `{"errno":-9,"errmsg":"secret-canary"}`, "BAIDU_SHARE_ACCESS_CODE_REJECTED"},
		{"captcha", `{"errno":8001}`, "BAIDU_SHARE_VERIFICATION_REQUIRED"},
		{"ambiguous context", `{"errno":-12}`, "BAIDU_SHARE_VERIFICATION_FAILED"},
		{"unknown errno", `{"errno":987654}`, "BAIDU_SHARE_VERIFICATION_FAILED"},
		{"missing errno", `{}`, "BAIDU_SHARE_RESPONSE_INVALID"},
		{"null errno", `{"errno":null,"randsk":"secret-canary"}`, "BAIDU_SHARE_RESPONSE_INVALID"},
		{"string errno", `{"errno":"0","randsk":"secret-canary"}`, "BAIDU_SHARE_RESPONSE_INVALID"},
		{"fraction errno", `{"errno":0.5}`, "BAIDU_SHARE_RESPONSE_INVALID"},
		{"missing proof", `{"errno":0}`, "BAIDU_SHARE_RESPONSE_INVALID"},
		{"empty proof", `{"errno":0,"randsk":"  "}`, "BAIDU_SHARE_RESPONSE_INVALID"},
		{"null proof", `{"errno":0,"randsk":null}`, "BAIDU_SHARE_RESPONSE_INVALID"},
		{"html", `<html>secret-canary</html>`, "BAIDU_SHARE_RESPONSE_INVALID"},
		{"oversized", strings.Repeat("x", maximumShareResponseBytes+1), "BAIDU_SHARE_RESPONSE_INVALID"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			calls := 0
			client := fixtureClient(func(r *http.Request) (*http.Response, error) {
				calls++
				if r.Method != http.MethodPost || r.Header.Get("User-Agent") != requester.UserAgent || r.Header.Get("Referer") != "https://pan.baidu.com/s/1fixture" {
					t.Fatal("verification request changed")
				}
				if err := r.ParseForm(); err != nil || r.Form.Get("pwd") != "abcd" || r.Form.Get("bdstoken") != "fixture-token" || r.Form.Get("vcode") != "null" || r.Form.Get("vcode_str") != "null" {
					t.Fatal("verification form changed")
				}
				return fixtureResponse(r, "", tc.body), nil
			})
			err := verifyShare(client, "https://pan.baidu.com/share/verify?shareid=789&uk=456", "https://pan.baidu.com/s/1fixture", "abcd", "fixture-token")
			if tc.code == "" {
				if err != nil {
					t.Fatal(err)
				}
			} else if err == nil || err.Error() != tc.code {
				t.Fatalf("got %v, expected %s", err, tc.code)
			}
			if calls != 1 {
				t.Fatal("verification retried")
			}
		})
	}
}

type failingReader struct{}

func (failingReader) Read([]byte) (int, error) { return 0, errors.New("secret-canary") }
func (failingReader) Close() error             { return nil }

func TestShareTransportAndBodyFailuresAreSafeAndBounded(t *testing.T) {
	for _, mode := range []string{"transport", "status", "read", "oversized"} {
		t.Run(mode, func(t *testing.T) {
			client := fixtureClient(func(r *http.Request) (*http.Response, error) {
				if mode == "transport" {
					return nil, errors.New("secret-canary")
				}
				resp := fixtureResponse(r, "", shareFixtureLocals)
				switch mode {
				case "status":
					resp.StatusCode = http.StatusServiceUnavailable
				case "read":
					resp.Body = failingReader{}
				case "oversized":
					resp.Body = io.NopCloser(strings.NewReader(strings.Repeat("x", maximumShareResponseBytes+1)))
				}
				return resp, nil
			})
			_, err := accessSharePage(client, "1fixture", true)
			want := "BAIDU_SHARE_NETWORK_ERROR"
			if mode == "oversized" {
				want = "BAIDU_SHARE_RESPONSE_INVALID"
			}
			if err == nil || err.Error() != want {
				t.Fatalf("unsafe or unexpected error %v", err)
			}
			err = verifyShare(client, "https://pan.baidu.com/share/verify", "https://pan.baidu.com/s/1fixture", "abcd", "fixture-token")
			if err == nil || err.Error() != want {
				t.Fatalf("unsafe or unexpected verify error %v", err)
			}
		})
	}
}

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

func resumeFixture(name, contents string) *baidupcs.FileDirectory {
	sum := md5.Sum([]byte(contents))
	return &baidupcs.FileDirectory{FsID: 11, Path: "/remote/" + name, Size: int64(len(contents)),
		MD5: "encoded-provider-md5", BlockListJSON: baidupcs.BlockListJSON{BlockList: []string{hex.EncodeToString(sum[:])}}}
}

func writeCheckpoint(t *testing.T, local string, state *transfer.DownloadInstanceInfoExport) []byte {
	t.Helper()
	data, err := proto.Marshal(state)
	if err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(local+pcsdownload.DownloadSuffix, data, 0600); err != nil {
		t.Fatal(err)
	}
	return data
}

func TestLocalReuseRequiresWholeFileChecksumEvenWithSidecar(t *testing.T) {
	for _, tc := range []struct {
		name, local   string
		sidecar, want bool
	}{
		{"complete", "original", false, true},
		{"complete with interrupted overwrite", "original", true, true},
		{"same length corrupt", "modified", false, false},
		{"same length corrupt with checkpoint", "modified", true, false},
		{"partial", "orig", true, false},
		{"missing", "", false, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			root := t.TempDir()
			local := filepath.Join(root, "data.bin")
			if tc.local != "" {
				if err := os.WriteFile(local, []byte(tc.local), 0600); err != nil {
					t.Fatal(err)
				}
			}
			if tc.sidecar {
				if err := os.WriteFile(local+pcsdownload.DownloadSuffix, []byte("preserve checkpoint"), 0600); err != nil {
					t.Fatal(err)
				}
			}
			before, _ := os.Stat(local)
			got, err := verifiedLocalDownload(local, resumeFixture("data.bin", "original"), nil)
			if err != nil || (got != nil) != tc.want {
				t.Fatalf("verified=%v, want=%v, err=%v", got != nil, tc.want, err)
			}
			if tc.local != "" {
				contents, err := os.ReadFile(local)
				after, _ := os.Stat(local)
				if err != nil || string(contents) != tc.local || !sameLocalFile(before, after) {
					t.Fatal("verification changed the file")
				}
			}
			if tc.sidecar {
				data, err := os.ReadFile(local + pcsdownload.DownloadSuffix)
				if err != nil || string(data) != "preserve checkpoint" {
					t.Fatal("verification changed the checkpoint")
				}
			}
		})
	}
}

func TestLocalReuseFetchesMissingChecksumAndPinsRemoteIdentity(t *testing.T) {
	root := t.TempDir()
	local := filepath.Join(root, "data.bin")
	if err := os.WriteFile(local, []byte("original"), 0600); err != nil {
		t.Fatal(err)
	}
	original := resumeFixture("data.bin", "original")
	for _, tc := range []struct {
		name      string
		change    func(*baidupcs.FileDirectory)
		wantError bool
	}{
		{"same object", func(fd *baidupcs.FileDirectory) {}, false},
		{"different id", func(fd *baidupcs.FileDirectory) { fd.FsID++ }, true},
		{"different path", func(fd *baidupcs.FileDirectory) { fd.Path += "2" }, true},
		{"different size", func(fd *baidupcs.FileDirectory) { fd.Size++ }, true},
		{"directory", func(fd *baidupcs.FileDirectory) { fd.Isdir = true }, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			listed := *original
			listed.BlockList = nil
			fresh := *original
			tc.change(&fresh)
			calls := 0
			got, err := verifiedLocalDownload(local, &listed, func(fd *baidupcs.FileDirectory) (*baidupcs.FileDirectory, error) { calls++; return &fresh, nil })
			if calls != 1 || (err != nil) != tc.wantError || (got != nil) == tc.wantError {
				t.Fatalf("calls=%d verified=%v err=%v", calls, got != nil, err)
			}
		})
	}
	for _, blocks := range [][]string{nil, {"not a hash"}, {original.BlockList[0], original.BlockList[0]}} {
		fd := *original
		fd.BlockList = blocks
		fd.MD5 = original.BlockList[0]
		got, err := verifiedLocalDownload(local, &fd, func(*baidupcs.FileDirectory) (*baidupcs.FileDirectory, error) {
			return nil, errors.New("metadata unavailable")
		})
		if got != nil || err != nil {
			t.Fatalf("unsupported or encoded checksum accepted: %v", err)
		}
	}
}

func TestLocalReuseRejectsSymlink(t *testing.T) {
	root := t.TempDir()
	target := filepath.Join(root, "target")
	local := filepath.Join(root, "link")
	if err := os.WriteFile(target, []byte("original"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(target, local); err != nil {
		t.Skip(err)
	}
	if got, err := verifiedLocalDownload(local, resumeFixture("data.bin", "original"), nil); got != nil || err == nil {
		t.Fatal("symlink accepted")
	}
}

func TestCheckpointProgressUsesRangesNotFileLength(t *testing.T) {
	root := t.TempDir()
	local := filepath.Join(root, "data.bin")
	if err := os.WriteFile(local, []byte("fullsize"), 0600); err != nil {
		t.Fatal(err)
	}
	valid := func() *transfer.DownloadInstanceInfoExport {
		return &transfer.DownloadInstanceInfoExport{TotalSize: 8, GenBegin: 8, BlockSize: 8, RangeGenMode: transfer.RangeGenMode_BlockSize, Ranges: []*transfer.Range{{Begin: 3, End: 8}}}
	}
	writeCheckpoint(t, local, valid())
	if n, ok := checkpointDownloadedBytes(local, 8); !ok || n != 3 {
		t.Fatalf("bytes=%d valid=%v", n, ok)
	}
	for _, change := range []func(*transfer.DownloadInstanceInfoExport){
		func(s *transfer.DownloadInstanceInfoExport) { s.TotalSize = 9 },
		func(s *transfer.DownloadInstanceInfoExport) { s.GenBegin = 9 },
		func(s *transfer.DownloadInstanceInfoExport) { s.BlockSize = 0 },
		func(s *transfer.DownloadInstanceInfoExport) { s.Ranges[0].Begin = -1 },
		func(s *transfer.DownloadInstanceInfoExport) { s.Ranges[0].End = 9 },
		func(s *transfer.DownloadInstanceInfoExport) {
			s.Ranges = append(s.Ranges, &transfer.Range{Begin: 4, End: 5})
		},
	} {
		s := valid()
		change(s)
		writeCheckpoint(t, local, s)
		if _, ok := checkpointDownloadedBytes(local, 8); ok {
			t.Fatal("invalid checkpoint accepted")
		}
	}
	if err := os.WriteFile(local+pcsdownload.DownloadSuffix, []byte{0xff}, 0600); err != nil {
		t.Fatal(err)
	}
	if _, ok := checkpointDownloadedBytes(local, 8); ok {
		t.Fatal("partial checkpoint accepted")
	}
}

func TestDownloadResumeSkipsVerifiedFilesAndPreservesPartialState(t *testing.T) {
	root := t.TempDir()
	dir := filepath.Join(root, "remote")
	if err := os.Mkdir(dir, 0700); err != nil {
		t.Fatal(err)
	}
	partial := filepath.Join(dir, "part.bin")
	complete := filepath.Join(dir, "complete.bin")
	if err := os.WriteFile(partial, []byte("pa"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(complete, []byte("done"), 0600); err != nil {
		t.Fatal(err)
	}
	checkpoint := writeCheckpoint(t, partial, &transfer.DownloadInstanceInfoExport{TotalSize: 4, GenBegin: 4, BlockSize: 4, RangeGenMode: transfer.RangeGenMode_BlockSize, Ranges: []*transfer.Range{{Begin: 2, End: 4}}})
	if err := os.WriteFile(complete+pcsdownload.DownloadSuffix, []byte("old overwrite state"), 0600); err != nil {
		t.Fatal(err)
	}
	before, _ := os.Stat(complete)
	var progress [][2]int64
	calls := 0
	files, err := downloadManifestFiles(root, "/remote", []*baidupcs.FileDirectory{resumeFixture("part.bin", "part"), resumeFixture("complete.bin", "done")}, nil,
		func(fd *baidupcs.FileDirectory, local string, checkpointProgress func(int64)) error {
			calls++
			if local != partial {
				t.Fatal("completed file downloaded again")
			}
			state, _ := os.ReadFile(local + pcsdownload.DownloadSuffix)
			if string(state) != string(checkpoint) {
				t.Fatal("partial checkpoint replaced")
			}
			checkpointProgress(3)
			checkpointProgress(3) // Idle heartbeat lets the caller clear stale speed.
			return os.WriteFile(local, []byte("part"), 0600)
		}, func(done, total int64) { progress = append(progress, [2]int64{done, total}) })
	if err != nil || calls != 1 || len(files) != 2 {
		t.Fatalf("calls=%d files=%d err=%v", calls, len(files), err)
	}
	if !slices.Equal(progress, [][2]int64{{6, 8}, {7, 8}, {7, 8}, {8, 8}}) {
		t.Fatalf("progress=%v", progress)
	}
	after, _ := os.Stat(complete)
	if !sameLocalFile(before, after) {
		t.Fatal("reused file changed")
	}
	if _, err := os.Stat(complete + pcsdownload.DownloadSuffix); err != nil {
		t.Fatal("sidecar removed")
	}
	for _, file := range files {
		if strings.HasSuffix(file, pcsdownload.DownloadSuffix) {
			t.Fatal("sidecar published")
		}
	}
}

func TestDownloadDoesNotSkipSameSizeCorruptionOrUnsupportedChecksum(t *testing.T) {
	for _, unsupported := range []bool{false, true} {
		root := t.TempDir()
		local := filepath.Join(root, "file.bin")
		if err := os.WriteFile(local, []byte("oops"), 0600); err != nil {
			t.Fatal(err)
		}
		fd := resumeFixture("file.bin", "good")
		if unsupported {
			fd.BlockList = nil
		}
		calls := 0
		var first int64 = -1
		_, err := downloadManifestFiles(root, "/remote/file.bin", []*baidupcs.FileDirectory{fd}, nil,
			func(*baidupcs.FileDirectory, string, func(int64)) error {
				calls++
				return os.WriteFile(local, []byte("good"), 0600)
			},
			func(done, total int64) {
				if first < 0 {
					first = done
				}
			})
		if err != nil || calls != 1 || first != 0 {
			t.Fatalf("unsupported=%v calls=%d first=%d err=%v", unsupported, calls, first, err)
		}
	}
}

func multipartFixture(name string, contents []byte, blockSize int64) *baidupcs.FileDirectory {
	fd := &baidupcs.FileDirectory{FsID: 12, Path: "/remote/" + name, Size: int64(len(contents)), MD5: strings.Repeat("0", 32)}
	for offset := int64(0); offset < fd.Size; offset += blockSize {
		end := offset + blockSize
		if end > fd.Size {
			end = fd.Size
		}
		sum := md5.Sum(contents[offset:end])
		fd.BlockList = append(fd.BlockList, hex.EncodeToString(sum[:]))
	}
	return fd
}

func TestMultipartChecksumUsesKnownBoundariesAndCompleteTail(t *testing.T) {
	for _, blockSize := range []int64{baidupcs.MinUploadBlockSize, 8 << 20, baidupcs.MiddleUploadBlockSize, 32 << 20, baidupcs.MaxUploadBlockSize} {
		fd := &baidupcs.FileDirectory{Size: blockSize + 7, BlockListJSON: baidupcs.BlockListJSON{BlockList: []string{strings.Repeat("0", 32), strings.Repeat("1", 32)}}}
		plan := trustedDownloadChecksums(fd)
		if plan == nil || plan.blockSize != blockSize || len(plan.hashes) != 2 {
			t.Fatalf("boundary %d not retained", blockSize)
		}
	}
	// Several arbitrary block sizes could produce this count. No supported
	// boundary fits, so the verifier must not guess one from the average.
	unknown := &baidupcs.FileDirectory{Size: 128<<20 + 7, BlockListJSON: baidupcs.BlockListJSON{BlockList: []string{strings.Repeat("0", 32), strings.Repeat("1", 32)}}}
	if trustedDownloadChecksums(unknown) != nil {
		t.Fatal("unsupported block layout accepted")
	}
}

func TestAmbiguousMultipartBlockBoundaries(t *testing.T) {
	// Both boundaries produce two blocks for this file. The verifier cannot
	// infer the original upload boundary from the file length and list alone.
	if got := uniqueMultipartBlockSize(40<<20, 2, 24<<20, 32<<20); got != 0 {
		t.Fatalf("ambiguous block boundary accepted: %d", got)
	}
}

func TestMultipartLocalReuseChecksEveryBlockAndPreservesSidecar(t *testing.T) {
	original := bytes.Repeat([]byte("a"), int(baidupcs.MinUploadBlockSize)+3)
	copy(original[len(original)-3:], []byte("end"))
	for _, tc := range []struct {
		name   string
		change func([]byte, *baidupcs.FileDirectory)
		want   bool
	}{
		{"complete with stale sidecar", func(_ []byte, _ *baidupcs.FileDirectory) {}, true},
		{"first block corrupt", func(b []byte, _ *baidupcs.FileDirectory) { b[0] = 'b' }, false},
		{"last partial block corrupt", func(b []byte, _ *baidupcs.FileDirectory) { b[len(b)-1] = 'x' }, false},
		{"swapped block order", func(_ []byte, fd *baidupcs.FileDirectory) {
			fd.BlockList[0], fd.BlockList[1] = fd.BlockList[1], fd.BlockList[0]
		}, false},
		{"tail hash missing", func(_ []byte, fd *baidupcs.FileDirectory) { fd.BlockList = fd.BlockList[:1] }, false},
		{"malformed tail checksum", func(_ []byte, fd *baidupcs.FileDirectory) { fd.BlockList[1] = "not-md5" }, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			local := filepath.Join(t.TempDir(), "multi.bin")
			data := append([]byte(nil), original...)
			fd := multipartFixture("multi.bin", original, baidupcs.MinUploadBlockSize)
			tc.change(data, fd)
			if err := os.WriteFile(local, data, 0600); err != nil {
				t.Fatal(err)
			}
			if err := os.WriteFile(local+pcsdownload.DownloadSuffix, []byte("preserve"), 0600); err != nil {
				t.Fatal(err)
			}
			before, _ := os.Lstat(local)
			got, err := verifiedLocalDownload(local, fd, nil)
			if err != nil || (got != nil) != tc.want {
				t.Fatalf("verified=%v want=%v err=%v", got != nil, tc.want, err)
			}
			after, _ := os.Lstat(local)
			sidecar, err := os.ReadFile(local + pcsdownload.DownloadSuffix)
			if !sameLocalFile(before, after) || err != nil || string(sidecar) != "preserve" {
				t.Fatal("verification modified local state")
			}
		})
	}
}

func TestDownloadReusesMultipartMetadataAndResumesOnlyIncompleteFile(t *testing.T) {
	root := t.TempDir()
	dir := filepath.Join(root, "remote")
	if err := os.Mkdir(dir, 0700); err != nil {
		t.Fatal(err)
	}
	data := bytes.Repeat([]byte("x"), int(baidupcs.MinUploadBlockSize)+3)
	complete := filepath.Join(dir, "multi.bin")
	partial := filepath.Join(dir, "part.bin")
	if err := os.WriteFile(complete, data, 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(complete+pcsdownload.DownloadSuffix, []byte("stale overwrite"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(partial, []byte("pa"), 0600); err != nil {
		t.Fatal(err)
	}
	checkpoint := writeCheckpoint(t, partial, &transfer.DownloadInstanceInfoExport{TotalSize: 4, GenBegin: 4, BlockSize: 4, RangeGenMode: transfer.RangeGenMode_BlockSize, Ranges: []*transfer.Range{{Begin: 2, End: 4}}})
	fresh := multipartFixture("multi.bin", data, baidupcs.MinUploadBlockSize)
	listed := *fresh
	listed.BlockList = nil
	before, _ := os.Lstat(complete)
	metaCalls := 0
	executed := 0
	var progress [][2]int64
	files, err := downloadManifestFiles(root, "/remote", []*baidupcs.FileDirectory{&listed, resumeFixture("part.bin", "part")},
		func(fd *baidupcs.FileDirectory) (*baidupcs.FileDirectory, error) {
			metaCalls++
			if fd.FsID != fresh.FsID {
				t.Fatal("metadata requested for partial file")
			}
			return fresh, nil
		},
		func(_ *baidupcs.FileDirectory, local string, _ func(int64)) error {
			executed++
			if local != partial {
				t.Fatal("completed multipart file downloaded again")
			}
			saved, _ := os.ReadFile(local + pcsdownload.DownloadSuffix)
			if !bytes.Equal(saved, checkpoint) {
				t.Fatal("checkpoint changed")
			}
			return os.WriteFile(local, []byte("part"), 0600)
		},
		func(done, total int64) { progress = append(progress, [2]int64{done, total}) })
	if err != nil || metaCalls != 1 || executed != 1 || len(files) != 2 {
		t.Fatalf("meta=%d execute=%d files=%d err=%v", metaCalls, executed, len(files), err)
	}
	size := int64(len(data))
	if len(progress) != 2 || progress[0] != [2]int64{size + 2, size + 4} || progress[1] != [2]int64{size + 4, size + 4} {
		t.Fatalf("progress=%v", progress)
	}
	after, _ := os.Lstat(complete)
	if !sameLocalFile(before, after) {
		t.Fatal("completed multipart file changed")
	}
	for _, file := range files {
		if strings.HasSuffix(file, pcsdownload.DownloadSuffix) {
			t.Fatal("sidecar published")
		}
	}
}

func TestMultipartResumeCannotPublishCorruptCompletedRange(t *testing.T) {
	original := bytes.Repeat([]byte("a"), int(baidupcs.MinUploadBlockSize)+3)
	copy(original[len(original)-3:], []byte("end"))
	for _, repairAll := range []bool{false, true} {
		t.Run(fmt.Sprint("repairAll=", repairAll), func(t *testing.T) {
			root := t.TempDir()
			local := filepath.Join(root, "multi.bin")
			corrupt := append([]byte(nil), original...)
			corrupt[0] = 'x'
			copy(corrupt[len(corrupt)-3:], []byte("bad"))
			if err := os.WriteFile(local, corrupt, 0600); err != nil {
				t.Fatal(err)
			}
			size := int64(len(original))
			sidecar := writeCheckpoint(t, local, &transfer.DownloadInstanceInfoExport{TotalSize: size, GenBegin: size, BlockSize: baidupcs.MinUploadBlockSize, RangeGenMode: transfer.RangeGenMode_BlockSize, Ranges: []*transfer.Range{{Begin: size - 3, End: size}}})
			fd := multipartFixture("multi.bin", original, baidupcs.MinUploadBlockSize)
			calls := 0
			result, err := downloadManifestFiles(root, fd.Path, []*baidupcs.FileDirectory{fd}, nil,
				func(_ *baidupcs.FileDirectory, p string, _ func(int64)) error {
					calls++
					f, e := os.OpenFile(p, os.O_WRONLY, 0600)
					if e != nil {
						return e
					}
					defer f.Close()
					if repairAll {
						_, e = f.WriteAt(original, 0)
					} else {
						_, e = f.WriteAt([]byte("end"), size-3)
					}
					return e
				}, func(int64, int64) {})
			if calls != 1 {
				t.Fatalf("execute calls=%d", calls)
			}
			if repairAll {
				if err != nil || len(result) != 1 {
					t.Fatalf("repaired file rejected: %v", err)
				}
			} else {
				if err == nil || err.Error() != "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH" || len(result) != 0 {
					t.Fatalf("corrupt file published: results=%d err=%v", len(result), err)
				}
			}
			saved, e := os.ReadFile(local + pcsdownload.DownloadSuffix)
			if e != nil || !bytes.Equal(saved, sidecar) {
				t.Fatal("bridge changed sidecar")
			}
			if info, e := os.Stat(local); e != nil || info.Size() != size {
				t.Fatal("bridge removed file")
			}
		})
	}
}

// A virtual slow reader advances time per read rather than waiting for the real watchdog.
type timedVerificationReader struct {
	reader io.Reader
	now    *time.Time
	delay  time.Duration
}

func (r timedVerificationReader) Read(p []byte) (int, error) {
	*r.now = r.now.Add(r.delay)
	return r.reader.Read(p)
}

type verificationProbeReader func([]byte) (int, error)

func (r verificationProbeReader) Read(p []byte) (int, error) { return r(p) }

func TestVerificationUsesOneLargeBufferAndStopsAtEachChecksumBoundary(t *testing.T) {
	const chunk = 4 << 20
	large := bytes.Repeat([]byte{0x35}, 2*chunk+23)
	for i := chunk; i < len(large); i++ {
		large[i] = 0x82
	}
	for _, tc := range []struct {
		name     string
		data     []byte
		file     *baidupcs.FileDirectory
		requests []int
	}{
		{"multipart", large, multipartFixture("large.bin", large, baidupcs.MinUploadBlockSize), []int{chunk, chunk, 23}},
		{"short file", []byte("verified"), resumeFixture("short.bin", "verified"), []int{8}},
		{"empty file", nil, resumeFixture("empty.bin", ""), nil},
	} {
		t.Run(tc.name, func(t *testing.T) {
			checksums := trustedDownloadChecksums(tc.file)
			if checksums == nil {
				t.Fatal("fixture must have trusted checksums")
			}
			source := bytes.NewReader(tc.data)
			var requests []int
			var firstBuffer *byte
			probe := verificationProbeReader(func(p []byte) (int, error) {
				if firstBuffer == nil {
					firstBuffer = &p[0]
				} else if firstBuffer != &p[0] {
					t.Fatal("verification allocated another read buffer")
				}
				requests = append(requests, len(p))
				return source.Read(p)
			})
			size, matches, err := verifyChecksumBlocks(newVerificationReader(probe, tc.file.Size, verificationProgress{}), checksums)
			if err != nil || !matches || size != tc.file.Size || !slices.Equal(requests, tc.requests) {
				t.Fatalf("bytes=%d matches=%v err=%v requests=%v, want %v", size, matches, err, requests, tc.requests)
			}
		})
	}
}

func TestVerificationPreservesShortReadAndExactCopyErrorSemantics(t *testing.T) {
	const data = "original"
	checksums := trustedDownloadChecksums(resumeFixture("file.bin", data))
	var reports []int64
	source := strings.NewReader(data)
	probe := verificationProbeReader(func(p []byte) (int, error) { return source.Read(p[:min(len(p), 2)]) })
	progress := verificationProgress{Clock: func() time.Time { return time.Unix(0, 0) },
		Report: func(read, total int64) { reports = append(reports, read) }}
	n, matches, err := verifyChecksumBlocks(newVerificationReader(probe, int64(len(data)), progress), checksums)
	if err != nil || !matches || n != int64(len(data)) || !slices.Equal(reports, []int64{0, int64(len(data))}) {
		t.Fatalf("short reads lost data or progress: bytes=%d matches=%v err=%v reports=%v", n, matches, err, reports)
	}
	for _, tc := range []struct {
		name    string
		data    string
		readErr error
		wantErr error
	}{
		{"early EOF", data[:3], io.EOF, io.EOF},
		{"other read failure", data[:3], io.ErrClosedPipe, io.ErrClosedPipe},
		{"error with complete block", data, io.ErrUnexpectedEOF, nil},
	} {
		t.Run(tc.name, func(t *testing.T) {
			probe := verificationProbeReader(func(p []byte) (int, error) { return copy(p, tc.data), tc.readErr })
			n, matches, err := verifyChecksumBlocks(newVerificationReader(probe, int64(len(data)), verificationProgress{}), checksums)
			if !errors.Is(err, tc.wantErr) || n != int64(len(tc.data)) || matches != (tc.wantErr == nil) {
				t.Fatalf("bytes=%d matches=%v err=%v, want %v", n, matches, err, tc.wantErr)
			}
		})
	}
}

func TestVerificationReadActivityIsThrottledAndCancellationIsObserved(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	now := time.Unix(0, 0)
	var reports []int64
	data := strings.Repeat("x", 128*1024)
	source := timedVerificationReader{strings.NewReader(data), &now, 400 * time.Millisecond}
	reader := newVerificationReader(source, int64(len(data)), verificationProgress{Context: ctx, Clock: func() time.Time { return now }, Report: func(read, total int64) {
		reports = append(reports, read)
		if read > 0 {
			cancel()
		}
	}})
	_, err := io.CopyBuffer(io.Discard, reader, make([]byte, 16*1024))
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("cancellation lost: %v", err)
	}
	if len(reports) != 2 || reports[0] != 0 || reports[1] <= 0 || reports[1] >= int64(len(data)) {
		t.Fatalf("unthrottled or fake completion reports: %v", reports)
	}
	before := len(reports)
	if _, err = reader.Read(make([]byte, 16)); !errors.Is(err, context.Canceled) || len(reports) != before {
		t.Fatal("cancelled reader performed further activity")
	}
}
func TestChecksumActivityCoversBothPreflightAndFinalVerificationWithoutDownloadAccounting(t *testing.T) {
	root := t.TempDir()
	if err := os.Mkdir(filepath.Join(root, "remote"), 0700); err != nil {
		t.Fatal(err)
	}
	data := strings.Repeat("original", 8192)
	existing := resumeFixture("existing.bin", data)
	pending := resumeFixture("pending.bin", data)
	if err := os.WriteFile(filepath.Join(root, "remote", "existing.bin"), []byte(data), 0600); err != nil {
		t.Fatal(err)
	}
	var checked [][2]int64
	var progress [][2]int64
	downloads := 0
	files, err := downloadManifestFiles(root, "/remote", []*baidupcs.FileDirectory{existing, pending}, nil,
		func(fd *baidupcs.FileDirectory, local string, _ func(int64)) error {
			downloads++
			return os.WriteFile(local, []byte(data), 0600)
		},
		func(done, total int64) { progress = append(progress, [2]int64{done, total}) },
		verificationProgress{Context: context.Background(), Report: func(read, total int64) { checked = append(checked, [2]int64{read, total}) }})
	if err != nil || len(files) != 2 || downloads != 1 {
		t.Fatalf("unexpected download/result: %v %v %d", err, files, downloads)
	}
	size := int64(len(data))
	if len(checked) != 4 || checked[0] != [2]int64{0, size} || checked[1] != [2]int64{size, size} || checked[2] != [2]int64{0, size} || checked[3] != [2]int64{size, size} {
		t.Fatalf("missing pre/post checksum activity: %v", checked)
	}
	if len(progress) != 2 || progress[0] != [2]int64{size, size * 2} || progress[1] != [2]int64{size * 2, size * 2} {
		t.Fatalf("verification inflated download accounting: %v", progress)
	}
}

func TestPostTransportVerificationRetriesOnlyChangedLocalIdentityOnce(t *testing.T) {
	for _, tc := range []struct {
		name                 string
		wantError            error
		wantReads, downloads int
	}{
		{"settled", nil, 2, 1},
		{"still changing", errLocalFileChanged, 2, 1},
		{"replaced with unrelated bytes", errLocalFileChanged, 2, 1},
		{"cancelled on retry", context.Canceled, 2, 1},
		{"ordinary read error", nil, 1, 1},
		{"pre-existing file changes", errLocalFileChanged, 1, 0},
	} {
		t.Run(tc.name, func(t *testing.T) {
			root := t.TempDir()
			local := filepath.Join(root, "remote", "payload.bin")
			if err := os.Mkdir(filepath.Dir(local), 0700); err != nil {
				t.Fatal(err)
			}
			data := strings.Repeat("original", 1024)
			corrupt := strings.Repeat("modified", 1024)
			file := resumeFixture(filepath.Base(local), data)
			if tc.name == "pre-existing file changes" {
				if err := os.WriteFile(local, []byte(data), 0600); err != nil {
					t.Fatal(err)
				}
			}
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			reads, downloads := 0, 0
			files, err := downloadManifestFiles(root, "/remote", []*baidupcs.FileDirectory{file}, nil,
				func(_ *baidupcs.FileDirectory, destination string, _ func(int64)) error {
					downloads++
					return os.WriteFile(destination, []byte(data), 0600)
				}, func(int64, int64) {}, verificationProgress{Context: ctx, Report: func(read, total int64) {
					if read == 0 {
						reads++
						if tc.name == "cancelled on retry" && reads == 2 {
							cancel()
						}
						if tc.name == "ordinary read error" {
							if err := os.Truncate(local, 0); err != nil {
								t.Fatal(err)
							}
						}
						return
					}
					if read != total || (reads != 1 && tc.name != "still changing") {
						return
					}
					if tc.name == "replaced with unrelated bytes" {
						replacement := filepath.Join(root, "replacement.bin")
						if err := os.WriteFile(replacement, []byte(corrupt), 0600); err != nil {
							t.Fatal(err)
						}
						if err := os.Rename(replacement, local); err != nil {
							t.Fatal(err)
						}
					} else {
						stamp := time.Unix(1700000000+int64(reads), 0)
						if err := os.Chtimes(local, stamp, stamp); err != nil {
							t.Fatal(err)
						}
					}
				}})
			if reads != tc.wantReads || downloads != tc.downloads {
				t.Fatalf("reads=%d downloads=%d error=%v", reads, downloads, err)
			}
			if tc.name == "ordinary read error" {
				var storage *storageFailure
				if !errors.As(err, &storage) || storage.code != "BAIDU_OUTPUT_FILE_READ_FAILED" {
					t.Fatalf("expected original read failure, got %v", err)
				}
			} else if !errors.Is(err, tc.wantError) {
				t.Fatalf("expected %v, got %v", tc.wantError, err)
			}
			if err == nil {
				if len(files) != 1 || files[0] != local {
					t.Fatalf("expected exact existing payload, got %v", files)
				}
			} else if len(files) != 0 {
				t.Fatalf("unproven payload returned: %v", files)
			}
			if tc.name == "replaced with unrelated bytes" {
				if bytes, err := os.ReadFile(local); err != nil || string(bytes) != corrupt {
					t.Fatalf("retry modified the replacement: %v", err)
				}
			}
		})
	}
}

func TestCancellationDuringChecksumNeverStartsDownloadOrDeliversFiles(t *testing.T) {
	root := t.TempDir()
	if err := os.Mkdir(filepath.Join(root, "remote"), 0700); err != nil {
		t.Fatal(err)
	}
	data := strings.Repeat("original", 8192)
	file := resumeFixture("complete.bin", data)
	if err := os.WriteFile(filepath.Join(root, "remote", "complete.bin"), []byte(data), 0600); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	now := time.Unix(0, 0)
	downloads := 0
	files, err := downloadManifestFiles(root, "/remote", []*baidupcs.FileDirectory{file}, nil,
		func(*baidupcs.FileDirectory, string, func(int64)) error { downloads++; return nil }, func(int64, int64) {},
		verificationProgress{Context: ctx, Clock: func() time.Time { now = now.Add(time.Second); return now }, Report: func(read, total int64) {
			if read > 0 {
				cancel()
			}
		}})
	if !errors.Is(err, context.Canceled) || len(files) != 0 || downloads != 0 {
		t.Fatalf("cancellation delivered or downloaded: %v %v %d", err, files, downloads)
	}
}

func TestCancellationAfterPreflightNeverStartsUpstreamDownload(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	downloads := 0
	files, err := downloadManifestFiles(t.TempDir(), "/remote", []*baidupcs.FileDirectory{resumeFixture("missing.bin", "data")}, nil,
		func(*baidupcs.FileDirectory, string, func(int64)) error { downloads++; return nil },
		func(int64, int64) { cancel() }, verificationProgress{Context: ctx})
	if !errors.Is(err, context.Canceled) || len(files) != 0 || downloads != 0 {
		t.Fatalf("cancelled preflight started download: %v %v %d", err, files, downloads)
	}
}

func TestShareProbeDoesNotTransferOrFetchPayloads(t *testing.T) {
	requests := []string{}
	client := fixtureClient(func(r *http.Request) (*http.Response, error) {
		requests = append(requests, r.Method+" "+r.URL.Path)
		switch r.URL.Path {
		case "/s/1fixture":
			return fixtureResponse(r, "", `<script>locals.mset(`+shareFixtureLocals+`);</script>`), nil
		case "/share/verify":
			if err := r.ParseForm(); err != nil || r.Form.Get("pwd") != "a1b2" {
				t.Fatal("access code not supplied")
			}
			return fixtureResponse(r, "", `{"errno":0,"randsk":"fixture"}`), nil
		case "/share/list":
			if r.URL.Query().Get("num") != "10" || r.URL.Query().Get("page") != "1" {
				t.Fatal("unbounded discovery")
			}
			return fixtureResponse(r, "", `{"errno":0,"list":[{"fs_id":42}]}`), nil
		default:
			t.Fatal("probe attempted mutation or payload request: " + r.URL.Path)
			return nil, errors.New("unexpected")
		}
	})
	if err := probeShare(request{URL: "https://pan.baidu.com/s/1fixture", AccessCode: "a1b2"}, client); err != nil {
		t.Fatal(err)
	}
	if len(requests) != 4 {
		t.Fatalf("unexpected request count %d", len(requests))
	}
}

func TestShareProbeExpiredEvidenceDoesNotRequireAccountOrCode(t *testing.T) {
	client := fixtureClient(func(r *http.Request) (*http.Response, error) {
		return fixtureResponse(r, "https://pan.baidu.com/wap/error?errortype=10", "expired"), nil
	})
	if err := probeShare(request{URL: "https://pan.baidu.com/s/1fixture"}, client); err == nil || err.Error() != "BAIDU_SHARE_EXPIRED" {
		t.Fatal(err)
	}
}

func TestShareProbeRejectsWrongEndpointBeforeAnyRequest(t *testing.T) {
	client := fixtureClient(func(r *http.Request) (*http.Response, error) { t.Fatal("should not contact network"); return nil, nil })
	for _, input := range []string{"https://evil.test/s/1fixture", "https://pan.baidu.com:443/s/1fixture", "https://user@pan.baidu.com/s/1fixture", "http://pan.baidu.com/s/1fixture", "https://pan.baidu.com/anything/1fixture"} {
		if probeShare(request{URL: input}, client) == nil {
			t.Fatal("accepted " + input)
		}
	}
}

func TestShareProbeKeepsTLSAndRedirectBoundaries(t *testing.T) {
	probe := newShareProbeRequester(context.Background())
	defer probe.client.CloseIdleConnections()
	transport := probe.client.Transport.(*http.Transport)
	if transport.TLSClientConfig != nil && transport.TLSClientConfig.InsecureSkipVerify {
		t.Fatal("TLS verification disabled")
	}
	for _, raw := range []string{"http://pan.baidu.com/s/1fixture", "https://example.test", "https://pan.baidu.com.evil.test", "https://user@pan.baidu.com"} {
		u, _ := url.Parse(raw)
		if probe.client.CheckRedirect(&http.Request{URL: u}, nil) == nil {
			t.Fatal("unsafe redirect allowed")
		}
	}
	if _, err := probe.Req(http.MethodPost, "https://pan.baidu.com/share/transfer", map[string]string{}, nil); err == nil {
		t.Fatal("mutation accepted")
	}
}

func TestAnonymousShareContextAllowsEmptyAccountTokenButTransferStillRequiresIt(t *testing.T) {
	client := fixtureClient(func(r *http.Request) (*http.Response, error) {
		return fixtureResponse(r, "https://pan.baidu.com/wap/init?surl=fixture", `<script>try { window.locals = {"shareid":123,"share_uk":"456","bdstoken":"","loginstate":0}; }catch(ex){}</script>`), nil
	})
	if _, err := accessSharePageContext(client, "1fixture", true, false); err != nil {
		t.Fatal(err)
	}
	if _, err := accessSharePage(client, "1fixture", true); err == nil || err.Error() != "BAIDU_SHARE_PAGE_INVALID" {
		t.Fatal("account flow must remain strict")
	}
}

func TestFixedOfficialShareProbeUsesConfiguredResolverWithoutChangingTLSHost(t *testing.T) {
	t.Setenv("NO_PROXY", "*")
	t.Setenv("no_proxy", "*")
	var addresses []string
	probe := newShareProbeRequesterWithDialer(context.Background(), func(ctx context.Context, network, address string) (net.Conn, error) {
		addresses = append(addresses, address)
		return nil, errors.New("fixture stops before connecting")
	})
	defer probe.client.CloseIdleConnections()
	for _, host := range []string{"pan.baidu.com", "yun.baidu.com"} {
		if _, err := probe.Req(http.MethodGet, "https://"+host+"/s/1fixture", nil, nil); err == nil {
			t.Fatal("fixture must fail before connecting")
		}
	}
	if len(addresses) != 2 || addresses[0] != "pan.baidu.com:443" || addresses[1] != "yun.baidu.com:443" {
		t.Fatalf("official hostname must reach configured resolver unchanged: %v", addresses)
	}
	for _, raw := range []string{"https://198.18.0.72/s/1fixture", "https://127.0.0.1/s/1fixture", "https://10.0.0.1/s/1fixture", "http://pan.baidu.com/s/1fixture", "https://pan.baidu.com:8443/s/1fixture"} {
		if _, err := probe.Req(http.MethodGet, raw, nil, nil); err == nil {
			t.Fatal("non-official endpoint accepted: " + raw)
		}
	}
	if len(addresses) != 2 {
		t.Fatal("untrusted endpoint reached dialer")
	}
}

func TestShareProbePreservesDNSPolicyReasonAcrossEveryMetadataRequest(t *testing.T) {
	for _, failedPath := range []string{"/s/1fixture", "/share/verify", "/share/list"} {
		t.Run(failedPath, func(t *testing.T) {
			client := fixtureClient(func(r *http.Request) (*http.Response, error) {
				if r.URL.Path == failedPath {
					return nil, fmt.Errorf("transport detail: %w", errShareProbeNetworkPolicy)
				}
				if r.URL.Path == "/share/verify" {
					return fixtureResponse(r, "", `{"errno":0,"randsk":"fixture"}`), nil
				}
				return fixtureResponse(r, "", `<script>locals.mset(`+shareFixtureLocals+`);</script>`), nil
			})
			err := probeShare(request{URL: "https://pan.baidu.com/s/1fixture", AccessCode: "a1b2"}, client)
			if !errors.Is(err, errShareProbeNetworkPolicy) || err.Error() != "BAIDU_SHARE_NETWORK_REQUIRES_PROXY" {
				t.Fatalf("DNS policy should remain distinct without URL or transport detail: %v", err)
			}
		})
	}
	if err := shareRequestError(errors.New("BAIDU_SHARE_NETWORK_REQUIRES_PROXY")); err.Error() != "BAIDU_SHARE_NETWORK_ERROR" {
		t.Fatal("untrusted text must not establish a DNS policy failure")
	}
}

func TestStorageFailureIsStructuredSanitizedAndDistinctFromCloudQuota(t *testing.T) {
	for _, tc := range []struct {
		cause error
		kind  string
	}{
		{syscall.ENOSPC, "diskFull"}, {syscall.EDQUOT, "quota"}, {syscall.EACCES, "permission"},
		{syscall.EIO, "unavailable"}, {errors.New("unknown local failure"), "unknown"},
	} {
		err := storageFail("BAIDU_OUTPUT_DIRECTORY_FAILED", "createDirectory", &os.PathError{Op: "mkdir", Path: "/private/secret", Err: tc.cause})
		event := errorEvent(err)
		data, _ := json.Marshal(event)
		if event["storage"].(map[string]any)["kind"] != tc.kind || strings.Contains(string(data), "secret") || strings.Contains(string(data), tc.cause.Error()) {
			t.Fatalf("unsafe or incorrect storage event: %s", data)
		}
	}
	for _, code := range []string{"BAIDU_OUTPUT_DIRECTORY_FAILED", "BAIDU_STORAGE_QUOTA_EXCEEDED"} {
		if _, ok := errorEvent(fail(code))["storage"]; ok {
			t.Fatal("legacy or remote error inferred to be local storage")
		}
	}
}

func TestInspectionProfileIsReadOnlyAndRequiresExactStableIdentity(t *testing.T) {
	saved := pcsconfig.Config
	defer func() { pcsconfig.Config = saved }()
	root := t.TempDir()
	config := filepath.Join(root, pcsconfig.ConfigName)
	original := []byte(`{"baidu_active_uid":123,"baidu_user_list":[{"uid":123,"bduss":"fixture"}]}`)
	if err := os.WriteFile(config, original, 0600); err != nil {
		t.Fatal(err)
	}
	before, _ := os.Stat(config)
	if _, uid, err := inspectionPCS(root, "123"); err != nil || uid != "123" {
		t.Fatalf("valid profile rejected: %v", err)
	}
	after, _ := os.Stat(config)
	data, _ := os.ReadFile(config)
	names, _ := os.ReadDir(root)
	if !sameLocalFile(before, after) || !bytes.Equal(data, original) || len(names) != 1 {
		t.Fatal("inspection wrote account data")
	}
	if _, _, err := inspectionPCS(root, "456"); err == nil || err.Error() != "BAIDU_ACCOUNT_IDENTITY_CHANGED" {
		t.Fatal("changed identity accepted")
	}
	if err := os.WriteFile(config, append(original, []byte(`{}`)...), 0600); err != nil {
		t.Fatal(err)
	}
	if _, _, err := inspectionPCS(root, "123"); err == nil {
		t.Fatal("trailing/truncated profile accepted")
	}
	missing := filepath.Join(root, "missing")
	if _, _, err := inspectionPCS(missing, "123"); err == nil {
		t.Fatal("missing profile accepted")
	}
	if _, err := os.Stat(missing); !os.IsNotExist(err) {
		t.Fatal("inspection created directory")
	}
}

func TestDownloadDispatchUsesReadOnlyProfileAndRejectsUnboundedConnections(t *testing.T) {
	savedConfig, savedInput := pcsconfig.Config, os.Stdin
	defer func() { pcsconfig.Config, os.Stdin = savedConfig, savedInput }()
	root := t.TempDir()
	profile := filepath.Join(root, pcsconfig.ConfigName)
	original := []byte(`{"baidu_active_uid":123,"baidu_user_list":[{"uid":123,"bduss":"fixture"}]}`)
	if err := os.WriteFile(profile, original, 0400); err != nil {
		t.Fatal(err)
	}
	before, _ := os.Stat(profile)
	requestFile := filepath.Join(t.TempDir(), "request.json")
	if err := os.WriteFile(requestFile, []byte(`{"command":"download","expectedUid":"123","connectionsPerFile":21}`), 0600); err != nil {
		t.Fatal(err)
	}
	input, err := os.Open(requestFile)
	if err != nil {
		t.Fatal(err)
	}
	defer input.Close()
	os.Stdin = input
	t.Setenv(pcsconfig.EnvConfigDir, root)
	if err := run(context.Background()); err == nil || err.Error() != "BAIDU_DOWNLOAD_OPTIONS_INVALID" {
		t.Fatalf("unexpected dispatch: %v", err)
	}
	after, _ := os.Stat(profile)
	data, _ := os.ReadFile(profile)
	names, _ := os.ReadDir(root)
	if !sameLocalFile(before, after) || !bytes.Equal(data, original) || len(names) != 1 {
		t.Fatal("download dispatch changed profile")
	}
}

func TestReadOnlyProfileInitializesUpstreamDownloadIdentityAndKeepsExistingPathJournal(t *testing.T) {
	saved := pcsconfig.Config
	defer func() { pcsconfig.Config = saved }()
	root, profile, r, entries := portableFixture(t)
	originalProfile := []byte(`{"baidu_active_uid":17,"pan_ua":"FixturePan","pcs_ua":"FixturePCS","baidu_user_list":[{"uid":17,"bduss":"fixture-bduss","stoken":"fixture-stoken"}]}`)
	config := filepath.Join(profile, pcsconfig.ConfigName)
	if err := os.WriteFile(config, originalProfile, 0400); err != nil {
		t.Fatal(err)
	}
	// A previous bridge wrote this operation's journal with the real account UID.
	wanted, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, true)
	if err != nil {
		t.Fatal(err)
	}
	journalFiles, err := filepath.Glob(filepath.Join(profile, "operations", "*.paths-v1.json"))
	if err != nil || len(journalFiles) != 1 {
		t.Fatalf("missing journal: %v", err)
	}
	journalBefore, _ := os.ReadFile(journalFiles[0])
	profileBefore, _ := os.Stat(config)
	pcs, uid, err := inspectionPCS(profile, "17")
	if err != nil || uid != "17" {
		t.Fatalf("readonly profile failed: %v", err)
	}
	if pcsconfig.Config.ActiveUser().UID != 17 || pcsconfig.Config.ActiveUser().BDUSS != "fixture-bduss" {
		t.Fatal("upstream download active-user cache was not initialized")
	}
	if pcsconfig.Config.ActiveUserBaiduPCS() != pcs || pcs.GetBDUSS() != "fixture-bduss" {
		t.Fatal("upstream payload downloader would use a different or anonymous client")
	}
	panURL, _ := url.Parse("https://pan.baidu.com/")
	var hasStoken bool
	for _, cookie := range pcsconfig.Config.ActiveUserBaiduPCS().GetClient().Jar.Cookies(panURL) {
		hasStoken = hasStoken || cookie.Name == "STOKEN" && cookie.Value == "fixture-stoken"
	}
	if !hasStoken {
		t.Fatal("upstream download client lost its STOKEN")
	}
	// Exercise the same global client + cookie-cloning setup as upstream
	// DownloadTaskUnit.execPanDownload, without contacting a payload server.
	panClient, pcsClient := pcsconfig.Config.PanHTTPClient(), pcsconfig.Config.PCSHTTPClient()
	if panClient.UserAgent != "FixturePan" || pcsClient.UserAgent != "FixturePCS" {
		t.Fatal("upstream download HTTP clients lost profile configuration")
	}
	payloadURL := "https://fixture.pcs.baidu.com/payload"
	payloadCookies, err := pcsdownload.CloneJarWithDomain(pcsconfig.Config.ActiveUserBaiduPCS().GetClient().Jar, payloadURL)
	if err != nil {
		t.Fatal(err)
	}
	panClient.SetCookiejar(payloadCookies)
	payload, _ := url.Parse(payloadURL)
	var hasBduss bool
	for _, cookie := range panClient.Jar.Cookies(payload) {
		hasBduss = hasBduss || cookie.Name == "BDUSS" && cookie.Value == "fixture-bduss"
	}
	if !hasBduss {
		t.Fatal("upstream download HTTP client would send anonymous payload requests")
	}
	panClient.CloseIdleConnections()
	pcsClient.CloseIdleConnections()
	actual, err := resolveDownloadPaths(context.Background(), profile,
		fmt.Sprint(pcsconfig.Config.ActiveUser().UID), root, r, entries, true)
	if err != nil || fmt.Sprint(actual) != fmt.Sprint(wanted) {
		t.Fatalf("original download identity was rejected: %v", err)
	}
	journalAfter, _ := os.ReadFile(journalFiles[0])
	profileAfter, _ := os.Stat(config)
	profileBytes, _ := os.ReadFile(config)
	if !bytes.Equal(journalBefore, journalAfter) || !sameLocalFile(profileBefore, profileAfter) || !bytes.Equal(originalProfile, profileBytes) {
		t.Fatal("readonly initialization changed credentials or an existing operation journal")
	}
}

func TestMembershipRequiresExplicitProviderEvidence(t *testing.T) {
	for value, want := range map[int]string{0: "free", 1: "vip", 2: "svip"} {
		got, err := membershipKind([]byte(fmt.Sprintf(`{"errno":0,"vip_type":%d,"uk":456}`, value)))
		if err != nil || got != want {
			t.Fatalf("membership %d: %q %v", value, got, err)
		}
	}
	for _, body := range []string{
		`{}`, `{"errno":0,"uk":456}`, `{"errno":0,"vip_type":2}`, `{"errno":0,"vip_type":2,"uk":0}`,
		`{"errno":0,"vip_type":null,"uk":456}`, `{"errno":0,"vip_type":"2","uk":456}`,
		`{"errno":0,"vip_type":2.5,"uk":456}`, `{"errno":0,"vip_type":9,"uk":456}`,
		`{"errno":-6,"errmsg":"private-cookie","vip_type":2,"uk":456}`, `{"errno":0,"vip_type":2,"uk":456}{}`,
		strings.Repeat("x", maximumMembershipResponseBytes+1),
	} {
		kind, err := membershipKind([]byte(body))
		if err == nil || kind != "" || strings.Contains(err.Error(), "private-cookie") {
			t.Fatal("unsupported membership accepted or raw diagnostics leaked")
		}
	}
}

func TestMembershipProbeIsOneBoundedOfficialRead(t *testing.T) {
	calls := 0
	client := fixtureClient(func(r *http.Request) (*http.Response, error) {
		calls++
		if r.Method != http.MethodGet || r.URL.String() != membershipURL {
			t.Fatalf("unexpected membership request: %s", r.Method)
		}
		return fixtureResponse(r, membershipURL, `{"errno":0,"uk":456,"vip_type":2}`), nil
	})
	kind, err := probeMembership(client)
	if err != nil || kind != "svip" || calls != 1 {
		t.Fatalf("probe failed %q %v", kind, err)
	}
	client = fixtureClient(func(r *http.Request) (*http.Response, error) {
		return fixtureResponse(r, "https://example.invalid/private-cookie", `{"errno":0,"uk":456,"vip_type":2}`), nil
	})
	if _, err := probeMembership(client); err == nil {
		t.Fatal("redirected membership trusted")
	}
	pcs := baidupcs.NewPCS(250528, "fixture")
	protected := newMembershipRequester(context.Background(), pcs)
	defer protected.client.CloseIdleConnections()
	if protected.client.Jar != pcs.GetClient().Jar {
		t.Fatal("account session was not reused")
	}
	transport := protected.client.Transport.(*http.Transport)
	if transport.TLSClientConfig != nil && transport.TLSClientConfig.InsecureSkipVerify {
		t.Fatal("membership disabled TLS verification")
	}
	if protected.client.CheckRedirect(&http.Request{}, nil) != http.ErrUseLastResponse {
		t.Fatal("membership permits cookie redirects")
	}
}

func TestDownloadConnectionsAreExplicitAndBounded(t *testing.T) {
	for _, value := range []int{0, 1, 4, 20} {
		got, err := downloadConnections(value)
		want := value
		if want == 0 {
			want = 1
		}
		if err != nil || got != want {
			t.Fatalf("connections %d: %d %v", value, got, err)
		}
	}
	for _, value := range []int{-1, 21, 100000} {
		if _, err := downloadConnections(value); err == nil {
			t.Fatal("unbounded connections accepted")
		}
	}
}

func TestRetainedInspectionPreservesFullAndCheckpointedFiles(t *testing.T) {
	root := t.TempDir()
	folder := filepath.Join(root, "saved")
	if err := os.Mkdir(folder, 0700); err != nil {
		t.Fatal(err)
	}
	full := filepath.Join(folder, "full")
	partial := filepath.Join(folder, "partial")
	if err := os.WriteFile(full, []byte("full"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(partial, []byte("12"), 0600); err != nil {
		t.Fatal(err)
	}
	checkpoint := writeCheckpoint(t, partial, &transfer.DownloadInstanceInfoExport{TotalSize: 4, GenBegin: 4, BlockSize: 4, RangeGenMode: transfer.RangeGenMode_BlockSize, Ranges: []*transfer.Range{{Begin: 2, End: 4}}})
	entries := []entry{{ID: 1, Path: "/saved/full", Size: 4}, {ID: 2, Path: "/saved/partial", Size: 4}, {ID: 3, Path: "/saved/missing", Size: 10}}
	before, _ := os.Stat(full)
	if err := inspectLocalDownload(context.Background(), root, "/saved", entries); err != nil {
		t.Fatal(err)
	}
	after, _ := os.Stat(full)
	data, _ := os.ReadFile(partial + pcsdownload.DownloadSuffix)
	if !sameLocalFile(before, after) || !bytes.Equal(data, checkpoint) {
		t.Fatal("read-only inspection changed files")
	}
	if _, err := os.Stat(filepath.Join(folder, "missing")); !os.IsNotExist(err) {
		t.Fatal("inspection created missing file")
	}
	if err := os.Remove(partial + pcsdownload.DownloadSuffix); err != nil {
		t.Fatal(err)
	}
	if err := inspectLocalDownload(context.Background(), root, "/saved", entries); err == nil || err.Error() != "BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED" {
		t.Fatalf("unverified partial accepted: %v", err)
	}
	writeCheckpoint(t, partial, &transfer.DownloadInstanceInfoExport{TotalSize: 99, GenBegin: 4, BlockSize: 4, RangeGenMode: transfer.RangeGenMode_BlockSize})
	if err := inspectLocalDownload(context.Background(), root, "/saved", entries); err == nil || err.Error() != "BAIDU_DOWNLOAD_CHECKPOINT_INVALID" {
		t.Fatalf("wrong checkpoint accepted: %v", err)
	}
}

func TestRetainedInspectionAcceptsOnlyAnUnstartedEmptyCheckpointPair(t *testing.T) {
	root := t.TempDir()
	local := filepath.Join(root, "file")
	sidecar := local + pcsdownload.DownloadSuffix
	if err := os.WriteFile(local, nil, 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(sidecar, nil, 0600); err != nil {
		t.Fatal(err)
	}
	entries := []entry{{ID: 1, Path: "/file", Size: 4}}
	if err := inspectLocalDownload(context.Background(), root, "/file", entries); err != nil {
		t.Fatal(err)
	}
	for _, name := range []string{local, sidecar} {
		if info, err := os.Stat(name); err != nil || info.Size() != 0 {
			t.Fatalf("inspection changed %s", name)
		}
	}
	if err := os.WriteFile(local, []byte{1}, 0600); err != nil {
		t.Fatal(err)
	}
	if err := inspectLocalDownload(context.Background(), root, "/file", entries); err == nil || err.Error() != "BAIDU_DOWNLOAD_CHECKPOINT_INVALID" {
		t.Fatalf("nonempty partial with empty checkpoint accepted: %v", err)
	}
}

func TestRetainedInspectionRejectsUnexpectedFilesSymlinksAndChangedManifest(t *testing.T) {
	root := t.TempDir()
	entries := []entry{{ID: 1, Path: "/saved/file", Size: 4}}
	if !sameManifest(entries, entries) || sameManifest(entries, []entry{{ID: 1, Path: "/saved/file", Size: 5}}) || sameManifest(entries, []entry{{ID: 2, Path: "/saved/file", Size: 4}}) {
		t.Fatal("manifest identity invalid")
	}
	if sameManifest([]entry{{ID: 1, Path: "/saved/a", Size: 4}, {ID: 1, Path: "/saved/b", Size: 4}}, entries) {
		t.Fatal("duplicate identity accepted")
	}
	if err := os.WriteFile(filepath.Join(root, "other"), nil, 0600); err != nil {
		t.Fatal(err)
	}
	if err := inspectLocalDownload(context.Background(), root, "/saved", entries); err == nil || err.Error() != "BAIDU_OUTPUT_UNEXPECTED_FILE" {
		t.Fatal("foreign file accepted")
	}
	if err := os.Remove(filepath.Join(root, "other")); err != nil {
		t.Fatal(err)
	}
	if err := os.Mkdir(filepath.Join(root, "saved"), 0700); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(filepath.Join(root, "other"), filepath.Join(root, "saved", "file")); err != nil {
		t.Skip("symlinks unavailable")
	}
	if err := inspectLocalDownload(context.Background(), root, "/saved", entries); err == nil {
		t.Fatal("symlink accepted")
	}
}

func portableFixture(t *testing.T) (string, string, request, []entry) {
	t.Helper()
	root, profile := t.TempDir(), t.TempDir()
	remote := "/saved/transfer"
	entries := []entry{{1, remote, 0, true}, {2, remote + "/" + strings.Repeat("多", 90) + ".7z.001", 4, false}}
	return root, profile, request{OperationID: "original-download", RemotePath: remote, OutputDirectory: root, ExpectedUID: "17", ExpectedManifest: entries}, entries
}

func TestPortableManifestPreservesFamiliesBoundsAndOriginalManifest(t *testing.T) {
	stem := strings.Repeat("日本語", 40)
	dir := "/saved/" + strings.Repeat("目录", 60)
	var entries []entry
	for i, suffix := range []string{".part01.rar", ".part02.rar", ".rar", ".r00", ".r01", ".zip", ".z01", ".7z.001", ".7z.002"} {
		entries = append(entries, entry{int64(i + 1), dir + "/" + stem + suffix, 4, false})
	}
	before, _ := json.Marshal(entries)
	paths, err := portableManifestPaths(dir, entries)
	if err != nil {
		t.Fatal(err)
	}
	family := ""
	for _, e := range entries {
		relative := paths[e.Path]
		for _, segment := range strings.Split(relative, "/") {
			if !utf8.ValidString(segment) || len(segment) > 255 {
				t.Fatalf("invalid segment: %q", segment)
			}
		}
		name := path.Base(relative)
		if len(name)+len(pcsdownload.DownloadSuffix) > 255 {
			t.Fatal("no checkpoint suffix budget")
		}
		mappedStem, suffix := localNameParts(name, false)
		_, originalSuffix := localNameParts(path.Base(e.Path), false)
		if suffix != originalSuffix {
			t.Fatalf("suffix changed: %q != %q", suffix, originalSuffix)
		}
		if family == "" {
			family = mappedStem
		} else if mappedStem != family {
			t.Fatal("multipart family split")
		}
	}
	after, _ := json.Marshal(entries)
	if !bytes.Equal(before, after) {
		t.Fatal("remote manifest mutated")
	}
	for i, j := 0, len(entries)-1; i < j; i, j = i+1, j-1 {
		entries[i], entries[j] = entries[j], entries[i]
	}
	other, err := portableManifestPaths(dir, entries)
	if err != nil {
		t.Fatal(err)
	}
	a, _ := json.Marshal(paths)
	b, _ := json.Marshal(other)
	if !bytes.Equal(a, b) {
		t.Fatal("manifest order affected mapping")
	}
}

func TestMaskedRarVolumesPreserveDistinctFamilyStems(t *testing.T) {
	remote := "/saved/set"
	names := []string{"bundle.part01.rar删除", "bundle.part02.rar删除", "bundle.part01.rar改", "bundle.part02.rar改",
		"bundle.part01.rar", "bundle.part02.rar", "bundle.part01.rar删除.txt", "bundle.part03.rar删除"}
	entries := []entry{{1, remote, 0, true}}
	for i, name := range names {
		entries = append(entries, entry{int64(i + 2), path.Join(remote, name), 7, i == len(names)-1})
	}
	mapped, err := portableManifestPaths(remote, entries)
	if err != nil {
		t.Fatal(err)
	}
	stem := func(i int) string {
		t.Helper()
		name := path.Base(mapped[path.Join(remote, names[i])])
		suffix := strings.TrimPrefix(names[i], "bundle")
		if !strings.HasSuffix(name, suffix) {
			t.Fatalf("lost suffix %q: %q", suffix, name)
		}
		return strings.TrimSuffix(name, suffix)
	}
	if stem(0) != stem(1) || stem(2) != stem(3) || stem(0) == stem(2) ||
		stem(4) != stem(5) || stem(4) == stem(0) {
		t.Fatalf("wrong archive groups: %v", mapped)
	}
	// Directories and files with further extensions keep their prior family rules.
	old, err := portableManifestPathsForVersion(remote, entries, 1)
	if err != nil {
		t.Fatal(err)
	}
	for _, i := range []int{6, 7} {
		k := path.Join(remote, names[i])
		if mapped[k] != old[k] {
			t.Fatalf("unrelated name changed: %q", k)
		}
	}
}

func TestMaskedRarPathJournalRetainsExactOldNamesAndRejectsModifiedMaps(t *testing.T) {
	root, profile := t.TempDir(), t.TempDir()
	r := request{OperationID: "masked-archive", RemotePath: "/saved/set"}
	entries := []entry{{1, r.RemotePath, 0, true}, {2, r.RemotePath + "/bundle.part1.rar删除", 4, false},
		{3, r.RemotePath + "/bundle.part2.rar删除", 3, false}, {4, r.RemotePath + "/说明.txt", 4, false}}
	current, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, true)
	if err != nil {
		t.Fatal(err)
	}
	journals, _ := filepath.Glob(filepath.Join(profile, "operations", "*.paths-v1.json"))
	if len(journals) != 1 {
		t.Fatal("expected a bound journal")
	}
	newJSON, err := os.ReadFile(journals[0])
	if err != nil {
		t.Fatal(err)
	}
	var journal localPathJournal
	if err := json.Unmarshal(newJSON, &journal); err != nil || journal.Version != 2 {
		t.Fatal("expected current format", err)
	}
	// Literal old names guard against silently changing the historical algorithm
	// together with its compatibility verifier.
	legacy := map[string]string{r.RemotePath: "set", entries[1].Path: "set/bundle.part1~7f3787d40e7eb6ac.rar删除",
		entries[2].Path: "set/bundle.part2~46590b8c80a6b8d5.rar删除", entries[3].Path: "set/说明~f659f45391ad4d0b.txt"}
	journal.Version, journal.Paths = 1, sortedLocalPathPairs(legacy)
	oldJSON, _ := json.Marshal(journal)
	if err := os.WriteFile(journals[0], oldJSON, 0600); err != nil {
		t.Fatal(err)
	}
	local := filepath.Join(root, filepath.FromSlash(legacy[entries[1].Path]))
	if err := os.MkdirAll(filepath.Dir(local), 0700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(local, []byte("data"), 0600); err != nil {
		t.Fatal(err)
	}
	for _, persist := range []bool{false, true} {
		v, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, persist)
		if err != nil || v[entries[1].Path] != legacy[entries[1].Path] || v[entries[2].Path] != legacy[entries[2].Path] ||
			v[entries[1].Path] == current[entries[1].Path] {
			t.Fatal("old map did not resume", err)
		}
	}
	stillOld, _ := os.ReadFile(journals[0])
	if !bytes.Equal(stillOld, oldJSON) {
		t.Fatal("historical journal rewritten")
	}
	for _, version := range []int{1, 2} {
		journal.Version = version
		journal.Paths[1].Relative = "../elsewhere"
		tampered, _ := json.Marshal(journal)
		if err := os.WriteFile(journals[0], tampered, 0600); err != nil {
			t.Fatal(err)
		}
		if _, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, false); err == nil || err.Error() != "BAIDU_LOCAL_PATH_MAPPING_INVALID" {
			t.Fatalf("tampered v%d map accepted: %v", version, err)
		}
	}
	if content, err := os.ReadFile(local); err != nil || string(content) != "data" {
		t.Fatal("original payload changed")
	}
}

func TestPortableManifestDisambiguatesCaseAndUnicodeSiblings(t *testing.T) {
	entries := []entry{{1, "/saved/root/A.txt", 1, false}, {2, "/saved/root/a.txt", 1, false},
		{3, "/saved/root/é.txt", 1, false}, {4, "/saved/root/e\u0301.txt", 1, false},
		{5, "/saved/root/safe.txt", 1, false}, {6, "/saved/root/目录/A", 0, true}, {7, "/saved/root/目錄/A", 0, true}}
	paths, err := portableManifestPaths("/saved/root", entries)
	if err != nil {
		t.Fatal(err)
	}
	seen := map[string]bool{}
	for _, e := range entries {
		local := strings.ToLower(paths[e.Path])
		if seen[local] {
			t.Fatal("collision")
		}
		seen[local] = true
	}
	if paths[entries[4].Path] != "root/safe.txt" {
		t.Fatal("ordinary unique filename changed")
	}
	for _, e := range entries[:4] {
		if !strings.Contains(paths[e.Path], "~") {
			t.Fatal("colliding filename has no stable discriminator")
		}
	}
}

func TestPortableMapInspectionIsReadOnlyAndJournalIsBoundAndNeverReplaced(t *testing.T) {
	root, profile, r, entries := portableFixture(t)
	paths, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, false)
	if err != nil {
		t.Fatal(err)
	}
	if files, _ := os.ReadDir(profile); len(files) != 0 {
		t.Fatal("inspection wrote profile")
	}
	if files, _ := os.ReadDir(root); len(files) != 0 {
		t.Fatal("inspection wrote output")
	}
	persisted, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, true)
	if err != nil {
		t.Fatal(err)
	}
	a, _ := json.Marshal(paths)
	b, _ := json.Marshal(persisted)
	if !bytes.Equal(a, b) {
		t.Fatal("inspection and execution differ")
	}
	journals, _ := filepath.Glob(filepath.Join(profile, "operations", "*.paths-v1.json"))
	if len(journals) != 1 {
		t.Fatal("missing map")
	}
	before, _ := os.ReadFile(journals[0])
	info, _ := os.Stat(journals[0])
	local := filepath.Join(root, filepath.FromSlash(paths[entries[1].Path]))
	if err = os.MkdirAll(filepath.Dir(local), 0700); err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(local, []byte("data"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err = resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, true); err != nil {
		t.Fatal(err)
	}
	if err = inspectLocalDownloadWithPaths(context.Background(), root, r.RemotePath, entries, paths); err != nil {
		t.Fatal(err)
	}
	after, _ := os.ReadFile(journals[0])
	later, _ := os.Stat(journals[0])
	if !bytes.Equal(before, after) || !os.SameFile(info, later) {
		t.Fatal("existing map replaced")
	}
	for _, mode := range []string{"uid", "root", "size", "version"} {
		t.Run(mode, func(t *testing.T) {
			uid, output := "17", root
			changed := append([]entry(nil), entries...)
			switch mode {
			case "uid":
				uid = "18"
			case "root":
				output = root + "-other"
			case "size":
				changed[1].Size++
			case "version":
				var journal localPathJournal
				_ = json.Unmarshal(before, &journal)
				journal.Version = 99
				bad, _ := json.Marshal(journal)
				_ = os.WriteFile(journals[0], bad, 0600)
				defer os.WriteFile(journals[0], before, 0600)
			}
			_, err := resolveDownloadPaths(context.Background(), profile, uid, output, r, changed, true)
			if err == nil || err.Error() != "BAIDU_LOCAL_PATH_MAPPING_INVALID" {
				t.Fatalf("unsafe changed identity accepted: %v", err)
			}
		})
	}
	payload, _ := os.ReadFile(local)
	if string(payload) != "data" {
		t.Fatal("existing payload changed")
	}
}

func TestPortableMapNeverAdoptsUnjournaledPayloadOrChangesLegacyPaths(t *testing.T) {
	t.Run("unrecognized mapped payload", func(t *testing.T) {
		root, profile, r, entries := portableFixture(t)
		paths, err := portableManifestPaths(r.RemotePath, entries)
		if err != nil {
			t.Fatal(err)
		}
		local := filepath.Join(root, filepath.FromSlash(paths[entries[1].Path]))
		_ = os.MkdirAll(filepath.Dir(local), 0700)
		_ = os.WriteFile(local, []byte("data"), 0600)
		_, err = resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, true)
		if err == nil || err.Error() != "BAIDU_LOCAL_PATH_MAPPING_CONFLICT" {
			t.Fatalf("unjournaled payload adopted: %v", err)
		}
		if files, _ := os.ReadDir(profile); len(files) != 0 {
			t.Fatal("conflict created journal")
		}
	})
	t.Run("legacy unicode family", func(t *testing.T) {
		root, profile, r, _ := portableFixture(t)
		entries := []entry{{1, r.RemotePath + "/日本.txt", 4, false}, {2, r.RemotePath + "/中文.txt", 4, false}}
		for _, e := range entries {
			local := filepath.Join(root, "transfer", path.Base(e.Path))
			_ = os.MkdirAll(filepath.Dir(local), 0700)
			_ = os.WriteFile(local, []byte("data"), 0600)
		}
		paths, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, true)
		if err != nil {
			t.Fatal(err)
		}
		for _, e := range entries {
			if paths[e.Path] != "transfer/"+path.Base(e.Path) {
				t.Fatal("legacy file renamed")
			}
		}
		if files, _ := os.ReadDir(profile); len(files) != 0 {
			t.Fatal("legacy map should not be frozen to a new algorithm")
		}
	})
	for _, scenario := range []struct {
		name       string
		checkpoint bool
		incomplete bool
		unexpected bool
		code       string
	}{
		{"unverified partial", false, false, false, "BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED"},
		{"invalid checkpoint", true, false, false, "BAIDU_DOWNLOAD_CHECKPOINT_INVALID"},
		{"invalid checkpoint with another unverified partial", true, true, false, "BAIDU_DOWNLOAD_CHECKPOINT_INVALID"},
		{"partial with unrelated payload", false, false, true, "BAIDU_LOCAL_PATH_MAPPING_CONFLICT"},
		{"invalid checkpoint with unrelated payload", true, false, true, "BAIDU_LOCAL_PATH_MAPPING_CONFLICT"},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			root, profile, r, entries := portableFixture(t)
			entries = append(entries, entry{3, r.RemotePath + "/old.bin", 4, false})
			directory := filepath.Join(root, "transfer")
			if err := os.MkdirAll(directory, 0700); err != nil {
				t.Fatal(err)
			}
			local := filepath.Join(directory, "old.bin")
			if err := os.WriteFile(local, []byte("d"), 0600); err != nil {
				t.Fatal(err)
			}
			if scenario.checkpoint {
				if err := os.WriteFile(local+pcsdownload.DownloadSuffix, nil, 0600); err != nil {
					t.Fatal(err)
				}
			}
			if scenario.incomplete {
				entries = append(entries, entry{4, r.RemotePath + "/incomplete.bin", 4, false})
				if err := os.WriteFile(filepath.Join(directory, "incomplete.bin"), []byte("d"), 0600); err != nil {
					t.Fatal(err)
				}
			}
			if scenario.unexpected {
				if err := os.WriteFile(filepath.Join(directory, "unknown.bin"), []byte("unrelated"), 0600); err != nil {
					t.Fatal(err)
				}
			}
			for _, persist := range []bool{false, true} {
				mapped, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, persist)
				if mapped != nil || err == nil || err.Error() != scenario.code {
					t.Fatalf("expected %s, got %v (persist=%v)", scenario.code, err, persist)
				}
			}
			if b, err := os.ReadFile(local); err != nil || string(b) != "d" {
				t.Fatal("legacy payload was modified")
			}
			if scenario.checkpoint {
				if b, err := os.ReadFile(local + pcsdownload.DownloadSuffix); err != nil || len(b) != 0 {
					t.Fatal("legacy checkpoint was modified")
				}
			}
			if files, err := os.ReadDir(profile); err != nil || len(files) != 0 {
				t.Fatal("failed inspection created a path journal")
			}
		})
	}
}

func TestPortableMapUsesOriginalRemotePathAndReusesVerifiedPayload(t *testing.T) {
	root, profile, r, entries := portableFixture(t)
	paths, err := resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, true)
	if err != nil {
		t.Fatal(err)
	}
	hash := md5.Sum([]byte("data"))
	files := []*baidupcs.FileDirectory{{FsID: 1, Path: r.RemotePath, Isdir: true}, {FsID: 2, Path: entries[1].Path, Size: 4}}
	files[1].BlockList = []string{hex.EncodeToString(hash[:])}
	calls := 0
	execute := func(fd *baidupcs.FileDirectory, local string, _ func(int64)) error {
		calls++
		if fd.Path != entries[1].Path {
			t.Fatal("PcsPath mutated")
		}
		return os.WriteFile(local, []byte("data"), 0600)
	}
	for i := 0; i < 2; i++ {
		result, err := downloadManifestFilesWithPaths(root, r.RemotePath, files, paths, nil, execute, func(int64, int64) {})
		if err != nil {
			t.Fatal(err)
		}
		if len(result) != 1 || result[0] != filepath.Join(root, filepath.FromSlash(paths[entries[1].Path])) {
			t.Fatal("wrong local outputs")
		}
	}
	if calls != 1 {
		t.Fatal("verified payload downloaded twice")
	}
}

func TestLocalPathTooLongIsNotInvalidFile(t *testing.T) {
	local := filepath.Join(t.TempDir(), strings.Repeat("x", 270))
	_, err := verifiedLocalDownload(local, &baidupcs.FileDirectory{Size: 4}, nil)
	if err == nil || err.Error() != "BAIDU_LOCAL_PATH_TOO_LONG" {
		t.Fatalf("incorrect long-name classification: %v", err)
	}
	event := errorEvent(storageFail("BAIDU_DOWNLOAD_FAILED", "download", &os.PathError{Op: "open", Path: "private name", Err: syscall.ENAMETOOLONG}))
	data, _ := json.Marshal(event)
	if strings.Contains(string(data), "private name") || !strings.Contains(string(data), "BAIDU_LOCAL_PATH_TOO_LONG") {
		t.Fatal(string(data))
	}
}

func TestPortableManifestProtectsCheckpointNamespaceAndUnicodeCaseFold(t *testing.T) {
	entries := []entry{{1, "/saved/root/file", 4, false}, {2, "/saved/root/file" + pcsdownload.DownloadSuffix, 4, false},
		{3, "/saved/root/S.txt", 4, false}, {4, "/saved/root/ſ.txt", 4, false}}
	paths, err := portableManifestPaths("/saved/root", entries)
	if err != nil {
		t.Fatal(err)
	}
	seen := map[string]bool{}
	for _, e := range entries {
		for _, p := range []string{paths[e.Path], paths[e.Path] + pcsdownload.DownloadSuffix} {
			folded := localCaseFold(p)
			if seen[folded] {
				t.Fatal("file/checkpoint or Unicode case collision")
			}
			seen[folded] = true
		}
	}
}

func TestExistingCompletionVerifiesAllBytesAndRetainsFiles(t *testing.T) {
	local := filepath.Join(t.TempDir(), "data.bin")
	os.WriteFile(local, []byte("original"), 0600)
	fd := resumeFixture("data.bin", "original")
	expected := entry{fd.FsID, fd.Path, fd.Size, false}
	calls := 0
	result, err := verifyExistingFile(context.Background(), local, expected, func() (*baidupcs.FileDirectory, error) { calls++; return fd, nil })
	digest := sha256.Sum256([]byte("original"))
	if err != nil || result.Sha256 != hex.EncodeToString(digest[:]) || calls != 2 {
		t.Fatalf("result=%+v calls=%d error=%v", result, calls, err)
	}
	contents, _ := os.ReadFile(local)
	if string(contents) != "original" {
		t.Fatal("verification modified payload")
	}
}

func TestExistingCompletionVerifiesProviderBlocksAndTail(t *testing.T) {
	for _, size := range []int{8, 32} {
		t.Run(fmt.Sprintf("%dMiB", size), func(t *testing.T) {
			blockSize := size << 20
			contents := make([]byte, 2*blockSize+23)
			contents[0], contents[blockSize], contents[2*blockSize] = 1, 2, 3
			local := filepath.Join(t.TempDir(), "multipart.bin")
			fd := multipartFixture("multipart.bin", contents, int64(blockSize))
			expected := entry{fd.FsID, fd.Path, fd.Size, false}
			metadata := func() (*baidupcs.FileDirectory, error) { return fd, nil }
			if err := os.WriteFile(local, contents, 0600); err != nil {
				t.Fatal(err)
			}
			result, err := verifyExistingFile(context.Background(), local, expected, metadata)
			digest := sha256.Sum256(contents)
			if err != nil || result.Sha256 != hex.EncodeToString(digest[:]) || result.Length != fd.Size {
				t.Fatalf("valid block list verification failed: length=%d err=%v", result.Length, err)
			}
			for _, offset := range []int{blockSize + 1, len(contents) - 1} {
				// Change one byte without altering the file length or the other blocks.
				contents[offset] ^= 1
				if err := os.WriteFile(local, contents, 0600); err != nil {
					t.Fatal(err)
				}
				result, err = verifyExistingFile(context.Background(), local, expected, metadata)
				if err == nil || err.Error() != "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH" || result.Sha256 != "" {
					t.Fatalf("corruption at offset %d: result=%+v err=%v", offset, result, err)
				}
				var mismatch *existingChecksumMismatch
				if !errors.As(err, &mismatch) || mismatch.blocks != 3 || mismatch.bad != 1 ||
					mismatch.first != offset/blockSize+1 || mismatch.firstBytes != min(int64(blockSize), fd.Size-int64(offset/blockSize*blockSize)) {
					t.Fatalf("wrong block coordinates at offset %d", offset)
				}
				contents[offset] ^= 1
			}
		})
	}
}

func TestExistingCompletionRejectsUnprovenOrChangedFiles(t *testing.T) {
	for _, mode := range []string{"missingHash", "corrupt", "partial", "remoteIdentity", "remoteChanged", "unknownBlocks", "cancel"} {
		t.Run(mode, func(t *testing.T) {
			local := filepath.Join(t.TempDir(), "data.bin")
			data := "original"
			if mode == "corrupt" {
				data = "corrupt!"
			}
			if mode == "partial" {
				data = "part"
			}
			os.WriteFile(local, []byte(data), 0600)
			fd := resumeFixture("data.bin", "original")
			expected := entry{fd.FsID, fd.Path, fd.Size, false}
			if mode == "missingHash" {
				fd.BlockList = nil
			}
			if mode == "remoteIdentity" {
				fd.FsID++
			}
			if mode == "unknownBlocks" {
				fd.BlockList = []string{strings.Repeat("0", 32), strings.Repeat("1", 32)}
			}
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			if mode == "cancel" {
				cancel()
			}
			calls := 0
			result, err := verifyExistingFile(ctx, local, expected, func() (*baidupcs.FileDirectory, error) {
				calls++
				if mode == "remoteChanged" && calls == 2 {
					return resumeFixture("data.bin", "changed!"), nil
				}
				return fd, nil
			})
			if err == nil || result.Sha256 != "" {
				t.Fatalf("unsafe recovery accepted: %+v %v", result, err)
			}
			contents, _ := os.ReadFile(local)
			if string(contents) != data {
				t.Fatal("failed verification modified payload")
			}
		})
	}
}

func TestLegacyDirectoryDownloadResumesBesideSmbDeleteMarker(t *testing.T) {
	const marker = ".smbdeleteAAA59fc4.4"
	root, profile := t.TempDir(), t.TempDir()
	entries := []entry{{1, "/remote", 0, true}, {2, "/remote/甲.zip", 4, false}, {3, "/remote/乙.zip", 4, false}}
	r := request{OperationID: "existing-directory", ExpectedUID: "17", RemotePath: "/remote", OutputDirectory: root, ExpectedManifest: entries}
	payload := filepath.Join(root, "remote")
	if err := os.Mkdir(payload, 0700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(payload, "甲.zip"), []byte("kept"), 0600); err != nil {
		t.Fatal(err)
	}
	sibling := filepath.Join(root, marker)
	if err := os.WriteFile(sibling, []byte("externally held"), 0600); err != nil {
		t.Fatal(err)
	}
	portable, err := portableManifestPaths(r.RemotePath, entries)
	if err != nil || portable[entries[1].Path] == "remote/甲.zip" {
		t.Fatal("fixture did not require a legacy map", err)
	}
	var paths map[string]string
	for _, persist := range []bool{false, true} {
		paths, err = resolveDownloadPaths(context.Background(), profile, "17", root, r, entries, persist)
		if err != nil || paths[entries[1].Path] != "remote/甲.zip" || paths[entries[2].Path] != "remote/乙.zip" {
			t.Fatalf("legacy map: %v", err)
		}
		if err = inspectLocalDownloadWithPaths(context.Background(), root, r.RemotePath, entries, paths); err != nil {
			t.Fatal(err)
		}
	}
	fetched := 0
	files, err := downloadManifestFilesWithPaths(root, r.RemotePath, []*baidupcs.FileDirectory{resumeFixture("甲.zip", "kept"), resumeFixture("乙.zip", "more")}, paths, nil,
		func(fd *baidupcs.FileDirectory, local string, _ func(int64)) error {
			if local != filepath.Join(payload, "乙.zip") {
				t.Fatalf("unexpected transfer %s", local)
			}
			fetched++
			return os.WriteFile(local, []byte("more"), 0600)
		}, func(int64, int64) {})
	if err != nil || fetched != 1 || len(files) != 2 || slices.Contains(files, sibling) {
		t.Fatalf("downloaded %d, returned %v: %v", fetched, files, err)
	}
	if b, err := os.ReadFile(sibling); err != nil || string(b) != "externally held" {
		t.Fatal("marker changed", err)
	}
	if b, err := os.ReadFile(filepath.Join(payload, "甲.zip")); err != nil || string(b) != "kept" {
		t.Fatal("existing file changed", err)
	}
}

func TestDirectoryInspectionKeepsUnexpectedSiblingsAndRealRemoteMarkerNamesProtected(t *testing.T) {
	const marker = ".smbdeleteAAA59fc4.4"
	for _, scenario := range []string{"unrelated", "lookalike", "no-complete-payload", "directory", "symlink", "inside-payload", "remote-same-name", "single-file"} {
		t.Run(scenario, func(t *testing.T) {
			root := t.TempDir()
			dir := filepath.Join(root, "remote")
			if err := os.Mkdir(dir, 0700); err != nil {
				t.Fatal(err)
			}
			entries := []entry{{1, "/remote", 0, true}, {2, "/remote/a", 4, false}}
			if scenario != "no-complete-payload" {
				if err := os.WriteFile(filepath.Join(dir, "a"), []byte("kept"), 0600); err != nil {
					t.Fatal(err)
				}
			}
			remote := "/remote"
			name := filepath.Join(root, marker)
			switch scenario {
			case "unrelated":
				name = filepath.Join(root, ".notes")
			case "lookalike":
				name += ".extra"
			case "inside-payload":
				name = filepath.Join(dir, marker)
			case "remote-same-name":
				entries = append(entries, entry{3, "/remote/" + strings.ToUpper(marker), 1, false})
			case "single-file":
				remote = "/remote/a"
				entries = entries[1:]
			}
			var err error
			switch scenario {
			case "directory":
				err = os.Mkdir(name, 0700)
			case "symlink":
				err = os.Symlink(filepath.Join(dir, "a"), name)
				if err != nil {
					t.Skip("no symlink support")
				}
			default:
				err = os.WriteFile(name, []byte("untouched"), 0600)
			}
			if err != nil {
				t.Fatal(err)
			}
			if err = inspectLocalDownload(context.Background(), root, remote, entries); err == nil {
				t.Fatal("unexpected input was accepted")
			}
			if _, err := os.Lstat(name); err != nil {
				t.Fatal("input changed", err)
			}
		})
	}
	root := t.TempDir()
	name := filepath.Join(root, marker)
	if err := os.WriteFile(name, []byte("short"), 0600); err != nil {
		t.Fatal(err)
	}
	// A source that actually has a marker-shaped name is verified as ordinary payload.
	err := inspectLocalDownload(context.Background(), root, "/"+marker, []entry{{1, "/" + marker, 9, false}})
	if err == nil || err.Error() != "BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED" {
		t.Fatalf("unexpected manifest validation %v", err)
	}
}

func TestRetainedWriteAdmissionRequiresChecksumsAndForbidsRewritingCreditedMembers(t *testing.T) {
	root := t.TempDir()
	files := []*baidupcs.FileDirectory{}
	paths := map[string]string{}
	for i, name := range []string{"missing", "good", "wrong", "short", "unknown"} {
		fd := resumeFixture(name, "data")
		fd.FsID = int64(i + 1)
		files = append(files, fd)
		paths[fd.Path] = name
		if name == "unknown" {
			fd.BlockList = nil
		}
		if name != "missing" {
			data := map[string]string{"good": "data", "wrong": "xxxx", "short": "da", "unknown": "data"}[name]
			if err := os.WriteFile(filepath.Join(root, name), []byte(data), 0600); err != nil {
				t.Fatal(err)
			}
		}
	}
	ids, err := verifiedRetainedFileIDs(root, files, paths, nil, verificationProgress{Context: context.Background()})
	if err != nil || !slices.Equal(ids, []string{"2"}) {
		t.Fatalf("verified ids=%v err=%v", ids, err)
	}
	before, err := os.Stat(filepath.Join(root, "good"))
	if err != nil {
		t.Fatal(err)
	}
	writes := 0
	run := func(readOnly []string) error {
		_, err := downloadManifestFilesWithReadOnly(root, "/remote", files, paths, readOnly, nil,
			func(fd *baidupcs.FileDirectory, local string, report func(int64)) error {
				if fd.FsID == 2 {
					t.Fatal("credited file opened for writing")
				}
				writes++
				return os.WriteFile(local, []byte("data"), 0600)
			}, func(int64, int64) {})
		return err
	}
	if err := run(ids); err != nil || writes != 4 {
		t.Fatalf("ordinary resume writes=%d err=%v", writes, err)
	}
	after, err := os.Stat(filepath.Join(root, "good"))
	if err != nil || !sameLocalFile(before, after) {
		t.Fatal("credited file changed")
	}
	for _, id := range [][]string{{"99"}, {"2", "2"}} {
		if err := run(id); err == nil || writes != 4 {
			t.Fatalf("invalid credit permitted writes: %v %v", id, err)
		}
	}
	if err := os.WriteFile(filepath.Join(root, "good"), []byte("oops"), 0600); err != nil {
		t.Fatal(err)
	}
	if err := run(ids); !errors.Is(err, errLocalFileChanged) || writes != 4 {
		t.Fatalf("corrupt protected member was not stopped before writing: %v", err)
	}
	if err := os.Remove(filepath.Join(root, "good")); err != nil {
		t.Fatal(err)
	}
	if err := run(ids); !errors.Is(err, errLocalFileChanged) || writes != 4 {
		t.Fatalf("missing protected member was not stopped before writing: %v", err)
	}
}

func TestRetainedManifestRejectsChangesAfterChecksum(t *testing.T) {
	for _, tc := range []struct{ action, code string }{
		{"remove", "BAIDU_VERIFIED_FILE_MISSING"}, {"replace", "BAIDU_VERIFIED_FILE_REPLACED"},
		{"link", "BAIDU_VERIFIED_FILE_REPLACED"}, {"truncate", "BAIDU_VERIFIED_FILE_SIZE_CHANGED"},
		{"timestamp", "BAIDU_VERIFIED_FILE_TIME_CHANGED"},
	} {
		t.Run(tc.action, func(t *testing.T) {
			root := t.TempDir()
			file := resumeFixture("retained", "data")
			file.FsID = 51
			abs := filepath.Join(root, "retained")
			if err := os.WriteFile(abs, []byte("data"), 0600); err != nil {
				t.Fatal(err)
			}
			original, _ := os.Lstat(abs)
			results, err := downloadManifestFilesWithReadOnly(root, "/remote", []*baidupcs.FileDirectory{file},
				map[string]string{file.Path: "retained"}, []string{"51"}, nil,
				func(*baidupcs.FileDirectory, string, func(int64)) error {
					t.Fatal("protected writer called")
					return nil
				},
				func(int64, int64) {
					var err error
					switch tc.action {
					case "remove":
						err = os.Remove(abs)
					case "replace":
						replacement := filepath.Join(root, "new-inode")
						if err = os.WriteFile(replacement, []byte("data"), 0600); err == nil {
							err = os.Chtimes(replacement, original.ModTime(), original.ModTime())
						}
						if err == nil {
							err = os.Rename(replacement, abs)
						}
					case "link":
						other := filepath.Join(root, "other")
						if err = os.Rename(abs, other); err == nil {
							err = os.Symlink(other, abs)
						}
					case "truncate":
						err = os.Truncate(abs, 1)
					case "timestamp":
						stamp := original.ModTime().Add(-time.Hour)
						err = os.Chtimes(abs, stamp, stamp)
					}
					if err != nil {
						t.Fatal(err)
					}
				})
			if err == nil || len(results) != 0 {
				t.Fatalf("accepted changed input: %v", err)
			}
			event := errorEvent(err)
			if event["code"] != tc.code || event["manifestFileNumber"] != 1 {
				t.Fatalf("unsafe/uninformative event: %v", event)
			}
			body, _ := json.Marshal(event)
			if strings.Contains(string(body), root) || strings.Contains(string(body), "retained") {
				t.Fatalf("path leaked in event: %s", body)
			}
		})
	}
}

func TestRetainedRecheckClassifiesStatFaultsAndRehashesOnlyOnRecovery(t *testing.T) {
	root := t.TempDir()
	p := filepath.Join(root, "payload")
	original := []byte("data")
	if err := os.WriteFile(p, original, 0600); err != nil {
		t.Fatal(err)
	}
	proof, err := os.Lstat(p)
	if err != nil {
		t.Fatal(err)
	}
	f, err := os.Open(p)
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()
	fd := resumeFixture("payload", string(original))
	for _, tc := range []struct {
		name          string
		fault         error
		fails         int
		expected      string
		stats, hashes int
	}{
		{"healthy", nil, 0, "", 1, 0}, {"io recovers", syscall.EIO, 1, "", 2, 1},
		{"stale twice", syscall.ESTALE, 2, "", 3, 1}, {"persistent timeout", syscall.ETIMEDOUT, 9, "BAIDU_OUTPUT_FILE_READ_FAILED", 3, 0},
		{"permission", syscall.EACCES, 9, "BAIDU_OUTPUT_FILE_READ_FAILED", 1, 0}, {"removed", os.ErrNotExist, 9, "BAIDU_VERIFIED_FILE_MISSING", 1, 0},
		{"cancelled", syscall.EIO, 9, "context canceled", 1, 0}, {"modified bytes with restored timestamp", syscall.EIO, 1, "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH", 2, 1},
	} {
		t.Run(tc.name, func(t *testing.T) {
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			stats, hashes := 0, 0
			err := recheckRetainedFile(ctx, proof, f, func() (os.FileInfo, error) {
				stats++
				if tc.name == "cancelled" {
					cancel()
				}
				if stats <= tc.fails {
					if tc.name == "modified bytes with restored timestamp" {
						if err := os.WriteFile(p, []byte("xxxx"), 0600); err != nil {
							t.Fatal(err)
						}
						if err := os.Chtimes(p, proof.ModTime(), proof.ModTime()); err != nil {
							t.Fatal(err)
						}
					}
					return nil, tc.fault
				}
				return os.Lstat(p)
			}, func() (os.FileInfo, error) { hashes++; return verifiedLocalDownload(p, fd, nil) })
			got := ""
			if err != nil {
				got = err.Error()
			}
			if got != tc.expected || stats != tc.stats || hashes != tc.hashes {
				t.Fatalf("error=%s stats=%d hashes=%d", got, stats, hashes)
			}
			if tc.expected == "BAIDU_OUTPUT_FILE_READ_FAILED" {
				var storage *storageFailure
				if !errors.As(err, &storage) || storage.operation != "recheckVerifiedFile" || !errors.Is(err, tc.fault) {
					t.Fatalf("dropped storage cause: %v", err)
				}
			}
		})
	}
}

func TestVerifiedHandleRemainsReadOnlyAndBelongsToOriginal(t *testing.T) {
	root := t.TempDir()
	file := filepath.Join(root, "member")
	if err := os.WriteFile(file, []byte("data"), 0600); err != nil {
		t.Fatal(err)
	}
	var witness *os.File
	retain := func(f *os.File) { witness = f }
	if info, err := verifyLocalDownload(file, resumeFixture("member", "xxxx"), nil, retain); info != nil || err != nil || witness != nil {
		t.Fatalf("unverified file pinned: %v", err)
	}
	proof, err := verifyLocalDownload(file, resumeFixture("member", "data"), nil, retain)
	if err != nil || witness == nil {
		t.Fatalf("missing verified handle: %v", err)
	}
	defer witness.Close()
	info, err := witness.Stat()
	if err != nil || !sameLocalFile(proof, info) {
		t.Fatalf("witness lost verified identity: %v", err)
	}
	if _, err = witness.Write([]byte("x")); err == nil {
		t.Fatal("witness admitted a write")
	}
	data, err := os.ReadFile(file)
	if err != nil || string(data) != "data" {
		t.Fatalf("verified bytes changed: %v", err)
	}
}

func TestRetainedPrefixHandlesReleaseOnFailureAndBeforeWriter(t *testing.T) {
	// Inspect only this test's private file descriptors, never the user's file paths.
	dir := "/proc/self/fd"
	if _, err := os.Stat(dir); err != nil {
		dir = "/dev/fd"
	}
	if _, err := os.Stat(dir); err != nil {
		t.Skip("descriptor enumeration unavailable")
	}
	for _, cancelAtReport := range []bool{false, true} {
		root := t.TempDir()
		file := filepath.Join(root, "start")
		files := []*baidupcs.FileDirectory{}
		paths := map[string]string{}
		for i, n := range []string{"start", "missing", "late"} {
			fd := resumeFixture(n, "data")
			fd.FsID = int64(i + 1)
			files = append(files, fd)
			paths[fd.Path] = n
			if n != "missing" {
				if err := os.WriteFile(filepath.Join(root, n), []byte("data"), 0600); err != nil {
					t.Fatal(err)
				}
			}
		}
		proof, err := os.Stat(file)
		if err != nil {
			t.Fatal(err)
		}
		count := func() int {
			entries, err := os.ReadDir(dir)
			if err != nil {
				t.Fatal(err)
			}
			n := 0
			for _, entry := range entries {
				info, err := os.Stat(filepath.Join(dir, entry.Name()))
				if err == nil && os.SameFile(info, proof) {
					n++
				}
			}
			return n
		}
		probe, err := os.Open(file)
		if err != nil {
			t.Fatal(err)
		}
		visible := count()
		probe.Close()
		if visible == 0 {
			t.Skip("filesystem does not expose descriptor identity")
		}
		ctx, cancel := context.WithCancel(context.Background())
		reports, writes := 0, 0
		_, err = downloadManifestFilesWithReadOnly(root, "/remote", files, paths, []string{"1", "3"}, nil,
			func(fd *baidupcs.FileDirectory, path string, _ func(int64)) error {
				writes++
				if fd.FsID != 2 || count() != 0 {
					t.Fatal("verified handle held during payload transfer")
				}
				return os.WriteFile(path, []byte("data"), 0600)
			}, func(int64, int64) {
				reports++
				if reports == 1 {
					if count() != 1 {
						t.Fatal("original handle not held at verified acceptance")
					}
					if cancelAtReport {
						cancel()
					}
				}
			}, verificationProgress{Context: ctx})
		cancel()
		if count() != 0 || cancelAtReport && (!errors.Is(err, context.Canceled) || writes != 0) || !cancelAtReport && (err != nil || writes != 1) {
			t.Fatalf("leaked descriptors or writes after cancellation: %v %d", err, writes)
		}
	}
}

func TestLargeRetainedPrefixSurvivesDescriptorLimits(t *testing.T) {
	root := t.TempDir()
	var files []*baidupcs.FileDirectory
	var credits []string
	paths := map[string]string{}
	const count = maxRetainedWitnesses + 11
	for i := 0; i <= count; i++ {
		name := fmt.Sprintf("file-%d", i+1)
		fd := resumeFixture(name, "data")
		fd.FsID = int64(i + 1)
		files = append(files, fd)
		paths[fd.Path] = name
		if i < count {
			credits = append(credits, fmt.Sprint(fd.FsID))
			if err := os.WriteFile(filepath.Join(root, name), []byte("data"), 0600); err != nil {
				t.Fatal(err)
			}
		}
	}
	writes := 0
	results, err := downloadManifestFilesWithReadOnly(root, "/remote", files, paths, credits, nil,
		func(fd *baidupcs.FileDirectory, local string, _ func(int64)) error {
			if fd.FsID != count+1 {
				t.Fatal("verified file was offered to the writer")
			}
			writes++
			return os.WriteFile(local, []byte("data"), 0600)
		}, func(int64, int64) {})
	if err != nil || writes != 1 || len(results) != len(files) {
		t.Fatalf("writes=%d results=%d error=%v", writes, len(results), err)
	}
}

func TestExistingVerificationBesideReleasedNestedCheckpoints(t *testing.T) {
	const marker = ".smbdeleteAAA59fc4.4"
	for _, scenario := range []string{"verified", "nonempty", "unrelated-name", "wrong-parent", "symlink", "directory", "missing-member", "live-checkpoint", "remote-collision"} {
		t.Run(scenario, func(t *testing.T) {
			root, profile := t.TempDir(), t.TempDir()
			t.Setenv(pcsconfig.EnvConfigDir, profile)
			payload := filepath.Join(root, "remote", "albums")
			if err := os.MkdirAll(payload, 0700); err != nil {
				t.Fatal(err)
			}
			members := []entry{{1, "/remote", 0, true}, {2, "/remote/albums", 0, true}, {3, "/remote/albums/甲.zip", 4, false}, {4, "/remote/albums/乙.zip", 4, false}}
			real := filepath.Join(payload, "甲.zip")
			write := func(path string, data []byte) {
				t.Helper()
				if err := os.WriteFile(path, data, 0600); err != nil {
					t.Fatal(err)
				}
			}
			write(real, []byte("kept"))
			if scenario != "missing-member" {
				write(filepath.Join(payload, "乙.zip"), []byte("kept"))
			}
			tombstone := filepath.Join(payload, marker)
			switch scenario {
			case "nonempty":
				write(tombstone, []byte("unexpected"))
			case "directory":
				if err := os.Mkdir(tombstone, 0700); err != nil {
					t.Fatal(err)
				}
			case "symlink":
				if err := os.Symlink(real, tombstone); err != nil {
					t.Skip(err)
				}
			case "unrelated-name":
				write(tombstone+".note", nil)
			case "wrong-parent":
				foreign := filepath.Join(root, "other")
				if err := os.Mkdir(foreign, 0700); err != nil {
					t.Fatal(err)
				}
				write(filepath.Join(foreign, marker), nil)
			case "live-checkpoint":
				write(tombstone, nil)
				write(real+pcsdownload.DownloadSuffix, nil)
			case "remote-collision":
				write(tombstone, nil)
				members = append(members, entry{5, "/remote/" + strings.ToUpper(marker), 1, false})
				write(filepath.Join(root, "remote", strings.ToUpper(marker)), []byte("x"))
			default:
				write(tombstone, nil)
			}
			r := request{Command: "verify-existing", ExpectedUID: "17", OperationID: "read-only", RemotePath: "/remote", OutputDirectory: root, ExpectedManifest: members}
			files, err := verifyExistingDownload(context.Background(), r, nil)
			if scenario != "verified" {
				if err == nil || len(files) != 0 {
					t.Fatalf("unconfirmed recovery: %v %v", files, err)
				}
				return
			}
			if err != nil || len(files) != 2 {
				t.Fatalf("verification: %v %v", files, err)
			}
			for _, file := range files {
				if file.Length != 4 || file.Sha256 != "" || file.Path == tombstone {
					t.Fatalf("unexpected preview member: %+v", file)
				}
			}
			for _, command := range []string{"inspect-download", "download", "inspect-retained-writes"} {
				r.Command = command
				if _, err = resolveDownloadPaths(context.Background(), profile, r.ExpectedUID, root, r, members, false); err == nil {
					t.Fatalf("nested marker enabled %s", command)
				}
			}
			if _, err = os.Stat(tombstone); err != nil {
				t.Fatal(err)
			}
			if entries, err := os.ReadDir(profile); err != nil || len(entries) != 0 {
				t.Fatalf("preview wrote provider state: %v %v", entries, err)
			}
		})
	}
}

func TestExistingMemberMismatchReportsOnlyStableNumericEvidence(t *testing.T) {
	for _, mode := range []string{"single", "unavailable", "unknownBoundary", "missing", "short", "long",
		"replaced", "disappeared", "resized", "modified", "changedProvider", "lostProvider", "metadataReadFailed", "cancelled"} {
		t.Run(mode, func(t *testing.T) {
			ctx, cancel := context.WithCancel(context.Background())
			defer cancel()
			root := t.TempDir()
			front := resumeFixture("first", "good")
			last := resumeFixture("private-member", "keep")
			last.FsID++
			r := request{VerifyRemote: true, OutputDirectory: root, ExpectedManifest: []entry{
				{42, "/remote", 0, true}, {front.FsID, front.Path, front.Size, false},
				{last.FsID, last.Path, last.Size, false}}}
			paths := map[string]string{front.Path: "first", last.Path: "private-member"}
			if err := os.WriteFile(filepath.Join(root, "first"), []byte("good"), 0600); err != nil {
				t.Fatal(err)
			}
			local := filepath.Join(root, "private-member")
			data := "oops"
			switch mode {
			case "short":
				data = "bad"
			case "long":
				data = "extra"
			case "unavailable":
				last.BlockList = nil
			case "unknownBoundary":
				last.BlockList = append(last.BlockList, last.BlockList[0])
			}
			if mode != "missing" {
				if err := os.WriteFile(local, []byte(data), 0600); err != nil {
					t.Fatal(err)
				}
			}
			calls := 0
			result, err := verifyExistingMembers(ctx, r, paths, func(remote string) (*baidupcs.FileDirectory, error) {
				if remote == front.Path {
					return front, nil
				}
				calls++
				if calls == 2 {
					switch mode {
					case "replaced":
						if err := os.Rename(local, local+".old"); err != nil {
							t.Fatal(err)
						}
						if err := os.WriteFile(local, []byte(data), 0600); err != nil {
							t.Fatal(err)
						}
					case "disappeared":
						if err := os.Remove(local); err != nil {
							t.Fatal(err)
						}
					case "resized":
						if err := os.Truncate(local, 2); err != nil {
							t.Fatal(err)
						}
					case "modified":
						now := time.Now().Add(-time.Hour)
						if err := os.Chtimes(local, now, now); err != nil {
							t.Fatal(err)
						}
					case "changedProvider":
						changed := *last
						changed.BlockList = []string{strings.Repeat("1", 32)}
						return &changed, nil
					case "lostProvider":
						changed := *last
						changed.BlockList = nil
						return &changed, nil
					case "metadataReadFailed":
						return nil, errors.New("private path and transport credentials")
					case "cancelled":
						cancel()
					}
				}
				return last, nil
			})
			if err == nil || len(result) != 0 {
				t.Fatalf("expected no partial result: %v", err)
			}
			want := map[string]string{"single": "BAIDU_DOWNLOAD_CHECKSUM_MISMATCH", "unavailable": "BAIDU_EXISTING_CHECKSUM_UNAVAILABLE",
				"unknownBoundary": "BAIDU_EXISTING_CHECKSUM_UNAVAILABLE", "missing": "BAIDU_VERIFIED_FILE_MISSING", "short": "BAIDU_VERIFIED_FILE_SIZE_CHANGED", "long": "BAIDU_VERIFIED_FILE_SIZE_CHANGED",
				"replaced": "BAIDU_VERIFIED_FILE_REPLACED", "disappeared": "BAIDU_VERIFIED_FILE_MISSING", "resized": "BAIDU_VERIFIED_FILE_SIZE_CHANGED",
				"modified": "BAIDU_VERIFIED_FILE_TIME_CHANGED", "changedProvider": "BAIDU_REMOTE_MANIFEST_CHANGED", "lostProvider": "BAIDU_REMOTE_MANIFEST_CHANGED",
				"metadataReadFailed": "BAIDU_MANIFEST_FAILED", "cancelled": context.Canceled.Error()}[mode]
			ev := errorEvent(err)
			if ev["code"] != want || ev["manifestFileNumber"] != 2 || ev["manifestFileCount"] != 2 {
				t.Fatalf("%+v", ev)
			}
			if mode == "single" {
				if len(ev) != 9 || ev["mismatchedBlockCount"] != 1 || ev["firstMismatchedBlockNumber"] != 1 || ev["checksumBlockCount"] != 1 || ev["firstMismatchedBlockBytes"] != int64(4) || calls != 2 {
					t.Fatalf("wrong public mismatch data: %+v", ev)
				}
				info, err := verifiedLocalDownload(local, last, nil)
				if err != nil || info != nil {
					t.Fatalf("normal download behavior changed: %v", err)
				}
			} else if len(ev) != 5 {
				t.Fatalf("unexpected error fields %+v", ev)
			}
		})
	}
}

func TestVerificationRecordsFirstBadBlockAndCompleteCount(t *testing.T) {
	// Scale the block boundary so the 885th of 951 can be tested in-memory.
	data := bytes.Repeat([]byte("ab"), 951)
	data = data[:len(data)-1]
	hashes := make([][]byte, 951)
	for i := range hashes {
		part := data[2*i : min(2*i+2, len(data))]
		sum := md5.Sum(part)
		hashes[i] = sum[:]
	}
	data[884*2] ^= 1
	cases := []struct {
		count  int
		second bool
	}{{1, false}, {2, true}}
	for _, tc := range cases {
		if tc.second {
			data[len(data)-1] ^= 1
		}
		mismatch := &existingChecksumMismatch{blocks: 951}
		reader := newVerificationReader(bytes.NewReader(data), int64(len(data)), verificationProgress{mismatch: mismatch})
		n, ok, err := verifyChecksumBlocks(reader, &downloadChecksums{blockSize: 2, hashes: hashes})
		if err != nil || ok || n != int64(len(data)) || mismatch.bad != tc.count || mismatch.first != 885 || mismatch.firstBytes != 2 {
			t.Fatalf("wrong block evidence: %+v %v", mismatch, err)
		}
	}
}

func TestVerifyOnlyLayoutOrdersSizeEvidenceAfterStructuralConflicts(t *testing.T) {
	entries := []entry{{1, "/remote", 0, true}, {2, "/remote/first", 4, false}, {3, "/remote/last", 5, false}}
	for _, state := range []string{"missing", "short", "long", "invalidCheckpoint", "unexpected", "symlink"} {
		t.Run(state, func(t *testing.T) {
			root := t.TempDir()
			dir := filepath.Join(root, "remote")
			if err := os.Mkdir(dir, 0700); err != nil {
				t.Fatal(err)
			}
			if err := os.WriteFile(filepath.Join(dir, "first"), []byte("good"), 0600); err != nil {
				t.Fatal(err)
			}
			last := filepath.Join(dir, "last")
			if state != "missing" {
				contents := "bad"
				if state == "long" {
					contents = "too-long"
				}
				if err := os.WriteFile(last, []byte(contents), 0600); err != nil {
					t.Fatal(err)
				}
			}
			if state == "invalidCheckpoint" {
				if err := os.WriteFile(last+pcsdownload.DownloadSuffix, []byte("broken"), 0600); err != nil {
					t.Fatal(err)
				}
			}
			if state == "unexpected" {
				if err := os.WriteFile(filepath.Join(dir, "unknown"), []byte("user"), 0600); err != nil {
					t.Fatal(err)
				}
			}
			if state == "symlink" {
				if err := os.Symlink(last, filepath.Join(dir, "link")); err != nil {
					t.Skip("no symlink support")
				}
			}
			err := inspectDownloadTree(context.Background(), root, "/remote", entries, nil, true)
			want := map[string]string{"missing": "BAIDU_VERIFIED_FILE_MISSING", "short": "BAIDU_VERIFIED_FILE_SIZE_CHANGED", "long": "BAIDU_VERIFIED_FILE_SIZE_CHANGED", "invalidCheckpoint": "BAIDU_DOWNLOAD_CHECKPOINT_INVALID", "unexpected": "BAIDU_OUTPUT_UNEXPECTED_FILE", "symlink": "BAIDU_UNSAFE_REMOTE_PATH"}[state]
			if err == nil || err.Error() != want {
				t.Fatalf("unexpected %v", err)
			}
			event := errorEvent(err)
			if state == "missing" || state == "short" || state == "long" {
				if event["manifestFileNumber"] != 2 || event["manifestFileCount"] != 2 {
					t.Fatalf("bad file ordinal %+v", event)
				}
			}
		})
	}
}

type failingVerificationRead struct{}

func (failingVerificationRead) Read(b []byte) (int, error) { return copy(b, "bad"), syscall.EIO }
func TestVerificationCannotConcludeMismatchAfterDeviceReadError(t *testing.T) {
	match := md5.Sum([]byte("yes"))
	mismatch := &existingChecksumMismatch{blocks: 1}
	reader := newVerificationReader(failingVerificationRead{}, 3, verificationProgress{mismatch: mismatch})
	_, ok, err := verifyChecksumBlocks(reader, &downloadChecksums{blockSize: 3, hashes: [][]byte{match[:]}})
	if ok || !errors.Is(err, syscall.EIO) || mismatch.bad != 0 {
		t.Fatalf("accepted partial evidence: %v %v", ok, err)
	}
}
