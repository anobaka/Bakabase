package main

import (
	"crypto/sha256"
	"crypto/tls"
	"crypto/x509"
	"encoding/hex"
	"encoding/json"
	"github.com/qjfoidnh/BaiduPCS-Go/requester"
	"net/http"
	"net/url"
	"os"
	"path/filepath"

	"errors"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsconfig"
	"io"
	"strings"
	"testing"
)

func TestTransferResultUsesOriginalErrnoAndAllRequestedIDs(t *testing.T) {
	for _, c := range []struct {
		name, body, code string
		confirmed        bool
	}{
		{"accepted asynchronous alternate spelling", `{"errno":0,"taskid":88}`, transferPartial, false},
		{"conflicting async markers", `{"errno":12,"task_id":0,"taskid":88,"info":[{"errno":-30,"fsid":4},{"errno":-30,"fsid":8}]}`, transferInvalidResponse, false},
		{"accepted", "{\"errno\":0}", "", false},
		{"synchronous receipt", `{"errno":0,"task_id":0,"info":[{"fsid":4,"path":"secret"}]}`, "", false},
		{"all rejected", `{"errno":12,"task_id":0,"info":[{"fsid":4,"errno":123},{"fsid":8,"errno":17}]}`, transferItemsRejected, true},
		{"all conflict", `{"errno":12,"task_id":0,"info":[{"fsid":4,"errno":-30},{"fs_id":8,"errno":-30}]}`, transferConflictRejected, true},
		{"proven count", `{"errno":12,"task_id":0,"target_file_nums":100,"target_file_nums_limit":2,"info":[{"fsid":4,"errno":-30},{"fsid":8,"errno":-30}]}`, transferCountRejected, true},
		{"count but uncertain", `{"errno":12,"target_file_nums":100,"target_file_nums_limit":2}`, transferCountUnconfirmed, false},
		{"mixed result", `{"errno":12,"task_id":0,"info":[{"fsid":4,"errno":-30},{"fsid":8,"errno":0}]}`, transferPartial, false},
		{"successful code mixed items", `{"errno":0,"info":[{"fsid":4,"errno":-30}]}`, transferPartial, false},
		{"accepted asynchronous", `{"errno":0,"task_id":123456789}`, transferPartial, false},
		{"unknown response errno", `{"errno":39837,"message":"secret"}`, transferUnconfirmed, false},
		{"missing task id", `{"errno":12,"info":[{"fsid":8,"errno":-30},{"fsid":4,"errno":-30}]}`, transferNameUnconfirmed, false},
		{"wrong source id", `{"errno":12,"task_id":0,"info":[{"fsid":9,"errno":-30},{"fsid":4,"errno":-30}]}`, transferNameUnconfirmed, false},
		{"duplicated item", `{"errno":12,"task_id":0,"info":[{"fsid":8,"errno":-30},{"fsid":8,"errno":-30}]}`, transferNameUnconfirmed, false},
		{"absent item errno", `{"errno":12,"task_id":0,"info":[{"fsid":8,"errno":-30},{"fsid":4}]}`, transferNameUnconfirmed, false},
		{"missing errno", `{"info":[{"errno":0}]}`, transferInvalidResponse, false},
		{"wrong errno type", `{"errno":"0"}`, transferInvalidResponse, false},
		{"explicit null errno", `{"errno":null}`, transferInvalidResponse, false},
		{"missing code cannot mean success", `[]`, transferInvalidResponse, false},
		{"repeated top level", `{"errno":0,"errno":12}`, transferInvalidResponse, false},
		{"repeated sub-item", `{"errno":12,"info":[{"fsid":4,"errno":0,"errno":-30}]}`, transferInvalidResponse, false},
		{"ambiguous source id", `{"errno":12,"info":[{"fsid":4,"fs_id":8,"errno":-30}]}`, transferInvalidResponse, false},
		{"out of range vendor code", `{"errno":9223372036854775807}`, transferInvalidResponse, false},
		{"malformed after POST", `{"errno":0`, transferInvalidResponse, false},
		{"post with multiple documents", `{"errno":0} {"errno":12}`, transferInvalidResponse, false},
	} {
		t.Run(c.name, func(t *testing.T) {
			d := readTransferDiagnostic(strings.NewReader(c.body), []int64{4, 8})
			if c.code == "" {
				if d != nil {
					t.Fatal(d)
				}
				return
			}
			if d == nil || !d.valid() || d.Code != c.code || d.confirmedRejection() != c.confirmed {
				t.Fatalf("unexpected %+v", d)
			}
			stage := "transferring"
			if c.confirmed {
				stage = "rejected"
			}
			if !validTransferDiagnostic(d, stage) {
				t.Fatal("cannot reopen evidence")
			}
		})
	}
}
func TestProviderTransferDiagnosticRedactsUntrustedFields(t *testing.T) {
	const private = "SHOULD_NEVER_PERSIST"
	d := readTransferDiagnostic(strings.NewReader(`{"errno":12,"info":[{"errno":-30,"path":"SHOULD_NEVER_PERSIST","msg":"SHOULD_NEVER_PERSIST"}],"extra":"SHOULD_NEVER_PERSIST","target_file_nums":29,"target_file_nums_limit":20,"task_id":0}`), []int64{127853486612})
	wire, _ := json.Marshal(errorEvent(d))
	journal, _ := json.Marshal(receipt{Stage: "transferring", TransferDiagnostic: d})
	for _, payload := range [][]byte{wire, journal} {
		for _, x := range []string{private, "127853486612", "extra", "task_id", "msg"} {
			if strings.Contains(string(payload), x) {
				t.Fatal("unexpected output", x)
			}
		}
		for _, x := range []string{`"errno":12`, `"itemErrnos":[-30]`, `"fileCount":29`, `"fileLimit":20`} {
			if !strings.Contains(string(payload), x) {
				t.Fatal("missing diagnostic", x)
			}
		}
	}
}

