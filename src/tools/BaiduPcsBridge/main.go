// Bakabase JSON bridge. Build this directory inside the pinned BaiduPCS-Go module.
// The upstream Apache-2.0 license is retained beside the combined executable.
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
	"net/http/cookiejar"
	"net/url"
	"os"
	"os/signal"
	"path"
	"path/filepath"
	"regexp"
	"runtime"
	"slices"
	"sort"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
	"unicode"
	"unicode/utf8"

	"github.com/golang/protobuf/proto"
	"github.com/qjfoidnh/BaiduPCS-Go/baidupcs"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsconfig"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsfunctions/pcsdownload"
	"github.com/qjfoidnh/BaiduPCS-Go/pcsutil/taskframework"
	"github.com/qjfoidnh/BaiduPCS-Go/pcsverbose"
	"github.com/qjfoidnh/BaiduPCS-Go/requester"
	"github.com/qjfoidnh/BaiduPCS-Go/requester/downloader"
	"github.com/qjfoidnh/BaiduPCS-Go/requester/transfer"
	"github.com/tidwall/gjson"
)

const version = "4.0.2+bakabase.15"

type request struct {
	Command            string  `json:"command"`
	Cookies            string  `json:"cookies"`
	BDUSS              string  `json:"bduss"`
	STOKEN             string  `json:"stoken"`
	OperationID        string  `json:"operationId"`
	URL                string  `json:"url"`
	AccessCode         string  `json:"accessCode"`
	RemoteDirectory    string  `json:"remoteDirectory"`
	RemotePath         string  `json:"remotePath"`
	OutputDirectory    string  `json:"outputDirectory"`
	ExpectedManifest   []entry `json:"expectedManifest"`
	ExpectedUID        string  `json:"expectedUid"`
	VerifyRemote       bool    `json:"verifyRemote"`
	ConnectionsPerFile int     `json:"connectionsPerFile"`
}
type entry struct {
	ID          int64  `json:"id"`
	Path        string `json:"path"`
	Size        int64  `json:"size"`
	IsDirectory bool   `json:"isDirectory"`
}
type receipt struct {
	Fingerprint string  `json:"fingerprint"`
	Stage       string  `json:"stage"`
	RemotePath  string  `json:"remotePath"`
	Manifest    []entry `json:"manifest"`
}

var output *json.Encoder
var outputMutex sync.Mutex
var remoteMutationStarted bool

func emit(v any)             { outputMutex.Lock(); defer outputMutex.Unlock(); _ = output.Encode(v) }
func fail(code string) error { return errors.New(code) }
func main() {
	if len(os.Args) == 2 && os.Args[1] == "--version" {
		fmt.Println(version)
		return
	}
	// Upstream prints filenames and errors freely; keep these out of both the wire
	// protocol and server logs. Only explicitly constructed events leave this process.
	wire := os.Stdout
	output = json.NewEncoder(wire)
	null, err := os.OpenFile(os.DevNull, os.O_WRONLY, 0)
	if err != nil {
		os.Exit(1)
	}
	os.Stdout, os.Stderr = null, null
	defer func() {
		if recover() != nil {
			emit(map[string]any{"type": "error", "code": "BAIDU_BRIDGE_FAILURE", "mayHaveSideEffects": remoteMutationStarted})
			os.Exit(1)
		}
	}()
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	err = run(ctx)
	if err != nil {
		emit(errorEvent(err))
		os.Exit(1)
	}
}
func run(ctx context.Context) error {
	var r request
	if json.NewDecoder(io.LimitReader(os.Stdin, 1024*1024)).Decode(&r) != nil {
		return fail("BAIDU_INVALID_REQUEST")
	}
	// A separate anonymous process: never initializes or saves an account profile,
	// takes a download lock, creates a receipt, transfers a share or fetches payloads.
	if r.Command == "probe-share" {
		client := newShareProbeRequester(ctx)
		defer client.client.CloseIdleConnections()
		if err := probeShare(r, client); err != nil {
			return err
		}
		emit(map[string]any{"type": "result", "code": "BAIDU_SHARE_METADATA_AVAILABLE"})
		return nil
	}
	if !filepath.IsAbs(os.Getenv(pcsconfig.EnvConfigDir)) {
		return fail("BAIDU_CONFIG_DIRECTORY_REQUIRED")
	}
	if r.Command == "download" || r.Command == "membership" || r.Command == "manifest" {
		pcs, uid, err := inspectionPCS(os.Getenv(pcsconfig.EnvConfigDir), r.ExpectedUID)
		if err != nil {
			return err
		}
		if r.Command == "download" {
			return download(ctx, r, pcs)
		}
		if r.Command == "manifest" {
			if !path.IsAbs(r.RemotePath) || path.Clean(r.RemotePath) != r.RemotePath {
				return fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
			}
			entries, _, err := manifestLimited(pcs, r.RemotePath, 10000)
			if err != nil {
				return err
			}
			emit(map[string]any{"type": "result", "uid": uid, "manifest": entries})
			return nil
		}
		client := newMembershipRequester(ctx, pcs)
		defer client.client.CloseIdleConnections()
		kind, err := probeMembership(client)
		if err != nil {
			return err
		}
		emit(map[string]any{"type": "result", "uid": uid, "membershipKind": kind, "probeMethod": "baidu-xpan-uinfo"})
		return nil
	}
	if r.Command == "verify-existing" {
		pcs, uid, err := inspectionPCS(os.Getenv(pcsconfig.EnvConfigDir), r.ExpectedUID)
		if err != nil {
			return err
		}
		files, err := verifyExistingDownload(ctx, r, pcs)
		if err != nil {
			return err
		}
		emit(map[string]any{"type": "result", "uid": uid, "files": files})
		return nil
	}
	if r.Command == "inspect-download" {
		pcs, uid, err := inspectionPCS(os.Getenv(pcsconfig.EnvConfigDir), r.ExpectedUID)
		if err != nil {
			return err
		}
		if err = inspectDownload(ctx, r, pcs); err != nil {
			return err
		}
		emit(map[string]any{"type": "result", "canResume": true, "uid": uid})
		return nil
	}
	if pcsconfig.Config.Init() != nil {
		return fail("BAIDU_CONFIG_INVALID")
	}
	defer pcsconfig.Config.Close()
	if r.Command == "login" {
		bduss, stoken := r.BDUSS, r.STOKEN
		for _, part := range strings.Split(r.Cookies, ";") {
			key, value, ok := strings.Cut(strings.TrimSpace(part), "=")
			if !ok {
				continue
			}
			value = strings.Trim(value, "\"")
			if key == "BDUSS" {
				bduss = value
			}
			if key == "STOKEN" {
				stoken = value
			}
		}
		if bduss == "" || stoken == "" {
			return fail("BAIDU_CREDENTIALS_REQUIRED")
		}
		user, err := pcsconfig.Config.SetupUserByBDUSS(bduss, "", stoken, r.Cookies)
		if err != nil || user == nil || user.UID == 0 {
			return fail("BAIDU_LOGIN_FAILED")
		}
		if _, _, err := pcsconfig.Config.ActiveUserBaiduPCS().QuotaInfo(); err != nil {
			return fail("BAIDU_LOGIN_FAILED")
		}
		if pcsconfig.Config.Save() != nil {
			return fail("BAIDU_CONFIG_WRITE_FAILED")
		}
		emit(map[string]any{"type": "result", "uid": strconv.FormatUint(user.UID, 10), "name": user.Name})
		return nil
	}
	if pcsconfig.Config.ActiveUser().UID == 0 {
		return fail("BAIDU_LOGIN_REQUIRED")
	}
	pcs := pcsconfig.Config.ActiveUserBaiduPCS()
	switch r.Command {
	case "validate":
		current := pcsconfig.Config.ActiveUser()
		online, err := pcsconfig.NewUserInfoByBDUSS(current.BDUSS)
		if err != nil || online == nil || online.UID != current.UID {
			return fail("BAIDU_SESSION_EXPIRED")
		}
		if _, _, err := pcs.QuotaInfo(); err != nil {
			return fail("BAIDU_SESSION_EXPIRED")
		}
		emit(map[string]any{"type": "result", "uid": strconv.FormatUint(pcsconfig.Config.ActiveUser().UID, 10), "name": pcsconfig.Config.ActiveUser().Name})
		return nil
	case "transfer":
		return transferShare(r, pcs)
	default:
		return fail("BAIDU_UNKNOWN_COMMAND")
	}
}

// Baidu's published XPan user-info schema exposes vip_type (0/1/2). This
// capability uses the existing web session, without obtaining/refreshing OAuth
// tokens. Providers which reject cookie authentication remain unknown. Never
// infer membership from quota, file size, speed or a missing field's zero value.
const membershipURL = "https://pan.baidu.com/rest/2.0/xpan/nas?method=uinfo"
const maximumMembershipResponseBytes = 64 * 1024

func newMembershipRequester(ctx context.Context, pcs *baidupcs.BaiduPCS) *shareProbeRequester {
	client := newShareProbeRequester(ctx)
	client.client.Jar = pcs.GetClient().Jar
	// Do not send account cookies across redirects, even to another Baidu host.
	client.client.CheckRedirect = func(req *http.Request, via []*http.Request) error { return http.ErrUseLastResponse }
	return client
}

func probeMembership(client shareRequester) (string, error) {
	response, err := client.Req(http.MethodGet, membershipURL, nil, map[string]string{
		"Accept": "application/json", "User-Agent": "Mozilla/5.0", "Referer": "https://pan.baidu.com/",
	})
	if err != nil {
		return "", fail("BAIDU_MEMBERSHIP_NETWORK_ERROR")
	}
	if response == nil || response.Body == nil {
		return "", fail("BAIDU_MEMBERSHIP_RESPONSE_INVALID")
	}
	defer response.Body.Close()
	if response.StatusCode != http.StatusOK || response.Request == nil || response.Request.URL == nil ||
		response.Request.URL.String() != membershipURL {
		return "", fail("BAIDU_MEMBERSHIP_NETWORK_ERROR")
	}
	body, err := io.ReadAll(io.LimitReader(response.Body, maximumMembershipResponseBytes+1))
	if err != nil {
		return "", fail("BAIDU_MEMBERSHIP_NETWORK_ERROR")
	}
	return membershipKind(body)
}

func membershipKind(body []byte) (string, error) {
	if len(body) > maximumMembershipResponseBytes {
		return "", fail("BAIDU_MEMBERSHIP_RESPONSE_INVALID")
	}
	var response struct {
		Errno   *int   `json:"errno"`
		VIPType *int   `json:"vip_type"`
		UK      *int64 `json:"uk"`
	}
	if json.Unmarshal(body, &response) != nil || response.Errno == nil {
		return "", fail("BAIDU_MEMBERSHIP_RESPONSE_INVALID")
	}
	if *response.Errno == -6 {
		return "", fail("BAIDU_SESSION_EXPIRED")
	}
	if *response.Errno != 0 {
		return "", fail("BAIDU_MEMBERSHIP_UNKNOWN")
	}
	if response.VIPType == nil || response.UK == nil || *response.UK <= 0 {
		return "", fail("BAIDU_MEMBERSHIP_RESPONSE_INVALID")
	}
	// Netdisk UK and the account's Baidu UID are different identifiers. The host
	// binds expectedUID to the profile, not to this unrelated user-info field.
	switch *response.VIPType {
	case 0:
		return "free", nil
	case 1:
		return "vip", nil
	case 2:
		return "svip", nil
	default:
		return "", fail("BAIDU_MEMBERSHIP_UNKNOWN")
	}
}

