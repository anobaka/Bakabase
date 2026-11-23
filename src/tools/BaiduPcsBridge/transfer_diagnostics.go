package main

import (
	"bytes"
	"crypto/tls"
	"net/http"
	"net/url"

	"encoding/json"
	"github.com/qjfoidnh/BaiduPCS-Go/requester"
	"io"
	"sort"
)

const (
	transferCountRejected     = "BAIDU_TRANSFER_FILE_COUNT_LIMIT"
	transferConflictRejected  = "BAIDU_TRANSFER_NAME_CONFLICT"
	transferItemsRejected     = "BAIDU_TRANSFER_ITEMS_REJECTED"
	transferCountUnconfirmed  = "BAIDU_TRANSFER_COUNT_LIMIT_CONFIRM_TARGET"
	transferNameUnconfirmed   = "BAIDU_TRANSFER_CONFLICT_CONFIRM_TARGET"
	transferUnconfirmed       = "BAIDU_TRANSFER_PROVIDER_UNCONFIRMED"
	transferOutputUnconfirmed = "BAIDU_TRANSFER_OUTPUT_UNCONFIRMED"
	transferPartial           = "BAIDU_TRANSFER_PARTIAL_OR_PENDING"
	transferMissingResponse   = "BAIDU_TRANSFER_RESPONSE_UNAVAILABLE"
	transferInvalidResponse   = "BAIDU_TRANSFER_RESPONSE_INVALID"
	maxDiagnosticPayload      = 8 << 20
	maxDiagnosticCount        = 100000000
)

// This is the entire diagnostics wire/disk contract. Paths, the response body, errors,
// Baidu identities and the opaque asynchronous task ID are deliberately never recorded.
type transferDiagnostic struct {
	Code        string  `json:"code"`
	Errno       *int32  `json:"errno,omitempty"`
	ItemErrnos  []int32 `json:"itemErrnos,omitempty"`
	FileCount   int     `json:"fileCount,omitempty"`
	FileLimit   int     `json:"fileLimit,omitempty"`
	Requested   int     `json:"requested,omitempty"`
	Rejected    int     `json:"rejected,omitempty"`
	NoAsyncTask bool    `json:"noAsyncTask,omitempty"`
}

func (d *transferDiagnostic) Error() string { return d.Code }
func (d *transferDiagnostic) confirmedRejection() bool {
	return d.Code == transferCountRejected || d.Code == transferConflictRejected || d.Code == transferItemsRejected
}

func (d *transferDiagnostic) valid() bool {
	if d == nil || len(d.ItemErrnos) > 8 || d.Requested < 0 || d.Requested > maxDiagnosticCount || d.Rejected < 0 || d.Rejected > d.Requested || d.FileCount < 0 || d.FileCount > maxDiagnosticCount || d.FileLimit < 0 || d.FileLimit > maxDiagnosticCount {
		return false
	}
	if d.Errno != nil && !boundedTransferErrno(*d.Errno) {
		return false
	}
	for i, n := range d.ItemErrnos {
		if !boundedTransferErrno(n) || (i > 0 && n <= d.ItemErrnos[i-1]) {
			return false
		}
	}
	switch d.Code {
	case transferCountRejected, transferCountUnconfirmed:
		if d.Errno == nil || *d.Errno != 12 || d.FileLimit == 0 || d.FileCount <= d.FileLimit {
			return false
		}
	case transferConflictRejected, transferNameUnconfirmed:
		if d.Errno == nil || *d.Errno != 12 || len(d.ItemErrnos) != 1 || d.ItemErrnos[0] != -30 {
			return false
		}
	case transferOutputUnconfirmed:
		if d.Errno == nil || *d.Errno != 0 || d.Requested == 0 {
			return false
		}
	case transferItemsRejected, transferUnconfirmed, transferPartial:
		if d.Errno == nil {
			return false
		}
	case transferMissingResponse, transferInvalidResponse:
		return d.Errno == nil && d.Requested == 0 && d.Rejected == 0 && d.FileCount == 0 && d.FileLimit == 0 && len(d.ItemErrnos) == 0 && !d.NoAsyncTask
	default:
		return false
	}
	return !d.confirmedRejection() || (d.Errno != nil && *d.Errno != 0 && d.NoAsyncTask && d.Requested > 0 && d.Requested == d.Rejected && len(d.ItemErrnos) > 0 && !containsTransferSuccess(d.ItemErrnos))
}