type failedTransferReader struct{}

func (failedTransferReader) Read([]byte) (int, error) {
	return 0, errors.New("private transport detail")
}
func TestTransferUnreadableOrOversizedHasNoOutcome(t *testing.T) {
	if d := readTransferDiagnostic(failedTransferReader{}, []int64{1}); d.Code != transferMissingResponse || d.confirmedRejection() {
		t.Fatal(d)
	}
	if d := readTransferDiagnostic(io.LimitReader(strings.NewReader(strings.Repeat(" ", maxDiagnosticPayload+5)), maxDiagnosticPayload+5), []int64{1}); d.Code != transferInvalidResponse || d.confirmedRejection() {
		t.Fatal(d)
	}
}

func TestSubmittedTransferPersistsAndReplaysOnlySafeEvidence(t *testing.T) {
	for _, c := range []struct{ name, body, code, stage string }{
		{"complete denial", `{"errno":12,"task_id":0,"info":[{"fsid":1,"path":"PRIVATE_FILE_TEST","errno":-30}]}`, transferConflictRejected, "rejected"},
		{"partial evidence", `{"errno":12,"info":[{"fsid":1,"errno":-30,"path":"PRIVATE_FILE_TEST"}]}`, transferNameUnconfirmed, "transferring"},
		{"no response", "", "BAIDU_TRANSFER_RESPONSE_UNAVAILABLE", "transferring"},
		{"async queued", `{"errno":0,"task_id":783412341,"path":"PRIVATE_FILE_TEST"}`, transferPartial, "transferring"},
	} {
		t.Run(c.name, func(t *testing.T) {
			t.Setenv(pcsconfig.EnvConfigDir, t.TempDir())
			r := request{OperationID: "TEST-OP", AccessCode: "abcd", URL: "https://pan.baidu.com/s/1fake", RemoteDirectory: "/owned"}
			key := sha256.Sum256([]byte(r.OperationID))
			sum := sha256.Sum256([]byte(r.URL + "\x00" + r.AccessCode + "\x00" + r.RemoteDirectory))
			dir := filepath.Join(os.Getenv(pcsconfig.EnvConfigDir), "operations")
			if err := os.Mkdir(dir, 0700); err != nil {
				t.Fatal(err)
			}
			file := filepath.Join(dir, hex.EncodeToString(key[:])+".json")
			state := &receipt{Stage: "transferring", Fingerprint: hex.EncodeToString(sum[:]), RemotePath: "/owned/bakabase-" + hex.EncodeToString(key[:])[:24]}
			if err := saveReceipt(file, state); err != nil {
				t.Fatal(err)
			}
			count := 0
			remoteMutationStarted = true
			err := recordTransferResponse(file, state, []int64{1}, func(visit func(*http.Response)) {
				count++
				if c.body != "" {
					visit(transferHTTPFixture(c.body))
				}
			})
			if count != 1 || err == nil || err.Error() != c.code || state.Stage != c.stage {
				t.Fatalf("%v %+v", err, state)
			}
			bytes, readErr := os.ReadFile(file)
			if readErr != nil || strings.Contains(string(bytes), "PRIVATE_FILE_TEST") || strings.Contains(string(bytes), "783412341") {
				t.Fatal("unsafe journal")
			}
			remoteMutationStarted = false
			replayed := transferShare(r, nil) // Must return the saved response without calling PCS again.
			if replayed == nil || replayed.Error() != c.code || remoteMutationStarted != (c.stage != "rejected") {
				t.Fatalf("%v", replayed)
			}
			after, err := os.ReadFile(file)
			if err != nil || string(bytes) != string(after) {
				t.Fatal("replay mutated the receipt")
			}
		})
	}
}
func TestFailedReceiptReplacementPreservesTheExistingObject(t *testing.T) {
	dir := t.TempDir()
	target := filepath.Join(dir, "existing")
	if err := os.Mkdir(target, 0700); err != nil {
		t.Fatal(err)
	}
	if err := saveReceipt(target, &receipt{Stage: "transferring"}); err == nil {
		t.Fatal("replaced unexpected object")
	}
	info, err := os.Stat(target)
	if err != nil || !info.IsDir() {
		t.Fatalf("existing directory lost: %v", err)
	}
	rows, err := os.ReadDir(dir)
	if err != nil || len(rows) != 1 {
		t.Fatal("private staging left behind")
	}
}

