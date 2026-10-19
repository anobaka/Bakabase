// Bakabase JSON bridge. Build this directory inside the pinned BaiduPCS-Go module.
// The upstream Apache-2.0 license is retained beside the combined executable.
package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/url"
	"os"
	"path"
	"path/filepath"
	"regexp"
	"runtime"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/qjfoidnh/BaiduPCS-Go/baidupcs"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsconfig"
	"github.com/qjfoidnh/BaiduPCS-Go/internal/pcsfunctions/pcsdownload"
	"github.com/qjfoidnh/BaiduPCS-Go/pcsutil/taskframework"
	"github.com/qjfoidnh/BaiduPCS-Go/pcsverbose"
	"github.com/qjfoidnh/BaiduPCS-Go/requester/downloader"
	"github.com/qjfoidnh/BaiduPCS-Go/requester/transfer"
)

const version = "4.0.2+bakabase.1"

type request struct {
	Command          string  `json:"command"`
	Cookies          string  `json:"cookies"`
	BDUSS            string  `json:"bduss"`
	STOKEN           string  `json:"stoken"`
	OperationID      string  `json:"operationId"`
	URL              string  `json:"url"`
	AccessCode       string  `json:"accessCode"`
	RemoteDirectory  string  `json:"remoteDirectory"`
	RemotePath       string  `json:"remotePath"`
	OutputDirectory  string  `json:"outputDirectory"`
	ExpectedManifest []entry `json:"expectedManifest"`
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
	err = run()
	if err != nil {
		emit(map[string]any{"type": "error", "code": err.Error(), "mayHaveSideEffects": remoteMutationStarted})
		os.Exit(1)
	}
}
func run() error {
	var r request
	if json.NewDecoder(io.LimitReader(os.Stdin, 1024*1024)).Decode(&r) != nil {
		return fail("BAIDU_INVALID_REQUEST")
	}
	if !filepath.IsAbs(os.Getenv(pcsconfig.EnvConfigDir)) {
		return fail("BAIDU_CONFIG_DIRECTORY_REQUIRED")
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
	case "download":
		return download(r, pcs)
	default:
		return fail("BAIDU_UNKNOWN_COMMAND")
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
		if len(files) > 2000000 {
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
	tokens := pcs.AccessSharePage(feature, true)
	if tokens["ErrMsg"] != "0" {
		return fail("BAIDU_SHARE_UNAVAILABLE")
	}
	verifyURL := pcs.GenerateShareQueryURL("verify", map[string]string{"shareid": tokens["shareid"], "time": strconv.FormatInt(time.Now().UnixMilli(), 10), "clienttype": "1", "uk": tokens["share_uk"]}).String()
	if pcs.PostShareQuery(verifyURL, r.URL, map[string]string{"pwd": code, "vcode": "null", "vcode_str": "null", "bdstoken": tokens["bdstoken"]})["ErrMsg"] != "0" {
		return fail("BAIDU_ACCESS_CODE_REJECTED")
	}
	pcs.UpdatePCSCookies(true)
	tokens = pcs.AccessSharePage(feature, false)
	if tokens["ErrMsg"] != "0" {
		return fail("BAIDU_SHARE_UNAVAILABLE")
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
func download(r request, pcs *baidupcs.BaiduPCS) error {
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
	if os.MkdirAll(r.OutputDirectory, 0700) != nil {
		return fail("BAIDU_OUTPUT_DIRECTORY_FAILED")
	}
	root, err := filepath.EvalSymlinks(r.OutputDirectory)
	if err != nil {
		return fail("BAIDU_OUTPUT_DIRECTORY_FAILED")
	}
	base := path.Dir(path.Clean(r.RemotePath))
	results := []string{}
	var total, completed int64
	for _, fd := range files {
		if !fd.Isdir {
			total += fd.Size
		}
	}
	for _, fd := range files {
		relative := strings.TrimPrefix(fd.Path, base+"/")
		if base == "/" {
			relative = strings.TrimPrefix(fd.Path, "/")
		}
		local, err := safeLocal(root, relative)
		if err != nil {
			return err
		}
		if fd.Isdir {
			if os.MkdirAll(local, 0700) != nil {
				return fail("BAIDU_OUTPUT_DIRECTORY_FAILED")
			}
			continue
		}
		if os.MkdirAll(filepath.Dir(local), 0700) != nil {
			return fail("BAIDU_OUTPUT_DIRECTORY_FAILED")
		}
		executor := taskframework.TaskExecutor{IsFailedDeque: true}
		executor.SetParallel(1)
		cfg := &downloader.Config{Mode: transfer.RangeGenMode_BlockSize, CacheSize: 65536, BlockSize: baidupcs.InitRangeSize, MaxParallel: 1, InstanceStateStorageFormat: downloader.InstanceStateStorageFormatProto3, TryHTTP: false}
		unit := &pcsdownload.DownloadTaskUnit{Cfg: cfg, PCS: pcs, ParentTaskExecutor: &executor, DownloadStatistic: &pcsdownload.DownloadStatistic{}, VerbosePrinter: pcsverbose.New("BAKABASE"), IsOverwrite: true, NoCheck: false, DownloadMode: pcsdownload.DownloadModeLocate, PcsPath: fd.Path, SavePath: local, FileInfo: fd}
		executor.Append(unit, 2)
		stop := make(chan struct{})
		done := make(chan struct{})
		go func(previous int64) {
			defer close(done)
			ticker := time.NewTicker(time.Second)
			defer ticker.Stop()
			for {
				select {
				case <-stop:
					return
				case <-ticker.C:
					var size int64
					if info, err := os.Stat(local); err == nil {
						size = info.Size()
						if size > fd.Size {
							size = fd.Size
						}
					}
					emit(map[string]any{"type": "progress", "completedBytes": previous + size, "totalBytes": total})
				}
			}
		}(completed)
		executor.Execute()
		close(stop)
		<-done
		if executor.FailedDeque().Size() != 0 {
			return fail("BAIDU_DOWNLOAD_FAILED")
		}
		info, err := os.Stat(local)
		if err != nil || info.Size() != fd.Size {
			return fail("BAIDU_DOWNLOAD_INCOMPLETE")
		}
		completed += fd.Size
		results = append(results, local)
		emit(map[string]any{"type": "progress", "completedBytes": completed, "totalBytes": total})
	}
	emit(map[string]any{"type": "result", "files": results})
	return nil
}
