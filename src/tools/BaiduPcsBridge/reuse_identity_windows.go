package main

import (
	"fmt"
	"golang.org/x/sys/windows"
	"os"
	"unsafe"
)

func reuseIdentity(name string) (string, int64, uint64, error) {
	p, err := windows.UTF16PtrFromString(name)
	if err != nil {
		return "", 0, 0, err
	}
	h, err := windows.CreateFile(p, windows.FILE_READ_ATTRIBUTES, windows.FILE_SHARE_READ|windows.FILE_SHARE_WRITE|windows.FILE_SHARE_DELETE, nil, windows.OPEN_EXISTING, windows.FILE_FLAG_BACKUP_SEMANTICS|windows.FILE_FLAG_OPEN_REPARSE_POINT, 0)
	if err != nil {
		return "", 0, 0, err
	}
	defer windows.CloseHandle(h)
	var s windows.ByHandleFileInformation
	if err = windows.GetFileInformationByHandle(h, &s); err != nil {
		return "", 0, 0, err
	}
	if s.FileAttributes&windows.FILE_ATTRIBUTE_REPARSE_POINT != 0 {
		return "", 0, 0, fail("BAIDU_UNSAFE_REMOTE_PATH")
	}
	// FILE_BASIC_INFO stores ChangeTime in its fourth 64-bit field.
	var basic [5]int64
	if err = windows.GetFileInformationByHandleEx(h, windows.FileBasicInfo, (*byte)(unsafe.Pointer(&basic)), uint32(unsafe.Sizeof(basic))); err != nil {
		return "", 0, 0, err
	}
	return fmt.Sprintf("%x:%x:%x", s.VolumeSerialNumber, s.FileIndexHigh, s.FileIndexLow), basic[3], uint64(s.NumberOfLinks), nil
}
func reuseLock(f *os.File) error {
	return windows.LockFileEx(windows.Handle(f.Fd()), windows.LOCKFILE_EXCLUSIVE_LOCK|windows.LOCKFILE_FAIL_IMMEDIATELY, 0, 1, 0, new(windows.Overlapped))
}
func syncReuseDir(name string) error { return nil } // Windows does not support fsync on a directory.