func TestAcceptedTransferWaitsForAnIndependentlyVerifiedRemoteManifest(t *testing.T) {
	file := filepath.Join(t.TempDir(), "response.json")
	state := &receipt{Stage: "transferring"}
	err := recordTransferResponse(file, state, []int64{9}, func(read func(*http.Response)) { read(transferHTTPFixture(`{"errno":0}`)) })
	if err != nil {
		t.Fatal(err)
	}
	var saved receipt
	payload, err := os.ReadFile(file)
	if err != nil || json.Unmarshal(payload, &saved) != nil {
		t.Fatal(err)
	}
	if !validTransferDiagnostic(saved.TransferDiagnostic, saved.Stage) || *saved.TransferDiagnostic.Errno != 0 || saved.TransferDiagnostic.Code != transferOutputUnconfirmed || saved.TransferDiagnostic.confirmedRejection() {
		t.Fatalf("%+v", saved)
	}
}

func transferHTTPFixture(body string) *http.Response {
	u, _ := url.Parse("https://pan.baidu.com/share/transfer")
	return &http.Response{StatusCode: http.StatusOK, Request: &http.Request{Method: http.MethodPost, URL: u}, Body: io.NopCloser(strings.NewReader(body)), TLS: &tls.ConnectionState{VerifiedChains: [][]*x509.Certificate{{{}}}}}
}
func TestTransferResponseRequiresVerifiedOfficialEndpoint(t *testing.T) {
	for _, c := range []struct {
		name   string
		change func(*http.Response)
	}{
		{"login redirect", func(r *http.Response) { r.Request.URL.Path = "/login" }},
		{"other host", func(r *http.Response) { r.Request.URL.Host = "other.invalid" }},
		{"plaintext", func(r *http.Response) { r.Request.URL.Scheme = "http" }},
		{"non-success HTTP", func(r *http.Response) { r.StatusCode = 503 }},
		{"redirected GET", func(r *http.Response) { r.Request.Method = "GET" }},
		{"untrusted TLS", func(r *http.Response) { r.TLS.VerifiedChains = nil }},
	} {
		t.Run(c.name, func(t *testing.T) {
			resp := transferHTTPFixture(`{"errno":12,"task_id":0,"info":[{"fsid":1,"errno":-30}]}`)
			c.change(resp)
			state := &receipt{Stage: "transferring"}
			err := recordTransferResponse(filepath.Join(t.TempDir(), "receipt"), state, []int64{1}, func(read func(*http.Response)) { read(resp) })
			if err == nil || err.Error() != transferInvalidResponse || state.Stage != "transferring" {
				t.Fatal("accepted unauthenticated outcome", err)
			}
		})
	}
}
func TestTransferRouteKeepsAccountConnectionAndNeverFollowsARedirect(t *testing.T) {
	c := requester.NewHTTPClient()
	original := &http.Transport{TLSClientConfig: &tls.Config{InsecureSkipVerify: true}}
	c.Transport = original
	if _, err := secureTransferTransport(c, "https://other.invalid/share/transfer"); err == nil || c.Transport != original {
		t.Fatal("unexpected destination accepted")
	}
	restore, err := secureTransferTransport(c, "https://pan.baidu.com/share/transfer")
	if err != nil {
		t.Fatal(err)
	}
	if c.Transport.(*http.Transport).TLSClientConfig.InsecureSkipVerify || c.CheckRedirect(nil, nil) != http.ErrUseLastResponse {
		t.Fatal("transfer did not enforce official HTTPS origin")
	}
	restore()
	if c.Transport != original || c.CheckRedirect != nil {
		t.Fatal("previous client configuration changed")
	}
}