const directoryPageSize = 1000
const directoryMaxPages = 2000

// The upstream aggregate list silently stops on duplicate IDs or its page cap.
// Use its public single-page transport and validate pagination ourselves.
func completePages(fetch func(int) ([]*baidupcs.FileDirectory, error), pageSize, pageLimit int) ([]*baidupcs.FileDirectory, error) {
	var all []*baidupcs.FileDirectory
	seen := map[int64]bool{}
	for page := 1; page <= pageLimit; page++ {
		items, err := fetch(page)
		if err != nil {
			return nil, err
		}
		if len(items) > pageSize {
			return nil, fail("BAIDU_DIRECTORY_PAGINATION_INVALID")
		}
		for _, item := range items {
			if item == nil || item.FsID <= 0 || seen[item.FsID] {
				return nil, fail("BAIDU_DIRECTORY_PAGINATION_INVALID")
			}
			seen[item.FsID] = true
			all = append(all, item)
		}
		if len(items) < pageSize {
			return all, nil
		}
	}
	return nil, fail("BAIDU_DIRECTORY_PAGE_LIMIT")
}

func listDirectory(pcs *baidupcs.BaiduPCS, root string) ([]*baidupcs.FileDirectory, error) {
	return completePages(func(page int) ([]*baidupcs.FileDirectory, error) {
		body, err := pcs.PrepareFilesDirectoriesList(root, baidupcs.DefaultOrderOptions, directoryPageSize, page)
		if err != nil {
			return nil, fail("BAIDU_DIRECTORY_LIST_FAILED")
		}
		defer body.Close()
		return decodeDirectoryPage(body, root)
	}, directoryPageSize, directoryMaxPages)
}

func decodeDirectoryPage(body io.Reader, root string) ([]*baidupcs.FileDirectory, error) {
	var response struct {
		Errno *int `json:"errno"`
		List  []struct {
			ID          int64  `json:"fs_id"`
			Path        string `json:"path"`
			Name        string `json:"server_filename"`
			Size        int64  `json:"size"`
			IsDirectory int    `json:"isdir"`
			MD5         string `json:"md5"`
			Ctime       int64  `json:"server_ctime"`
			Mtime       int64  `json:"server_mtime"`
			baidupcs.BlockListJSON
		} `json:"list"`
	}
	if json.NewDecoder(io.LimitReader(body, 16*1024*1024)).Decode(&response) != nil || response.Errno == nil || *response.Errno != 0 || response.List == nil {
		return nil, fail("BAIDU_DIRECTORY_LIST_FAILED")
	}
	items := make([]*baidupcs.FileDirectory, 0, len(response.List))
	for _, item := range response.List {
		if item.Path != path.Clean(item.Path) || path.Base(item.Path) != item.Name || path.Dir(path.Clean(item.Path)) != path.Clean(root) || item.Size < 0 || (item.IsDirectory != 0 && item.IsDirectory != 1) {
			return nil, fail("BAIDU_DIRECTORY_LIST_INVALID")
		}
		fd := &baidupcs.FileDirectory{FsID: item.ID, Path: item.Path, Filename: item.Name, Size: item.Size, Isdir: item.IsDirectory == 1, MD5: item.MD5, Ctime: item.Ctime, Mtime: item.Mtime, BlockListJSON: item.BlockListJSON}
		if len(fd.BlockList) == 1 {
			fd.MD5 = fd.BlockList[0]
		}
		items = append(items, fd)
	}
	return items, nil
}

func manifest(pcs *baidupcs.BaiduPCS, root string) ([]entry, []*baidupcs.FileDirectory, error) {
	return manifestLimited(pcs, root, 2000000)
}
func manifestLimited(pcs *baidupcs.BaiduPCS, root string, limit int) ([]entry, []*baidupcs.FileDirectory, error) {
	first, err := pcs.FilesDirectoriesMeta(root)
	if err != nil || first == nil {
		return nil, nil, fail("BAIDU_MANIFEST_FAILED")
	}
	files := []*baidupcs.FileDirectory{first}
	seen := map[int64]bool{first.FsID: true}
	for i := 0; i < len(files); i++ {
		if !files[i].Isdir {
			continue
		}
		children, err := listDirectory(pcs, files[i].Path)
		if err != nil {
			return nil, nil, err
		}
		for _, child := range children {
			if seen[child.FsID] {
				return nil, nil, fail("BAIDU_MANIFEST_DUPLICATE_ID")
			}
			seen[child.FsID] = true
			files = append(files, child)
		}
		if len(files) > limit {
			return nil, nil, fail("BAIDU_MANIFEST_TOO_LARGE")
		}
	}
	entries := make([]entry, 0, len(files))
	for _, fd := range files {
		entries = append(entries, entry{fd.FsID, fd.Path, fd.Size, fd.Isdir})
	}
	return entries, files, nil
}
func saveReceipt(file string, r *receipt) error {
	b, _ := json.Marshal(r)
	if os.WriteFile(file+".new", b, 0600) != nil {
		return fail("BAIDU_RECEIPT_WRITE_FAILED")
	}
	// Windows cannot replace an existing file with Rename. The previous intent must
	// remain on disk until the replacement is ready; host serializes each account.
	if err := os.Rename(file+".new", file); err != nil {
		if os.Remove(file) != nil || os.Rename(file+".new", file) != nil {
			return fail("BAIDU_RECEIPT_WRITE_FAILED")
		}
	}
	return nil
}

// Keep the account's existing cookie jar, proxy and HTTP behavior. In particular,
// the final response URL must survive: modern error pages contain login locals
// but no share identity, so loginstate alone cannot prove a share is available.
type shareRequester interface {
	Req(method, url string, post interface{}, header map[string]string) (*http.Response, error)
}

const maximumShareResponseBytes = 2 * 1024 * 1024

type shareProbeRequester struct {
	client  *http.Client
	context context.Context
}

var errShareProbeNetworkPolicy = errors.New("BAIDU_SHARE_NETWORK_REQUIRES_PROXY")

func shareRequestError(err error) error {
	if errors.Is(err, errShareProbeNetworkPolicy) {
		return errShareProbeNetworkPolicy
	}
	return fail("BAIDU_SHARE_NETWORK_ERROR")
}

func newShareProbeRequester(ctx context.Context) *shareProbeRequester {
	return newShareProbeRequesterWithDialer(ctx, (&net.Dialer{Timeout: 10 * time.Second}).DialContext)
}

func newShareProbeRequesterWithDialer(ctx context.Context, dial func(context.Context, string, string) (net.Conn, error)) *shareProbeRequester {
	jar, _ := cookiejar.New(nil)
	transport := http.DefaultTransport.(*http.Transport).Clone()
	transport.ResponseHeaderTimeout = 10 * time.Second
	// Req and every redirect accept only the two fixed official HTTPS hosts below.
	// Let the configured OS resolver/TUN route those trusted hosts (including fake-IP
	// DNS). TLS still authenticates the original hostname. This transport must never
	// be used for arbitrary user-provided URLs or payload download destinations.
	transport.DialContext = dial
	// The upstream general-purpose client disables certificate verification; this
	// metadata-only client deliberately keeps the standard TLS/SNI checks.
	return &shareProbeRequester{client: &http.Client{Transport: transport, Jar: jar, Timeout: 15 * time.Second,
		CheckRedirect: func(req *http.Request, via []*http.Request) error {
			if len(via) >= 5 || !safeShareProbeURL(req.URL) {
				return fail("BAIDU_SHARE_PAGE_INVALID")
			}
			return nil
		}}, context: ctx}
}

func safeShareProbeURL(u *url.URL) bool {
	return u != nil && u.Scheme == "https" && u.User == nil &&
		(strings.EqualFold(u.Host, "pan.baidu.com") || strings.EqualFold(u.Host, "yun.baidu.com"))
}

func (p *shareProbeRequester) Req(method, raw string, post interface{}, headers map[string]string) (*http.Response, error) {
	u, err := url.Parse(raw)
	if err != nil || !safeShareProbeURL(u) {
		return nil, fail("BAIDU_SHARE_PAGE_INVALID")
	}
	if method != http.MethodGet && (method != http.MethodPost || u.Path != "/share/verify") {
		return nil, fail("BAIDU_PROBE_MUTATION_FORBIDDEN")
	}
	var body io.Reader
	if values, ok := post.(map[string]string); ok {
		form := url.Values{}
		for key, value := range values {
			form.Set(key, value)
		}
		body = strings.NewReader(form.Encode())
	}
	req, err := http.NewRequestWithContext(p.context, method, raw, body)
	if err != nil {
		return nil, err
	}
	for key, value := range headers {
		req.Header.Set(key, value)
	}
	return p.client.Do(req)
}

func probeShare(r request, client shareRequester) error {
	u, err := url.Parse(r.URL)
	if err != nil || !safeShareProbeURL(u) {
		return fail("BAIDU_SHARE_URL_INVALID")
	}
	feature := ""
	if strings.HasPrefix(u.Path, "/s/") {
		feature = strings.TrimPrefix(strings.TrimSuffix(u.Path, "/"), "/s/")
	}
	if u.Path == "/share/init" {
		feature = "1" + u.Query().Get("surl")
	}
	if !regexp.MustCompile(`^1[A-Za-z0-9_-]{1,22}$`).MatchString(feature) {
		return fail("BAIDU_SHARE_URL_INVALID")
	}
	tokens, err := accessSharePageContext(client, feature, true, false)
	if err != nil {
		return err
	}
	code := r.AccessCode
	if code == "" {
		code = u.Query().Get("pwd")
	}
	if code != "" {
		if !regexp.MustCompile(`^[A-Za-z0-9]{4}$`).MatchString(code) {
			return fail("BAIDU_ACCESS_CODE_REQUIRED")
		}
		query := url.Values{"shareid": {tokens["shareid"]}, "surl": {feature[1:]}, "uk": {tokens["share_uk"]}, "clienttype": {"1"}}
		if err := verifyShare(client, "https://pan.baidu.com/share/verify?"+query.Encode(), r.URL, code, tokens["bdstoken"]); err != nil {
			return err
		}
		tokens, err = accessSharePageContext(client, feature, false, false)
		if err != nil {
			return err
		}
	}
	query := url.Values{"shareid": {tokens["shareid"]}, "uk": {tokens["share_uk"]}, "bdstoken": {tokens["bdstoken"]},
		"root": {"1"}, "web": {"5"}, "app_id": {baidupcs.PanAppID}, "shorturl": {feature[1:]}, "channel": {"chunlei"}, "num": {"10"}, "page": {"1"}}
	response, err := client.Req(http.MethodGet, "https://pan.baidu.com/share/list?"+query.Encode(), nil, map[string]string{"User-Agent": baidupcs.NetdiskUA})
	if err != nil {
		return shareRequestError(err)
	}
	data, err := readShareBody(response)
	if err != nil {
		return err
	}
	var listing struct {
		Errno *int              `json:"errno"`
		List  []json.RawMessage `json:"list"`
	}
	if json.Unmarshal(data, &listing) != nil || listing.Errno == nil {
		return fail("BAIDU_SHARE_RESPONSE_INVALID")
	}
	if *listing.Errno != 0 {
		if code == "" {
			return fail("BAIDU_ACCESS_CODE_REQUIRED")
		}
		return fail("BAIDU_SHARE_LIST_FAILED")
	}
	if len(listing.List) == 0 {
		return fail("BAIDU_SHARE_EMPTY")
	}
	return nil
}

