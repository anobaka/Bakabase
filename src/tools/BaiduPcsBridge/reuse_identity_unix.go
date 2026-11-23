//go:build darwin || linux

package main

import (
	"fmt"
	"golang.org/x/sys/unix"
	"os"
)

func reuseIdentity(name string) (identity string, changed int64, links uint64, err error) {
	var s unix.Stat_t
	if err = unix.Lstat(name, &s); err != nil {
		return
	}
	return fmt.Sprintf("%x:%x", s.Dev, s.Ino), s.Ctim.Nano(), uint64(s.Nlink), nil
}
func reuseLock(f *os.File) error { return unix.Flock(int(f.Fd()), unix.LOCK_EX|unix.LOCK_NB) }
func syncReuseDir(name string) error {
	f, err := os.Open(name)
	if err != nil {
		return err
	}
	defer f.Close()
	return f.Sync()
}