func validTransferDiagnostic(d *transferDiagnostic, stage string) bool {
	return d.valid() && ((stage == "rejected" && d.confirmedRejection()) || (stage == "transferring" && !d.confirmedRejection()))
}
func containsTransferSuccess(values []int32) bool {
	for _, n := range values {
		if n == 0 {
			return true
		}
	}
	return false
}
func boundedTransferErrno(value int32) bool { return value >= -999999 && value <= 999999 }

// Duplicate protocol fields can contradict an earlier success or pending task.
// Unknown vendor fields may include account and file metadata; never summarize them.
func transferFields(raw []byte, keys ...string) (map[string]json.RawMessage, bool) {
	reader := json.NewDecoder(bytes.NewReader(raw))
	token, err := reader.Token()
	if err != nil || token != json.Delim('{') {
		return nil, false
	}
	fields := make(map[string]json.RawMessage, len(keys))
	for reader.More() {
		token, err = reader.Token()
		if err != nil {
			return nil, false
		}
		key, ok := token.(string)
		if !ok {
			return nil, false
		}
		var value json.RawMessage
		if reader.Decode(&value) != nil {
			return nil, false
		}
		for _, allowed := range keys {
			if key == allowed {
				if _, duplicate := fields[key]; duplicate {
					return nil, false
				}
				fields[key] = value
				break
			}
		}
	}
	return fields, true
}

func transferNumber(raw json.RawMessage) (int64, bool) {
	var value *int64
	if len(raw) == 0 || json.Unmarshal(raw, &value) != nil || value == nil {
		return 0, false
	}
	return *value, true
}

func officialTransferURL(u *url.URL) bool {
	return u != nil && u.Scheme == "https" && u.Host == "pan.baidu.com" && u.User == nil && u.EscapedPath() == "/share/transfer" && u.Fragment == ""
}

// Keep the configured route and account jar. Authenticate this mutation's origin with
// standard TLS and refuse redirects, including redirects that would replay a POST.
func secureTransferTransport(client *requester.HTTPClient, endpoint string) (func(), error) {
	u, err := url.Parse(endpoint)
	original, ok := client.Transport.(*http.Transport)
	if err != nil || !officialTransferURL(u) || !ok {
		return nil, fail("BAIDU_SHARE_RESPONSE_INVALID")
	}
	secure := original.Clone()
	if secure.TLSClientConfig == nil {
		secure.TLSClientConfig = &tls.Config{}
	} else {
		secure.TLSClientConfig = secure.TLSClientConfig.Clone()
	}
	secure.TLSClientConfig.InsecureSkipVerify = false
	secure.TLSClientConfig.ServerName = ""
	redirect := client.CheckRedirect
	client.Transport = secure
	client.CheckRedirect = func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }
	return func() { client.Transport = original; client.CheckRedirect = redirect; secure.CloseIdleConnections() }, nil
}

func recordTransferResponse(journal string, state *receipt, ids []int64, submit func(func(*http.Response))) error {
	diagnostic := &transferDiagnostic{Code: transferMissingResponse}
	submit(func(response *http.Response) {
		diagnostic = &transferDiagnostic{Code: transferInvalidResponse}
		if response == nil || response.Body == nil || response.StatusCode != http.StatusOK || response.Request == nil || response.Request.Method != http.MethodPost ||
			!officialTransferURL(response.Request.URL) || response.TLS == nil || len(response.TLS.VerifiedChains) == 0 {
			return
		}
		diagnostic = readTransferDiagnostic(response.Body, ids)
	})
	if diagnostic != nil {
		state.TransferDiagnostic = diagnostic
		if diagnostic.confirmedRejection() {
			state.Stage = "rejected"
		}
		if err := saveReceipt(journal, state); err != nil {
			return err
		}
		remoteMutationStarted = !diagnostic.confirmedRejection()
		return diagnostic
	}
	// Acknowledgement alone does not establish what is in the destination. Keep that
	// distinction across interruption until the authoritative remote manifest is saved.
	zero := int32(0)
	state.TransferDiagnostic = &transferDiagnostic{Code: transferOutputUnconfirmed, Errno: &zero, Requested: len(ids)}
	return saveReceipt(journal, state)
}