var shareLocalsPattern = regexp.MustCompile(`(\{.+?loginstate.+?\})`)
var shareLocalsAssignmentPattern = regexp.MustCompile(`(?:window\.)?locals\s*(?:=\s*|\.mset\(\s*)`)
var shareIdentityPattern = regexp.MustCompile(`^[0-9]+$`)

func readShareBody(resp *http.Response) ([]byte, error) {
	if resp == nil || resp.Body == nil {
		return nil, fail("BAIDU_SHARE_RESPONSE_INVALID")
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fail("BAIDU_SHARE_NETWORK_ERROR")
	}
	body, err := io.ReadAll(io.LimitReader(resp.Body, maximumShareResponseBytes+1))
	if err != nil {
		return nil, fail("BAIDU_SHARE_NETWORK_ERROR")
	}
	if len(body) > maximumShareResponseBytes {
		return nil, fail("BAIDU_SHARE_RESPONSE_INVALID")
	}
	return body, nil
}

func sharePageProblem(final *url.URL) string {
	if final == nil || final.Scheme != "https" || final.User != nil ||
		(!strings.EqualFold(final.Host, "pan.baidu.com") && !strings.EqualFold(final.Host, "yun.baidu.com")) {
		return "BAIDU_SHARE_PAGE_INVALID"
	}
	if p := strings.TrimSuffix(final.Path, "/"); p == "/wap/error" || p == "/share/error" {
		return sharePageErrorTypeProblem(final.Query().Get("errortype"))
	}
	return ""
}

func sharePageErrorTypeProblem(errorType string) string {
	// Baidu's official error-new ERROR_TIP_MAP distinguishes unavailable shares
	// from temporary/access restrictions. Never infer a terminal state from the
	// localized title, a generic errno, or the map's unknown-type fallback text.
	// Reviewed against disk-share errorNew-all_5fd2b89.js on 2026-10-26.
	switch errorType {
	case "0":
		return "BAIDU_SHARE_DELETED"
	case "1":
		return "BAIDU_SHARE_CANCELLED"
	case "3":
		return "BAIDU_SHARE_BLOCKED"
	case "5":
		return "BAIDU_SHARE_NOT_FOUND"
	case "10":
		return "BAIDU_SHARE_EXPIRED"
	case "11":
		return "BAIDU_SHARE_VISIT_LIMIT_INVALIDATED"
	case "2", "12", "15":
		return "BAIDU_SHARE_TEMPORARILY_UNAVAILABLE"
	case "17":
		return "BAIDU_SHARE_ACCESS_RESTRICTED"
	case "123":
		return "BAIDU_SHARE_VISITOR_LIMIT_REACHED"
	case "124":
		return "BAIDU_SHARE_FROZEN"
	default:
		return "BAIDU_SHARE_UNAVAILABLE"
	}
}

func shareLocalsProblem(locals []byte) string {
	if gjson.GetBytes(locals, "share_page_type").String() != "error" {
		return ""
	}
	errorType := gjson.GetBytes(locals, "errortype")
	switch errorType.Type {
	case gjson.Number:
		// Keep the JSON representation: fractions/exponents must not be rounded
		// or coerced into a known error type.
		return sharePageErrorTypeProblem(errorType.Raw)
	case gjson.String:
		return sharePageErrorTypeProblem(errorType.Str)
	default:
		return "BAIDU_SHARE_UNAVAILABLE"
	}
}

func accessSharePage(client shareRequester, feature string, first bool) (map[string]string, error) {
	return accessSharePageContext(client, feature, first, true)
}

func accessSharePageContext(client shareRequester, feature string, first bool, requireAccountToken bool) (map[string]string, error) {
	headers := map[string]string{"User-Agent": baidupcs.NetdiskUA, "Referer": "https://pan.baidu.com/disk/home"}
	if !first {
		headers["Referer"] = "https://pan.baidu.com/share/init?surl=" + url.QueryEscape(feature[1:])
	}
	resp, err := client.Req(http.MethodGet, "https://pan.baidu.com/s/"+feature, nil, headers)
	if err != nil {
		if resp != nil && resp.Body != nil {
			resp.Body.Close()
		}
		return nil, shareRequestError(err)
	}
	if resp == nil || resp.Request == nil {
		if resp != nil && resp.Body != nil {
			resp.Body.Close()
		}
		return nil, fail("BAIDU_SHARE_RESPONSE_INVALID")
	}
	if problem := sharePageProblem(resp.Request.URL); problem != "" {
		if resp.Body != nil {
			resp.Body.Close()
		}
		return nil, fail(problem)
	}
	body, err := readShareBody(resp)
	if err != nil {
		return nil, err
	}
	var locals []byte
	// Read the actual JSON assignment rather than the first enclosing JavaScript
	// function brace; anonymous /wap/init pages wrap locals in a try block.
	for _, position := range shareLocalsAssignmentPattern.FindAllIndex(body, -1) {
		var candidate json.RawMessage
		if json.NewDecoder(bytes.NewReader(body[position[1]:])).Decode(&candidate) == nil {
			// The final HTTPS host was validated above. Official desktop error
			// pages keep /s/... in the URL and carry their reason only in locals;
			// error locals need not include account/login metadata.
			if problem := shareLocalsProblem(candidate); problem != "" {
				return nil, fail(problem)
			}
			if locals == nil && gjson.GetBytes(candidate, "loginstate").Exists() {
				locals = candidate
			}
		}
	}
	if locals == nil {
		if match := shareLocalsPattern.FindSubmatch(body); len(match) >= 2 {
			locals = match[1]
		}
	}
	if locals == nil {
		if strings.Contains(string(body), "platform-non-found") || strings.Contains(string(body), "error-404") {
			return nil, fail("BAIDU_SHARE_UNAVAILABLE")
		}
		return nil, fail("BAIDU_SHARE_PAGE_INVALID")
	}
	tokens := make(map[string]string)
	for _, key := range []string{"bdstoken", "uk", "share_uk", "shareid"} {
		tokens[key] = gjson.GetBytes(locals, key).String()
	}
	if requireAccountToken && tokens["bdstoken"] == "" || !shareIdentityPattern.MatchString(tokens["shareid"]) ||
		!shareIdentityPattern.MatchString(tokens["share_uk"]) {
		return nil, fail("BAIDU_SHARE_PAGE_INVALID")
	}
	return tokens, nil
}

func verifyShare(client shareRequester, verifyURL, referer, code, token string) error {
	resp, err := client.Req(http.MethodPost, verifyURL,
		map[string]string{"pwd": code, "vcode": "null", "vcode_str": "null", "bdstoken": token},
		map[string]string{"User-Agent": requester.UserAgent,
			"Content-Type": "application/x-www-form-urlencoded; charset=UTF-8", "Referer": referer})
	if err != nil {
		if resp != nil && resp.Body != nil {
			resp.Body.Close()
		}
		return shareRequestError(err)
	}
	if resp == nil || resp.Request == nil {
		if resp != nil && resp.Body != nil {
			resp.Body.Close()
		}
		return fail("BAIDU_SHARE_RESPONSE_INVALID")
	}
	if problem := sharePageProblem(resp.Request.URL); problem != "" {
		if resp.Body != nil {
			resp.Body.Close()
		}
		return fail(problem)
	}
	body, err := readShareBody(resp)
	if err != nil {
		return err
	}
	var result struct {
		Errno  *int   `json:"errno"`
		Randsk string `json:"randsk"`
	}
	if json.Unmarshal(body, &result) != nil || result.Errno == nil {
		return fail("BAIDU_SHARE_RESPONSE_INVALID")
	}
	switch *result.Errno {
	case 0:
		if strings.TrimSpace(result.Randsk) == "" {
			return fail("BAIDU_SHARE_RESPONSE_INVALID")
		}
		return nil
	case -9:
		return fail("BAIDU_SHARE_ACCESS_CODE_REJECTED")
	case 8001:
		return fail("BAIDU_SHARE_VERIFICATION_REQUIRED")
	default:
		// -12 is also returned for incomplete share context; it does not prove
		// an incorrect access code or that a captcha alone would solve it.
		return fail("BAIDU_SHARE_VERIFICATION_FAILED")
	}
}