func readTransferDiagnostic(r io.Reader, ids []int64) *transferDiagnostic {
	raw, err := io.ReadAll(io.LimitReader(r, maxDiagnosticPayload+1))
	if err != nil {
		return &transferDiagnostic{Code: transferMissingResponse}
	}
	invalid := &transferDiagnostic{Code: transferInvalidResponse}
	if len(raw) > maxDiagnosticPayload || !json.Valid(raw) {
		return invalid
	}
	f, ok := transferFields(raw, "errno", "task_id", "taskid", "info", "target_file_nums", "target_file_nums_limit")
	if !ok {
		return invalid
	}
	official, valid := transferNumber(f["errno"])
	if !valid || official < -999999 || official > 999999 || len(ids) > maxDiagnosticCount {
		return invalid
	}
	errno := int32(official)
	d := &transferDiagnostic{Code: transferUnconfirmed, Errno: &errno, Requested: len(ids)}
	async, present := transferNumber(f["task_id"])
	if _, has := f["task_id"]; has && (!present || async < 0) {
		return invalid
	}
	if other, exists := f["taskid"]; exists {
		value, valid := transferNumber(other)
		if !valid || value < 0 || (present && async != value) {
			return invalid
		}
		async, present = value, true
	}
	d.NoAsyncTask = present && async == 0
	for key, field := range map[string]*int{"target_file_nums": &d.FileCount, "target_file_nums_limit": &d.FileLimit} {
		if value, exists := f[key]; exists {
			n, good := transferNumber(value)
			if !good || n < 0 || n > maxDiagnosticCount {
				return invalid
			}
			*field = int(n)
		}
	}
	var info []json.RawMessage
	if rawInfo, exists := f["info"]; exists {
		if json.Unmarshal(rawInfo, &info) != nil || bytes.Equal(bytes.TrimSpace(rawInfo), []byte("null")) {
			return invalid
		}
	}
	requested := make(map[int64]bool, len(ids))
	for _, id := range ids {
		if id <= 0 || requested[id] {
			return invalid
		}
		requested[id] = true
	}
	rejected := make(map[int64]bool)
	observed := make(map[int32]bool)
	anySuccess, anyFailure := false, false
	matches := len(info) > 0 && len(info) == len(ids)
	for _, item := range info {
		fields, valid := transferFields(item, "fsid", "fs_id", "errno")
		if !valid {
			return invalid
		}
		id, hasID := transferNumber(fields["fsid"])
		if _, exists := fields["fsid"]; exists && !hasID {
			return invalid
		}
		if other, exists := fields["fs_id"]; exists {
			candidate, valid := transferNumber(other)
			if !valid || (hasID && id != candidate) {
				return invalid
			}
			id, hasID = candidate, true
		}
		n, hasErr := transferNumber(fields["errno"])
		if !hasErr {
			if _, exists := fields["errno"]; exists {
				return invalid
			}
			matches = false
			continue
		}
		if n < -999999 || n > 999999 {
			return invalid
		}
		observed[int32(n)] = true
		if n == 0 {
			anySuccess = true
			matches = false
			continue
		}
		anyFailure = true
		if !hasID || !requested[id] || rejected[id] {
			matches = false
			continue
		}
		rejected[id] = true
	}
	d.Rejected = len(rejected)
	for n := range observed {
		d.ItemErrnos = append(d.ItemErrnos, n)
	}
	sort.Slice(d.ItemErrnos, func(i, j int) bool { return d.ItemErrnos[i] < d.ItemErrnos[j] })
	if len(d.ItemErrnos) > 8 {
		d.ItemErrnos = d.ItemErrnos[:8]
	}
	if async > 0 || (anySuccess && official != 0) || (anyFailure && official == 0) {
		d.Code = transferPartial
		return d
	}
	if official == 0 {
		return nil
	} // The caller must still verify the actual remote output.
	complete := d.NoAsyncTask && matches && len(rejected) == len(ids)
	switch {
	case official == 12 && d.FileLimit > 0 && d.FileCount > d.FileLimit:
		d.Code = transferCountUnconfirmed
		if complete {
			d.Code = transferCountRejected
		}
	case official == 12 && len(observed) == 1 && observed[-30]:
		d.Code = transferNameUnconfirmed
		if complete {
			d.Code = transferConflictRejected
		}
	case complete:
		d.Code = transferItemsRejected
	}
	return d
}