func transferShare(r request, pcs *baidupcs.BaiduPCS) error {
	if r.OperationID == "" || !strings.HasPrefix(r.RemoteDirectory, "/") {
		return fail("BAIDU_TRANSFER_TARGET_REQUIRED")
	}
	sum := sha256.Sum256([]byte(r.OperationID))
	key := hex.EncodeToString(sum[:])
	dir := filepath.Join(os.Getenv(pcsconfig.EnvConfigDir), "operations")
	if os.MkdirAll(dir, 0700) != nil {
		return fail("BAIDU_RECEIPT_WRITE_FAILED")
	}
	fingerprint := sha256.Sum256([]byte(r.URL + "\x00" + r.AccessCode + "\x00" + r.RemoteDirectory))
	journal := filepath.Join(dir, key+".json")
	state := &receipt{Fingerprint: hex.EncodeToString(fingerprint[:]), Stage: "prepared", RemotePath: path.Join(r.RemoteDirectory, "bakabase-"+key[:24])}
	if b, err := os.ReadFile(journal); err == nil {
		var old receipt
		if json.Unmarshal(b, &old) != nil || old.Fingerprint != state.Fingerprint {
			remoteMutationStarted = true
			return fail("BAIDU_OPERATION_MISMATCH")
		}
		if old.Stage == "completed" {
			emit(map[string]any{"type": "result", "receipt": old})
			return nil
		}
		if old.Stage != "prepared" {
			remoteMutationStarted = true
			return fail("BAIDU_TRANSFER_CONFIRMATION_REQUIRED")
		}
	} else if !os.IsNotExist(err) {
		return fail("BAIDU_RECEIPT_READ_FAILED")
	}
	u, err := url.Parse(r.URL)
	if err != nil || (u.Scheme != "https" && u.Scheme != "http") || u.User != nil ||
		(!strings.EqualFold(u.Hostname(), "pan.baidu.com") && !strings.EqualFold(u.Hostname(), "yun.baidu.com")) {
		return fail("BAIDU_SHARE_URL_INVALID")
	}
	feature := path.Base(u.Path)
	if feature == "init" {
		feature = "1" + u.Query().Get("surl")
	}
	if !regexp.MustCompile(`^1[A-Za-z0-9_-]{1,22}$`).MatchString(feature) {
		return fail("BAIDU_SHARE_URL_INVALID")
	}
	code := r.AccessCode
	if code == "" {
		code = u.Query().Get("pwd")
	}
	if !regexp.MustCompile(`^[A-Za-z0-9]{4}$`).MatchString(code) {
		return fail("BAIDU_ACCESS_CODE_REQUIRED")
	}
	tokens, err := accessSharePage(pcs.GetClient(), feature, true)
	if err != nil {
		return err
	}
	verifyURL := pcs.GenerateShareQueryURL("verify", map[string]string{"shareid": tokens["shareid"], "time": strconv.FormatInt(time.Now().UnixMilli(), 10), "clienttype": "1", "uk": tokens["share_uk"]}).String()
	if err := verifyShare(pcs.GetClient(), verifyURL, r.URL, code, tokens["bdstoken"]); err != nil {
		return err
	}
	pcs.UpdatePCSCookies(true)
	tokens, err = accessSharePage(pcs.GetClient(), feature, false)
	if err != nil {
		return err
	}
	var ids []int64
	var params map[string]string
	seen := map[int64]bool{}
	for page := 1; page <= 10000; page++ {
		query := pcs.GenerateShareQueryURL("list", map[string]string{"bdstoken": tokens["bdstoken"], "root": "1", "web": "5", "app_id": baidupcs.PanAppID, "shorturl": feature[1:], "channel": "chunlei", "num": "1000", "page": strconv.Itoa(page)}).String()
		part := pcs.ExtractShareInfo(query, tokens["shareid"], tokens["share_uk"], tokens["bdstoken"])
		if part["ErrMsg"] != "success" {
			return fail("BAIDU_SHARE_LIST_FAILED")
		}
		count, _ := strconv.Atoi(part["item_num"])
		if count == 0 {
			break
		}
		var batch []int64
		if json.Unmarshal([]byte(part["fs_id"]), &batch) != nil {
			return fail("BAIDU_SHARE_LIST_FAILED")
		}
		for _, id := range batch {
			if id == 0 || seen[id] {
				return fail("BAIDU_SHARE_PAGINATION_INVALID")
			}
			seen[id] = true
		}
		ids = append(ids, batch...)
		params = part
		if count < 1000 {
			break
		}
		if page == 10000 {
			return fail("BAIDU_SHARE_TOO_LARGE")
		}
	}
	if len(ids) == 0 || params == nil {
		return fail("BAIDU_SHARE_EMPTY")
	}
	if err := saveReceipt(journal, state); err != nil {
		return err
	}
	if _, err := pcs.FilesDirectoriesMeta(state.RemotePath); err != nil {
		if err := pcs.Mkdir(state.RemotePath); err != nil {
			return fail("BAIDU_REMOTE_DIRECTORY_FAILED")
		}
	}
	children, errList := listDirectory(pcs, state.RemotePath)
	if errList != nil || len(children) != 0 {
		return fail("BAIDU_TARGET_NOT_EMPTY")
	}
	state.Stage = "transferring"
	if err := saveReceipt(journal, state); err != nil {
		return err
	}
	encoded, _ := json.Marshal(ids)
	params["fs_id"] = string(encoded)
	params["path"] = state.RemotePath
	params["referer"] = "https://pan.baidu.com/s/" + feature
	pcs.UpdatePCSCookies(true)
	remoteMutationStarted = true
	if pcs.GenerateRequestQuery("POST", params)["ErrNo"] != "0" {
		return fail("BAIDU_TRANSFER_FAILED_CONFIRM_TARGET")
	}
	children, errList = listDirectory(pcs, state.RemotePath)
	if errList != nil || len(children) != len(ids) {
		return fail("BAIDU_TRANSFER_INCOMPLETE_CONFIRM_TARGET")
	}
	state.Manifest, _, err = manifest(pcs, state.RemotePath)
	if err != nil {
		return err
	}
	state.Stage = "completed"
	if err := saveReceipt(journal, state); err != nil {
		return err
	}
	emit(map[string]any{"type": "result", "receipt": state})
	return nil
}

func safeLocal(root, relative string) (string, error) {
	if strings.Contains(relative, "\\") {
		return "", fail("BAIDU_UNSAFE_REMOTE_PATH")
	}
	for _, part := range strings.Split(relative, "/") {
		if part == ".." || strings.Contains(part, ":") {
			return "", fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		if runtime.GOOS == "windows" {
			device := strings.ToUpper(strings.Split(part, ".")[0])
			if strings.ContainsAny(part, `<>"|?*`) || strings.HasSuffix(part, ".") || strings.HasSuffix(part, " ") ||
				device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" || regexp.MustCompile(`^(COM|LPT)[0-9]$`).MatchString(device) {
				return "", fail("BAIDU_UNSAFE_REMOTE_PATH")
			}
		}
	}
	local := filepath.Join(root, filepath.FromSlash(relative))
	rel, err := filepath.Rel(root, local)
	if err != nil || rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) {
		return "", fail("BAIDU_UNSAFE_REMOTE_PATH")
	}
	// Existing symlinks must not redirect a remote filename outside the output root.
	for p := local; p != root; p = filepath.Dir(p) {
		if p == filepath.Dir(p) {
			return "", fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		if info, err := os.Lstat(p); err == nil && info.Mode()&os.ModeSymlink != 0 {
			return "", fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
	}
	return local, nil
}

type localPathPair struct{ Remote, Relative string }
type localPathJournal struct {
	Version     int             `json:"version"`
	UID         string          `json:"uid"`
	Fingerprint string          `json:"fingerprint"`
	Paths       []localPathPair `json:"paths"`
}

var archiveFamilySuffix = regexp.MustCompile(`(?i)(\.part[0-9]+\.rar|\.(?:7z|zip|rar)\.[0-9]{3,}|\.r[0-9]{2,}|\.z[0-9]{2,})$`)

// Some publishers append the same word to every volume extension. Keep the
// complete suffix on disk. The marker distinguishes families and never authorizes
// stripping it, opening a file or assuming the download contains an archive.
var maskedRarPartSuffix = regexp.MustCompile(`(?i)\.part[0-9]+\.rar([\p{L}\p{M}]{1,24})$`)

func localNameParts(name string, directory bool) (string, string) {
	if directory {
		return name, ""
	}
	if suffix := archiveFamilySuffix.FindString(name); suffix != "" {
		return strings.TrimSuffix(name, suffix), suffix
	}
	ext := path.Ext(name)
	return strings.TrimSuffix(name, ext), ext
}

func clipUTF8(value string, limit int) string {
	if limit < 0 {
		return ""
	}
	if len(value) <= limit {
		return value
	}
	for limit > 0 && !utf8.RuneStart(value[limit]) {
		limit--
	}
	return value[:limit]
}

func localCaseFold(value string) string {
	return strings.Map(func(r rune) rune {
		minimum := r
		for next := unicode.SimpleFold(r); next != r; next = unicode.SimpleFold(next) {
			if next < minimum {
				minimum = next
			}
		}
		return minimum
	}, value)
}

// The mapped basename leaves room for upstream's checkpoint suffix. The hash is
// based on the original parent and archive-family stem, never on a part number.
func portableLocalName(name, stem, suffix, family string, directory, force bool, suffixBudget int) (string, error) {
	limit := 255
	if !directory {
		limit -= len(pcsdownload.DownloadSuffix)
	}
	clean := strings.Map(func(r rune) rune {
		if r < 32 || strings.ContainsRune(`<>:"/\|?*`, r) {
			return '_'
		}
		return r
	}, stem)
	clean = strings.TrimRight(clean, ". ")
	device := strings.ToUpper(strings.Split(name, ".")[0])
	reserved := device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" || regexp.MustCompile(`^(COM|LPT)[0-9]$`).MatchString(device)
	if !force && len(stem)+suffixBudget <= limit && clean == stem && !reserved && !strings.HasSuffix(name, ".") && !strings.HasSuffix(name, " ") {
		return name, nil
	}
	if len(suffix) > 64 || strings.ContainsAny(suffix, `<>:"/\|?*`) || strings.HasSuffix(suffix, ".") || strings.HasSuffix(suffix, " ") {
		return "", fail("BAIDU_LOCAL_PATH_TOO_LONG")
	}
	sum := sha256.Sum256([]byte(family))
	tag := "~" + hex.EncodeToString(sum[:8])
	clean = strings.TrimRight(clipUTF8(clean, limit-len(tag)-suffixBudget), ". ")
	if clean == "" {
		clean = "item"
	}
	return clean + tag + suffix, nil
}

// Only fresh/empty destinations adopt these names. Unicode filesystems differ
// in normalization: without adding a second normalization implementation to the
// pinned upstream, distinct non-ASCII sibling families receive distinct hashes.
// Readable prefixes and multipart family suffixes remain intact.
func portableManifestPaths(remotePath string, entries []entry) (map[string]string, error) {
	return portableManifestPathsForVersion(remotePath, entries, 2)
}

func portableManifestPathsForVersion(remotePath string, entries []entry, version int) (map[string]string, error) {
	type node struct {
		relative             string
		directory            bool
		stem, suffix, family string
	}
	nodes := map[string]*node{}
	seenPaths := map[string]bool{}
	base := strings.TrimRight(path.Dir(remotePath), "/") + "/"
	for _, e := range entries {
		if seenPaths[e.Path] {
			return nil, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
		seenPaths[e.Path] = true
		if !utf8.ValidString(e.Path) || strings.IndexByte(e.Path, 0) >= 0 || !path.IsAbs(e.Path) || path.Clean(e.Path) != e.Path ||
			(e.Path != remotePath && !strings.HasPrefix(e.Path, strings.TrimRight(remotePath, "/")+"/")) {
			return nil, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
		relative := strings.TrimPrefix(e.Path, base)
		if relative == "" || strings.ContainsAny(relative, "\\:") {
			return nil, fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		for current, dir := relative, e.IsDirectory; current != "."; current, dir = path.Dir(current), true {
			if old := nodes[current]; old != nil && old.directory != dir {
				return nil, fail("BAIDU_LOCAL_PATH_MAPPING_CONFLICT")
			}
			nodes[current] = &node{relative: current, directory: dir}
		}
	}
	families := map[string]map[string]bool{}
	force := map[string]bool{}
	keys := make([]string, 0, len(nodes))
	for _, n := range nodes {
		name := path.Base(n.relative)
		n.stem, n.suffix = localNameParts(name, n.directory)
		n.family = fmt.Sprint(n.directory) + ":" + path.Join(path.Dir(n.relative), n.stem)
		if version == 2 && !n.directory {
			if match := maskedRarPartSuffix.FindStringSubmatchIndex(name); match != nil && match[0] > 0 {
				n.stem, n.suffix = name[:match[0]], name[match[0]:]
				// NUL cannot occur in a valid path segment.
				n.family = "masked-rar:" + path.Join(path.Dir(n.relative), n.stem) + "\x00" + name[match[2]:match[3]]
			}
		}
		// A literal checkpoint-like filename is a separate family. Otherwise its
		// common stem would recreate the same collision after adding the hash.
		if strings.HasSuffix(localCaseFold(n.relative), localCaseFold(pcsdownload.DownloadSuffix)) {
			n.family = "checkpoint-name:" + n.relative
		}
	}
	suffixBudgets := map[string]int{}
	for _, n := range nodes {
		if len(n.suffix) > suffixBudgets[n.family] {
			suffixBudgets[n.family] = len(n.suffix)
		}
	}
	for key, n := range nodes {
		keys = append(keys, key)
		family := n.family
		name := path.Base(key)
		// Treat case collisions as distinct families. A broad Unicode bucket is
		// conservative across NFC/NFD volumes; the hash disambiguates originals.
		buckets := []string{path.Dir(key) + "/case:" + localCaseFold(name), path.Dir(key) + "/unicode"}
		if !n.directory {
			buckets = append(buckets, path.Dir(key)+"/case:"+localCaseFold(name+pcsdownload.DownloadSuffix))
		}
		for _, b := range buckets {
			if strings.HasSuffix(b, "/unicode") && utf8.RuneCountInString(name) == len(name) {
				continue
			}
			if families[b] == nil {
				families[b] = map[string]bool{}
			}
			families[b][family] = true
		}
		candidate, err := portableLocalName(name, n.stem, n.suffix, family, n.directory, false, suffixBudgets[family])
		if err != nil {
			return nil, err
		}
		if candidate != name {
			force[family] = true
		}
	}
	for _, group := range families {
		if len(group) > 1 {
			for family := range group {
				force[family] = true
			}
		}
	}
	sort.Slice(keys, func(i, j int) bool {
		di, dj := strings.Count(keys[i], "/"), strings.Count(keys[j], "/")
		if di != dj {
			return di < dj
		}
		return keys[i] < keys[j]
	})
	mapped := map[string]string{}
	used := map[string]bool{}
	for _, key := range keys {
		n := nodes[key]
		family := n.family
		name, err := portableLocalName(path.Base(key), n.stem, n.suffix, family, n.directory, force[family], suffixBudgets[family])
		if err != nil {
			return nil, err
		}
		local := path.Join(mapped[path.Dir(key)], name)
		folded := localCaseFold(local)
		if used[folded] {
			return nil, fail("BAIDU_LOCAL_PATH_MAPPING_CONFLICT")
		}
		used[folded] = true
		if !n.directory {
			checkpoint := localCaseFold(local + pcsdownload.DownloadSuffix)
			if used[checkpoint] {
				return nil, fail("BAIDU_LOCAL_PATH_MAPPING_CONFLICT")
			}
			used[checkpoint] = true
		}
		mapped[key] = local
	}
	result := map[string]string{}
	for _, e := range entries {
		result[e.Path] = mapped[strings.TrimPrefix(e.Path, base)]
	}
	return result, nil
}

func emptyDownloadPayload(ctx context.Context, root string) error {
	if _, err := os.Lstat(root); os.IsNotExist(err) {
		return nil
	}
	count := 0
	return filepath.WalkDir(root, func(name string, d os.DirEntry, err error) error {
		if err != nil {
			return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectDirectory", err)
		}
		if err = ctx.Err(); err != nil {
			return err
		}
		count++
		if count > 30000 {
			return fail("BAIDU_INSPECTION_LIMIT")
		}
		if !d.IsDir() || d.Type()&os.ModeSymlink != 0 {
			return fail("BAIDU_LOCAL_PATH_MAPPING_CONFLICT")
		}
		return nil
	})
}

func sortedLocalPathPairs(paths map[string]string) []localPathPair {
	pairs := make([]localPathPair, 0, len(paths))
	for remote, relative := range paths {
		pairs = append(pairs, localPathPair{remote, relative})
	}
	sort.Slice(pairs, func(i, j int) bool { return pairs[i].Remote < pairs[j].Remote })
	return pairs
}

// Bind the map to the original account and immutable download intent. Never put
// internal journals among payload files or replace an existing journal. Inspect
// uses precisely the same checks but never creates directories/files.
func resolveDownloadPaths(ctx context.Context, configDir, uid, root string, r request, entries []entry, persist bool) (map[string]string, error) {
	sortedEntries := append([]entry(nil), entries...)
	sort.Slice(sortedEntries, func(i, j int) bool { return sortedEntries[i].Path < sortedEntries[j].Path })
	identity, _ := json.Marshal(struct {
		Operation, UID, Root, Remote string
		Manifest                     []entry
	}{r.OperationID, uid, root, r.RemotePath, sortedEntries})
	fingerprint := sha256.Sum256(identity)
	intent := hex.EncodeToString(fingerprint[:])
	key := sha256.Sum256([]byte(r.OperationID))
	dir := filepath.Join(configDir, "operations")
	journal := filepath.Join(dir, hex.EncodeToString(key[:])+".paths-v1.json")
	read := func() (map[string]string, error) {
		info, statErr := os.Lstat(journal)
		if os.IsNotExist(statErr) {
			return nil, nil
		}
		if statErr != nil {
			return nil, storageFail("BAIDU_RECEIPT_READ_FAILED", "inspectPathMap", statErr)
		}
		if !info.Mode().IsRegular() || info.Size() > 4*1024*1024 {
			return nil, fail("BAIDU_LOCAL_PATH_MAPPING_INVALID")
		}
		b, readErr := os.ReadFile(journal)
		if readErr != nil {
			return nil, storageFail("BAIDU_RECEIPT_READ_FAILED", "readPathMap", readErr)
		}
		var old localPathJournal
		if json.Unmarshal(b, &old) != nil || (old.Version != 1 && old.Version != 2) || old.UID != uid || old.Fingerprint != intent {
			return nil, fail("BAIDU_LOCAL_PATH_MAPPING_INVALID")
		}
		paths, err := portableManifestPathsForVersion(r.RemotePath, entries, old.Version)
		if err != nil || !slices.Equal(old.Paths, sortedLocalPathPairs(paths)) {
			return nil, fail("BAIDU_LOCAL_PATH_MAPPING_INVALID")
		}
		return paths, nil
	}
	if !filepath.IsAbs(configDir) || uid == "" || uid == "0" || r.OperationID == "" {
		return nil, fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	for current := dir; ; current = filepath.Dir(current) {
		info, statErr := os.Lstat(current)
		if statErr != nil && !os.IsNotExist(statErr) {
			return nil, storageFail("BAIDU_RECEIPT_READ_FAILED", "inspectPathMapDirectory", statErr)
		}
		if statErr == nil && (!info.IsDir() || info.Mode()&os.ModeSymlink != 0) {
			return nil, fail("BAIDU_LOCAL_PATH_MAPPING_INVALID")
		}
		if current == filepath.Dir(current) {
			break
		}
	}
	if saved, readErr := read(); readErr != nil || saved != nil {
		return saved, readErr
	}
	paths, err := portableManifestPaths(r.RemotePath, entries)
	if err != nil {
		return nil, err
	}
	changed := false
	base := strings.TrimRight(path.Dir(r.RemotePath), "/") + "/"
	for remote, relative := range paths {
		changed = changed || relative != strings.TrimPrefix(remote, base)
	}
	if !changed {
		return paths, nil
	}
	if err := emptyDownloadPayload(ctx, root); err != nil {
		// Legacy files keep their exact paths. Do not synthesize a different map
		// around them, even when another entry has never begun downloading.
		if err.Error() == "BAIDU_LOCAL_PATH_MAPPING_CONFLICT" {
			legacy := map[string]string{}
			for _, e := range entries {
				legacy[e.Path] = strings.TrimPrefix(e.Path, base)
			}
			if inspectErr := inspectLocalDownloadWithPaths(ctx, root, r.RemotePath, entries, legacy); inspectErr == nil {
				return legacy, nil
			}
		}
		return nil, err
	}
	// Reject any inherited symlink before persisting or creating payload paths.
	if err := inspectLocalDownloadWithPaths(ctx, root, r.RemotePath, entries, paths); err != nil {
		return nil, err
	}
	if !persist {
		return paths, nil
	}
	wantedJSON, _ := json.Marshal(localPathJournal{2, uid, intent, sortedLocalPathPairs(paths)})
	if len(wantedJSON) > 4*1024*1024 {
		return nil, fail("BAIDU_INSPECTION_LIMIT")
	}
	if err := os.MkdirAll(dir, 0700); err != nil {
		return nil, storageFail("BAIDU_RECEIPT_WRITE_FAILED", "createPathMapDirectory", err)
	}
	file, err := os.CreateTemp(dir, ".paths-v1-*")
	if err != nil {
		return nil, storageFail("BAIDU_RECEIPT_WRITE_FAILED", "createPathMap", err)
	}
	defer os.Remove(file.Name())
	if _, err = file.Write(wantedJSON); err == nil {
		err = file.Sync()
	}
	closeErr := file.Close()
	if err == nil {
		err = closeErr
	}
	if err != nil {
		return nil, storageFail("BAIDU_RECEIPT_WRITE_FAILED", "writePathMap", err)
	}
	// Atomic no-replace publication also protects a concurrent existing intent.
	if err = os.Link(file.Name(), journal); err != nil {
		if saved, readErr := read(); readErr != nil || saved != nil {
			return saved, readErr
		}
		return nil, storageFail("BAIDU_RECEIPT_WRITE_FAILED", "publishPathMap", err)
	}
	return paths, nil
}

func download(ctx context.Context, r request, pcs *baidupcs.BaiduPCS) error {
	connections, err := downloadConnections(r.ConnectionsPerFile)
	if err != nil {
		return err
	}
	if !strings.HasPrefix(r.RemotePath, "/") || !filepath.IsAbs(r.OutputDirectory) {
		return fail("BAIDU_DOWNLOAD_TARGET_REQUIRED")
	}
	entries, files, err := manifest(pcs, r.RemotePath)
	if err != nil {
		return err
	}
	if len(r.ExpectedManifest) > 0 {
		actual := map[int64]entry{}
		for _, item := range entries {
			actual[item.ID] = item
		}
		if len(actual) != len(r.ExpectedManifest) {
			return fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
		for _, expected := range r.ExpectedManifest {
			if item, ok := actual[expected.ID]; !ok || item != expected {
				return fail("BAIDU_REMOTE_MANIFEST_CHANGED")
			}
		}
	}
	if err := os.MkdirAll(r.OutputDirectory, 0700); err != nil {
		return storageFail("BAIDU_OUTPUT_DIRECTORY_FAILED", "createDirectory", err)
	}
	root, err := filepath.EvalSymlinks(r.OutputDirectory)
	if err != nil {
		return storageFail("BAIDU_OUTPUT_DIRECTORY_FAILED", "resolveDirectory", err)
	}
	paths, err := resolveDownloadPaths(ctx, os.Getenv(pcsconfig.EnvConfigDir), strconv.FormatUint(pcsconfig.Config.ActiveUser().UID, 10), root, r, entries, true)
	if err != nil {
		return err
	}
	results, err := downloadManifestFilesWithPaths(root, r.RemotePath, files, paths, func(fd *baidupcs.FileDirectory) (*baidupcs.FileDirectory, error) {
		return pcs.FilesDirectoriesMeta(fd.Path)
	}, func(fd *baidupcs.FileDirectory, local string) error {
		// Upstream ignores os.Create's error for empty files. Preserve the local
		// cause here as well; no payload request is needed for an empty member.
		if fd.Size == 0 {
			file, err := os.OpenFile(local, os.O_CREATE|os.O_WRONLY, 0600)
			if err != nil {
				return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "createFile", err)
			}
			if err = file.Close(); err != nil {
				return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "closeFile", err)
			}
			return nil
		}
		executor := taskframework.TaskExecutor{IsFailedDeque: true}
		executor.SetParallel(1)
		cfg := &downloader.Config{Mode: transfer.RangeGenMode_BlockSize, CacheSize: 65536, BlockSize: baidupcs.InitRangeSize, MaxParallel: connections, InstanceStateStorageFormat: downloader.InstanceStateStorageFormatProto3, TryHTTP: false}
		unit := &pcsdownload.DownloadTaskUnit{Cfg: cfg, PCS: pcs, ParentTaskExecutor: &executor, DownloadStatistic: &pcsdownload.DownloadStatistic{}, VerbosePrinter: pcsverbose.New("BAKABASE"), IsOverwrite: true, NoCheck: false, DownloadMode: pcsdownload.DownloadModeLocate, PcsPath: fd.Path, SavePath: local, FileInfo: fd}
		guarded := &storageAwareDownloadUnit{DownloadTaskUnit: unit}
		executor.Append(guarded, 2)
		executor.Execute()
		if executor.FailedDeque().Size() != 0 {
			var pathError *os.PathError
			if errors.As(guarded.lastError, &pathError) {
				return storageFail("BAIDU_DOWNLOAD_FAILED", "download", guarded.lastError)
			}
			return fail("BAIDU_DOWNLOAD_FAILED")
		}
		return nil
	}, func(completed, total int64) {
		emit(map[string]any{"type": "progress", "stage": "downloading", "completedBytes": completed, "totalBytes": total})
	}, verificationProgress{Context: ctx, Report: func(checked, total int64) {
		emit(map[string]any{"type": "progress", "stage": "verifying", "verifiedBytes": checked, "verificationTotalBytes": total})
	}})
	if err != nil {
		return err
	}
	emit(map[string]any{"type": "result", "files": results})
	return nil
}

func downloadConnections(configured int) (int, error) {
	if configured == 0 {
		return 1, nil
	}
	if configured < 1 || configured > 20 {
		return 0, fail("BAIDU_DOWNLOAD_OPTIONS_INVALID")
	}
	return configured, nil
}

// A nonzero file without an upstream sidecar is not proof of completion. The
// provider's MD5 field may be incorrect even after DecryptMD5 for multipart
// uploads. Verify the ordered original block_list, including every last byte.
type downloadChecksums struct {
	blockSize int64
	hashes    [][]byte
}

func trustedDownloadChecksums(fd *baidupcs.FileDirectory) *downloadChecksums {
	if fd.Size < 0 || len(fd.BlockList) == 0 {
		return nil
	}
	blockSize := fd.Size
	if len(fd.BlockList) > 1 {
		blockSize = 0
		// Use upstream upload boundaries, never size/block-count averages. Unknown
		// layouts stay on the ordinary continuation path rather than trusting size.
		for _, candidate := range []int64{baidupcs.MinUploadBlockSize, baidupcs.MiddleUploadBlockSize, baidupcs.MaxUploadBlockSize} {
			if fd.Size > 0 && 1+(fd.Size-1)/candidate == int64(len(fd.BlockList)) {
				if blockSize != 0 {
					return nil
				}
				blockSize = candidate
			}
		}
		if blockSize == 0 {
			return nil
		}
	}
	hashes := make([][]byte, len(fd.BlockList))
	for i, value := range fd.BlockList {
		hash, err := hex.DecodeString(value)
		if err != nil || len(hash) != md5.Size {
			return nil
		}
		hashes[i] = hash
	}
	return &downloadChecksums{blockSize: blockSize, hashes: hashes}
}

// Verification reports actual read activity, not a timer that could keep a stuck
// filesystem alive forever. A bounded read checks cancellation between chunks;
// the host also terminates the process tree when its cancellation token is set.
type verificationProgress struct {
	Digest   io.Writer
	Context  context.Context
	Report   func(int64, int64)
	Interval time.Duration
	Clock    func() time.Time
}
type verificationReader struct {
	reader   io.Reader
	ctx      context.Context
	report   func(int64, int64)
	total    int64
	read     int64
	last     time.Time
	interval time.Duration
	clock    func() time.Time
}

func newVerificationReader(reader io.Reader, total int64, progress verificationProgress) *verificationReader {
	ctx := progress.Context
	if ctx == nil {
		ctx = context.Background()
	}
	clock := progress.Clock
	if clock == nil {
		clock = time.Now
	}
	interval := progress.Interval
	if interval <= 0 {
		interval = time.Second
	}
	r := &verificationReader{reader: reader, ctx: ctx, report: progress.Report, total: total, interval: interval, clock: clock}
	r.last = clock()
	if r.report != nil && ctx.Err() == nil {
		r.report(0, total)
	}
	return r
}
func (r *verificationReader) Read(buffer []byte) (int, error) {
	if err := r.ctx.Err(); err != nil {
		return 0, err
	}
	if len(buffer) > 256*1024 {
		buffer = buffer[:256*1024]
	}
	n, err := r.reader.Read(buffer)
	r.read += int64(n)
	now := r.clock()
	if n > 0 && r.report != nil && (now.Sub(r.last) >= r.interval || r.read == r.total) {
		r.last = now
		r.report(r.read, r.total)
	}
	if cancelled := r.ctx.Err(); cancelled != nil {
		return n, cancelled
	}
	return n, err
}

func verifiedLocalDownload(local string, fd *baidupcs.FileDirectory,
	metadata func(*baidupcs.FileDirectory) (*baidupcs.FileDirectory, error), progress ...verificationProgress) (os.FileInfo, error) {
	control := verificationProgress{}
	if len(progress) > 0 {
		control = progress[0]
	}
	if control.Context != nil && control.Context.Err() != nil {
		return nil, control.Context.Err()
	}

	info, err := os.Lstat(local)
	if os.IsNotExist(err) {
		return nil, nil
	}
	if err != nil {
		return nil, storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectFile", err)
	}
	if !info.Mode().IsRegular() {
		return nil, fail("BAIDU_OUTPUT_FILE_INVALID")
	}
	if info.Size() != fd.Size {
		return nil, nil
	}
	expected := trustedDownloadChecksums(fd)
	if expected == nil && metadata != nil {
		// Directory listings may omit block_list. Fetch metadata only for a local
		// completion candidate, and never accept a different object at the same path.
		fresh, metaErr := metadata(fd)
		if metaErr == nil && fresh != nil {
			if fresh.FsID != fd.FsID || fresh.Path != fd.Path || fresh.Size != fd.Size || fresh.Isdir {
				return nil, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
			}
			expected = trustedDownloadChecksums(fresh)
			if expected != nil {
				// If the local candidate is corrupt, the ordinary download must use
				// the same original checksum for its final upstream validation.
				fd.BlockListJSON = fresh.BlockListJSON
				fd.MD5 = fresh.MD5
				if len(expected.hashes) == 1 {
					fd.MD5 = hex.EncodeToString(expected.hashes[0])
				}
			}
		}
	}
	if expected == nil {
		return nil, nil
	}
	f, err := os.Open(local)
	if err != nil {
		return nil, storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "open", err)
	}
	defer f.Close()
	opened, err := f.Stat()
	if err != nil || !sameLocalFile(info, opened) {
		return nil, fail("BAIDU_LOCAL_FILE_CHANGED")
	}
	var input io.Reader = f
	if control.Digest != nil {
		input = io.TeeReader(f, control.Digest)
	}
	reader := newVerificationReader(input, fd.Size, control)
	var n int64
	matches := true
	for _, expectedHash := range expected.hashes {
		length := expected.blockSize
		if remaining := fd.Size - n; length > remaining {
			length = remaining
		}
		hash := md5.New()
		read, readErr := io.CopyN(hash, reader, length)
		n += read
		if control.Context != nil && control.Context.Err() != nil {
			return nil, control.Context.Err()
		}
		if readErr != nil {
			if errors.Is(readErr, context.Canceled) || errors.Is(readErr, context.DeadlineExceeded) {
				return nil, readErr
			}
			return nil, storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "read", readErr)
		}
		if !bytes.Equal(hash.Sum(nil), expectedHash) {
			matches = false
		}
	}
	after, err := os.Lstat(local)
	if err != nil || n != fd.Size || !sameLocalFile(opened, after) {
		return nil, fail("BAIDU_LOCAL_FILE_CHANGED")
	}
	if !matches {
		return nil, nil
	}

	// A stale sidecar can remain after an interrupted overwrite of an already
	// complete file. The full checksum proves it safe to skip; preserve the sidecar.
	return after, nil
}

func sameLocalFile(before, after os.FileInfo) bool {
	return before != nil && after != nil && after.Mode().IsRegular() && os.SameFile(before, after) &&
		before.Size() == after.Size() && before.ModTime().Equal(after.ModTime())
}

// Read only the upstream range checkpoint for progress. File length alone may
// include old or sparse bytes and must not count as downloaded. This is telemetry,
// never completion evidence; a truncated concurrent checkpoint is simply ignored.
func checkpointDownloadedBytes(local string, expectedSize int64) (int64, bool) {
	info, err := os.Lstat(local)
	if err != nil || !info.Mode().IsRegular() {
		return 0, false
	}
	sidecar := local + pcsdownload.DownloadSuffix
	checkpointInfo, err := os.Lstat(sidecar)
	if err != nil || !checkpointInfo.Mode().IsRegular() {
		return 0, false
	}
	f, err := os.Open(sidecar)
	if err != nil {
		return 0, false
	}
	defer f.Close()
	opened, err := f.Stat()
	if err != nil || !sameLocalFile(checkpointInfo, opened) {
		return 0, false
	}
	data, err := io.ReadAll(io.LimitReader(f, 1024*1024+1))
	if err != nil || len(data) == 0 || len(data) > 1024*1024 {
		return 0, false
	}
	after, err := os.Lstat(sidecar)
	if err != nil || !sameLocalFile(opened, after) {
		return 0, false
	}
	state := &transfer.DownloadInstanceInfoExport{}
	if proto.Unmarshal(data, state) != nil || state.TotalSize != expectedSize ||
		state.RangeGenMode != transfer.RangeGenMode_BlockSize || state.BlockSize <= 0 ||
		state.GenBegin < 0 || state.GenBegin > expectedSize {
		return 0, false
	}
	for _, r := range state.Ranges {
		if r == nil || r.Begin < 0 || r.End < r.Begin || r.End > state.GenBegin {
			return 0, false
		}
	}
	sort.Slice(state.Ranges, func(i, j int) bool { return state.Ranges[i].Begin < state.Ranges[j].Begin })
	var remaining, end int64
	for _, r := range state.Ranges {
		if r.Begin < end || r.End-r.Begin > state.GenBegin-remaining {
			return 0, false
		}
		remaining += r.End - r.Begin
		end = r.End
	}
	downloaded := state.GenBegin - remaining
	if downloaded > info.Size() {
		return 0, false
	}
	return downloaded, true
}

func downloadManifestFiles(root, remotePath string, files []*baidupcs.FileDirectory,
	metadata func(*baidupcs.FileDirectory) (*baidupcs.FileDirectory, error),
	execute func(*baidupcs.FileDirectory, string) error, report func(int64, int64), verification ...verificationProgress) ([]string, error) {
	return downloadManifestFilesWithPaths(root, remotePath, files, nil, metadata, execute, report, verification...)
}

func downloadManifestFilesWithPaths(root, remotePath string, files []*baidupcs.FileDirectory, paths map[string]string,
	metadata func(*baidupcs.FileDirectory) (*baidupcs.FileDirectory, error),
	execute func(*baidupcs.FileDirectory, string) error, report func(int64, int64), verification ...verificationProgress) ([]string, error) {
	checkCancellation := func() error {
		if len(verification) > 0 && verification[0].Context != nil {
			return verification[0].Context.Err()
		}
		return nil
	}
	type localFile struct {
		remote     *baidupcs.FileDirectory
		path       string
		verified   os.FileInfo
		downloaded int64
	}
	base := path.Dir(path.Clean(remotePath))
	var pending []*localFile
	var total int64
	for _, fd := range files {
		if err := checkCancellation(); err != nil {
			return nil, err
		}
		relative := strings.TrimPrefix(fd.Path, base+"/")
		if base == "/" {
			relative = strings.TrimPrefix(fd.Path, "/")
		}
		if paths != nil {
			var found bool
			relative, found = paths[fd.Path]
			if !found {
				return nil, fail("BAIDU_LOCAL_PATH_MAPPING_INVALID")
			}
		}
		local, err := safeLocal(root, relative)
		if err != nil {
			return nil, err
		}
		if fd.Isdir {
			if err := os.MkdirAll(local, 0700); err != nil {
				return nil, storageFail("BAIDU_OUTPUT_DIRECTORY_FAILED", "createDirectory", err)
			}
			continue
		}
		if err := os.MkdirAll(filepath.Dir(local), 0700); err != nil {
			return nil, storageFail("BAIDU_OUTPUT_DIRECTORY_FAILED", "createDirectory", err)
		}
		if fd.Size < 0 || total > (1<<63-1)-fd.Size {
			return nil, fail("BAIDU_MANIFEST_FAILED")
		}
		total += fd.Size
		verified, err := verifiedLocalDownload(local, fd, metadata, verification...)
		if err != nil {
			return nil, err
		}
		file := &localFile{remote: fd, path: local, verified: verified}
		if verified != nil {
			file.downloaded = fd.Size
		} else {
			file.downloaded, _ = checkpointDownloadedBytes(local, fd.Size)
		}
		pending = append(pending, file)
	}
	reportProgress := func() {
		var completed int64
		for _, file := range pending {
			completed += file.downloaded
		}
		report(completed, total)
	}
	reportProgress()
	results := []string{}
	for _, file := range pending {
		if err := checkCancellation(); err != nil {
			return nil, err
		}
		fd, local := file.remote, file.path
		if file.verified != nil {
			info, err := os.Lstat(local)
			if err != nil || !sameLocalFile(file.verified, info) {
				return nil, fail("BAIDU_LOCAL_FILE_CHANGED")
			}
			results = append(results, local)
			continue
		}
		stop := make(chan struct{})
		done := make(chan struct{})
		go func() {
			defer close(done)
			ticker := time.NewTicker(time.Second)
			defer ticker.Stop()
			for {
				select {
				case <-stop:
					return
				case <-ticker.C:
					if downloaded, ok := checkpointDownloadedBytes(local, fd.Size); ok {
						file.downloaded = downloaded
					}
					reportProgress()
				}
			}
		}()
		err := execute(fd, local)
		close(stop)
		<-done
		if err != nil {
			return nil, err
		}
		info, err := os.Stat(local)
		if err != nil {
			return nil, storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectFile", err)
		}
		if info.Size() != fd.Size {
			return nil, fail("BAIDU_DOWNLOAD_INCOMPLETE")
		}
		// Upstream does not validate multipart checksums after resuming ranges.
		// A previously corrupt completed range must never become a successful
		// output merely because the remaining ranges and final length match.
		verified, verifyErr := verifiedLocalDownload(local, fd, metadata, verification...)
		if verifyErr != nil {
			return nil, verifyErr
		}
		if trustedDownloadChecksums(fd) != nil && verified == nil {
			return nil, fail("BAIDU_DOWNLOAD_CHECKSUM_MISMATCH")
		}
		file.downloaded = fd.Size
		results = append(results, local)
		reportProgress()
	}
	if err := checkCancellation(); err != nil {
		return nil, err
	}
	return results, nil
}

// Only explicit local syscall causes leave the bridge; never expose paths, upstream
// URLs, cookies or arbitrary error strings. Cloud quota errors have no storage field.
type storageFailure struct {
	code, operation, kind string
	cause                 error
}

func (e *storageFailure) Error() string { return e.code }
func (e *storageFailure) Unwrap() error { return e.cause }
func storageFail(code, operation string, cause error) error {
	if errors.Is(cause, syscall.ENAMETOOLONG) {
		return fail("BAIDU_LOCAL_PATH_TOO_LONG")
	}
	kind := "unknown"
	switch {
	case errors.Is(cause, syscall.ENOSPC):
		kind = "diskFull"
	case errors.Is(cause, syscall.EDQUOT):
		kind = "quota"
	case errors.Is(cause, syscall.EACCES), errors.Is(cause, syscall.EPERM):
		kind = "permission"
	case errors.Is(cause, syscall.EIO), errors.Is(cause, syscall.ENODEV), errors.Is(cause, syscall.ENXIO), errors.Is(cause, syscall.ESTALE), errors.Is(cause, syscall.EROFS), errors.Is(cause, syscall.ETIMEDOUT):
		kind = "unavailable"
	}
	// Windows native errno values are distinct from Unix syscall constants.
	if runtime.GOOS == "windows" {
		var errno syscall.Errno
		if errors.As(cause, &errno) {
			switch uint32(errno) {
			case 206: // ERROR_FILENAME_EXCED_RANGE
				return fail("BAIDU_LOCAL_PATH_TOO_LONG")
			case 39, 112:
				kind = "diskFull"
			case 1295: // ERROR_DISK_QUOTA_EXCEEDED; 1816 is a generic system-resource quota.
				kind = "quota"
			case 5:
				kind = "permission"
			case 21, 53, 64, 67, 121, 1117:
				kind = "unavailable"
			}
		}
	}
	return &storageFailure{code, operation, kind, cause}
}
func errorEvent(err error) map[string]any {
	event := map[string]any{"type": "error", "code": err.Error(), "mayHaveSideEffects": remoteMutationStarted}
	var storage *storageFailure
	if errors.As(err, &storage) {
		event["code"] = storage.code
		event["storage"] = map[string]any{"kind": storage.kind, "operation": storage.operation}
	}
	return event
}

type storageAwareDownloadUnit struct {
	*pcsdownload.DownloadTaskUnit
	lastError error
}

func (unit *storageAwareDownloadUnit) Run() *taskframework.TaskUnitRunResult {
	result := unit.DownloadTaskUnit.Run()
	if result != nil {
		unit.lastError = result.Err
		var local *os.PathError
		if errors.As(result.Err, &local) {
			result.NeedRetry = false
		}
	}
	return result
}

// Config.Init opens O_RDWR|O_CREATE. Inspection/download instead read a stable existing
// profile into memory and never calls Init, Close, Save or account validation.
func inspectionPCS(directory, expectedUID string) (*baidupcs.BaiduPCS, string, error) {
	if !filepath.IsAbs(directory) || expectedUID == "" {
		return nil, "", fail("BAIDU_ACCOUNT_IDENTITY_CHANGED")
	}
	for current := directory; ; current = filepath.Dir(current) {
		info, err := os.Lstat(current)
		if err != nil || !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
			return nil, "", fail("BAIDU_CONFIG_INVALID")
		}
		if current == filepath.Dir(current) {
			break
		}
	}
	file := filepath.Join(directory, pcsconfig.ConfigName)
	info, err := os.Lstat(file)
	if err != nil || !info.Mode().IsRegular() || info.Size() <= 0 || info.Size() > 1024*1024 {
		return nil, "", fail("BAIDU_CONFIG_INVALID")
	}
	f, err := os.Open(file)
	if err != nil {
		return nil, "", fail("BAIDU_CONFIG_INVALID")
	}
	defer f.Close()
	opened, err := f.Stat()
	if err != nil || !sameLocalFile(info, opened) {
		return nil, "", fail("BAIDU_CONFIG_INVALID")
	}
	cfg := pcsconfig.NewConfig("")
	cfg.InitDefaultConfig()
	data, err := io.ReadAll(io.LimitReader(f, 1024*1024+1))
	if err != nil || len(data) > 1024*1024 || json.Unmarshal(data, cfg) != nil {
		return nil, "", fail("BAIDU_CONFIG_INVALID")
	}
	after, err := os.Lstat(file)
	if err != nil || !sameLocalFile(opened, after) {
		return nil, "", fail("BAIDU_CONFIG_INVALID")
	}
	uid := strconv.FormatUint(cfg.BaiduActiveUID, 10)
	if uid != expectedUID || cfg.BaiduActiveUID == 0 {
		return nil, "", fail("BAIDU_ACCOUNT_IDENTITY_CHANGED")
	}
	user, err := cfg.GetBaiduUser(&pcsconfig.BaiduBase{UID: cfg.BaiduActiveUID})
	if err != nil || user == nil || user.UID != cfg.BaiduActiveUID || user.BDUSS == "" {
		return nil, "", fail("BAIDU_LOGIN_REQUIRED")
	}
	pcsconfig.Config = cfg
	requester.UserAgent = cfg.UserAgent
	requester.SetGlobalProxy(cfg.Proxy)
	requester.SetProxyHostnameRules(cfg.ProxyHostnames)
	requester.SetLocalTCPAddrList(strings.Split(cfg.LocalAddrs, ",")...)
	// JSON does not populate upstream's private activeUser/pcs caches. Download
	// units use those caches again for authentication, so returning an independent
	// user.BaiduPCS() client would leave their UID zero and cookie jar anonymous.
	// SwitchUser only binds the already-verified UID in this in-memory config;
	// the pinned implementation does not open, save or close a profile here.
	active, err := cfg.SwitchUser(&pcsconfig.BaiduBase{UID: cfg.BaiduActiveUID})
	if err != nil || active == nil || active.UID != user.UID || cfg.ActiveUser().UID != user.UID {
		return nil, "", fail("BAIDU_ACCOUNT_IDENTITY_CHANGED")
	}
	pcs := cfg.ActiveUserBaiduPCS()
	pcs.SetPCSAddr(cfg.PCSAddr)
	return pcs, uid, nil
}

func sameManifest(expected, actual []entry) bool {
	if len(expected) == 0 || len(expected) > 10000 || len(expected) != len(actual) {
		return false
	}
	values := map[int64]entry{}
	for _, e := range expected {
		if e.ID <= 0 || values[e.ID].ID != 0 || e.Size < 0 || !path.IsAbs(e.Path) || path.Clean(e.Path) != e.Path {
			return false
		}
		values[e.ID] = e
	}
	seen := map[int64]bool{}
	for _, e := range actual {
		if seen[e.ID] || values[e.ID] != e {
			return false
		}
		seen[e.ID] = true
	}
	return true
}
func inspectDownload(ctx context.Context, r request, pcs *baidupcs.BaiduPCS) error {
	if !path.IsAbs(r.RemotePath) || path.Clean(r.RemotePath) != r.RemotePath || !filepath.IsAbs(r.OutputDirectory) ||
		filepath.Clean(r.OutputDirectory) != r.OutputDirectory || !sameManifest(r.ExpectedManifest, r.ExpectedManifest) {
		return fail("BAIDU_DOWNLOAD_IDENTITY_INVALID")
	}
	if r.VerifyRemote {
		actual, _, err := manifestLimited(pcs, r.RemotePath, 10000)
		if err != nil {
			return err
		}
		if !sameManifest(r.ExpectedManifest, actual) {
			return fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
	}
	paths, err := resolveDownloadPaths(ctx, os.Getenv(pcsconfig.EnvConfigDir), r.ExpectedUID, r.OutputDirectory, r, r.ExpectedManifest, false)
	if err != nil {
		return err
	}
	return inspectLocalDownloadWithPaths(ctx, r.OutputDirectory, r.RemotePath, r.ExpectedManifest, paths)
}
func inspectLocalDownload(ctx context.Context, root, remotePath string, entries []entry) error {
	return inspectLocalDownloadWithPaths(ctx, root, remotePath, entries, nil)
}
func inspectLocalDownloadWithPaths(ctx context.Context, root, remotePath string, entries []entry, paths map[string]string) error {
	// No symlink in an existing ancestor, including a missing destination's parent.
	for current := root; ; current = filepath.Dir(current) {
		info, err := os.Lstat(current)
		if err != nil && !os.IsNotExist(err) {
			return storageFail("BAIDU_OUTPUT_DIRECTORY_FAILED", "inspectDirectory", err)
		}
		if err == nil && (!info.IsDir() || info.Mode()&os.ModeSymlink != 0) {
			return fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		if current == filepath.Dir(current) {
			break
		}
	}
	base := path.Dir(remotePath)
	allowed := map[string]bool{}
	expected := map[string]entry{}
	for _, e := range entries {
		if err := ctx.Err(); err != nil {
			return err
		}
		if e.Path != remotePath && !strings.HasPrefix(e.Path, strings.TrimRight(remotePath, "/")+"/") {
			return fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
		relative := strings.TrimPrefix(e.Path, strings.TrimRight(base, "/")+"/")
		if paths != nil {
			var found bool
			relative, found = paths[e.Path]
			if !found {
				return fail("BAIDU_LOCAL_PATH_MAPPING_INVALID")
			}
		}
		local, err := safeLocal(root, relative)
		if err != nil {
			return err
		}
		if e.IsDirectory {
			continue
		}
		if _, duplicate := expected[local]; duplicate {
			return fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
		expected[local] = e
		allowed[local] = true
		allowed[local+pcsdownload.DownloadSuffix] = true
	}
	for local, e := range expected {
		info, err := os.Lstat(local)
		if err != nil && !os.IsNotExist(err) {
			return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectFile", err)
		}
		sidecar, sideErr := os.Lstat(local + pcsdownload.DownloadSuffix)
		if sideErr != nil && !os.IsNotExist(sideErr) {
			return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectCheckpoint", sideErr)
		}
		if os.IsNotExist(err) {
			if sideErr == nil {
				return fail("BAIDU_DOWNLOAD_CHECKPOINT_INVALID")
			}
			continue
		}
		if !info.Mode().IsRegular() || info.Size() > e.Size {
			return fail("BAIDU_OUTPUT_FILE_INVALID")
		}
		if sideErr == nil {
			if !sidecar.Mode().IsRegular() || sidecar.Size() > 1024*1024 {
				return fail("BAIDU_DOWNLOAD_CHECKPOINT_INVALID")
			}
			// BaiduPCS-Go creates both files before recording its first range.
			// Its state reader treats an empty checkpoint as no saved progress.
			if e.Size > 0 && info.Size() == 0 && sidecar.Size() == 0 {
				continue
			}
			if _, valid := checkpointDownloadedBytes(local, e.Size); !valid {
				return fail("BAIDU_DOWNLOAD_CHECKPOINT_INVALID")
			}
		} else if info.Size() != e.Size {
			return fail("BAIDU_DOWNLOAD_PARTIAL_UNVERIFIED")
		}
	}
	if _, err := os.Lstat(root); os.IsNotExist(err) {
		return nil
	}
	count := 0
	return filepath.WalkDir(root, func(name string, d os.DirEntry, err error) error {
		if err != nil {
			return storageFail("BAIDU_OUTPUT_FILE_READ_FAILED", "inspectDirectory", err)
		}
		if err = ctx.Err(); err != nil {
			return err
		}
		count++
		if count > 30000 {
			return fail("BAIDU_INSPECTION_LIMIT")
		}
		if d.Type()&os.ModeSymlink != 0 {
			return fail("BAIDU_UNSAFE_REMOTE_PATH")
		}
		if d.IsDir() {
			return nil
		}
		if !allowed[name] {
			return fail("BAIDU_OUTPUT_UNEXPECTED_FILE")
		}
		return nil
	})
}

// Completion recovery never invokes the downloader, creates directories/checkpoints,
// refreshes credentials or transfers files. Only metadata and existing bytes are read.
type verifiedExistingFile struct {
	Path   string `json:"path"`
	Length int64  `json:"length"`
	Sha256 string `json:"sha256"`
}

func verifyExistingDownload(ctx context.Context, r request, pcs *baidupcs.BaiduPCS) ([]verifiedExistingFile, error) {
	if len(r.ExpectedManifest) > 10000 {
		return nil, fail("BAIDU_MANIFEST_TOO_LARGE")
	}
	inspect := r
	inspect.VerifyRemote = false
	if err := inspectDownload(ctx, inspect, pcs); err != nil {
		return nil, err
	}
	paths, err := resolveDownloadPaths(ctx, os.Getenv(pcsconfig.EnvConfigDir), r.ExpectedUID, r.OutputDirectory, r, r.ExpectedManifest, false)
	if err != nil {
		return nil, err
	}
	if r.VerifyRemote {
		actual, _, err := manifestLimited(pcs, r.RemotePath, 10000)
		if err != nil {
			return nil, err
		}
		if !sameManifest(r.ExpectedManifest, actual) {
			return nil, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
	}
	results := []verifiedExistingFile{}
	for _, expected := range r.ExpectedManifest {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		if expected.IsDirectory {
			continue
		}
		local, err := safeLocal(r.OutputDirectory, paths[expected.Path])
		if err != nil {
			return nil, err
		}
		info, err := os.Lstat(local)
		if err != nil || !info.Mode().IsRegular() || info.Size() != expected.Size {
			return nil, fail("BAIDU_EXISTING_DOWNLOAD_UNVERIFIED")
		}
		result := verifiedExistingFile{Path: local, Length: info.Size()}
		if r.VerifyRemote {
			result, err = verifyExistingFile(ctx, local, expected, func() (*baidupcs.FileDirectory, error) {
				return pcs.FilesDirectoriesMeta(expected.Path)
			})
			if err != nil {
				return nil, err
			}
		}
		results = append(results, result)
	}
	if len(results) == 0 {
		return nil, fail("BAIDU_EXISTING_DOWNLOAD_UNVERIFIED")
	}
	if r.VerifyRemote {
		actual, _, err := manifestLimited(pcs, r.RemotePath, 10000)
		if err != nil {
			return nil, err
		}
		if !sameManifest(r.ExpectedManifest, actual) {
			return nil, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
	}
	return results, nil
}
func verifyExistingFile(ctx context.Context, local string, expected entry, metadata func() (*baidupcs.FileDirectory, error)) (verifiedExistingFile, error) {
	empty := verifiedExistingFile{}
	before, err := metadata()
	if err != nil {
		return empty, fail("BAIDU_MANIFEST_FAILED")
	}
	matches := func(fd *baidupcs.FileDirectory) bool {
		return fd != nil && !fd.Isdir && fd.FsID == expected.ID && fd.Path == expected.Path && fd.Size == expected.Size
	}
	if !matches(before) {
		return empty, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
	}
	checksums := trustedDownloadChecksums(before)
	if checksums == nil {
		return empty, fail("BAIDU_EXISTING_DOWNLOAD_UNVERIFIED")
	}
	digest := sha256.New()
	info, err := verifiedLocalDownload(local, before, nil, verificationProgress{Context: ctx, Digest: digest})
	if err != nil {
		return empty, err
	}
	if info == nil {
		return empty, fail("BAIDU_EXISTING_DOWNLOAD_UNVERIFIED")
	}
	after, err := metadata()
	if err != nil {
		return empty, fail("BAIDU_MANIFEST_FAILED")
	}
	if !matches(after) {
		return empty, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
	}
	latest := trustedDownloadChecksums(after)
	if latest == nil || latest.blockSize != checksums.blockSize || len(latest.hashes) != len(checksums.hashes) {
		return empty, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
	}
	for i := range latest.hashes {
		if !bytes.Equal(latest.hashes[i], checksums.hashes[i]) {
			return empty, fail("BAIDU_REMOTE_MANIFEST_CHANGED")
		}
	}
	current, err := os.Lstat(local)
	if err != nil || !sameLocalFile(info, current) {
		return empty, fail("BAIDU_LOCAL_FILE_CHANGED")
	}
	return verifiedExistingFile{local, info.Size(), hex.EncodeToString(digest.Sum(nil))}, nil
}
