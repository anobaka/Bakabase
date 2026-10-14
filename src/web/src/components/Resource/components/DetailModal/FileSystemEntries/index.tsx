"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";

import PathLibrary from "path";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import FileSystemEntry from "./FileSystemEntry";

import { useResourceApi, useResourceOwner } from "@/features/management/resourceContext";
import { Button, Pagination, Spinner } from "@/components/bakaui";

type Props = {
  path: string;
  isFile: boolean;
};

const PageSize = 36;
const FileSystemEntries = ({ path, isFile }: Props) => {
  const BApi = useResourceApi();
  const owner = useResourceOwner();
  const isRemoteOwner = !!owner && !owner.isLocal;
  const { t } = useTranslation();
  const [currentPath, setCurrentPath] = useState(path);
  const [fsEntries, setFsEntries] = useState<Entry[]>([]);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);
  const [nextCursor, setNextCursor] = useState<string>();
  const [error, setError] = useState<string>();
  const requestVersion = useRef(0);

  const previewPath = useCallback(
    async (path: string, cursor?: string) => {
      const version = ++requestVersion.current;
      setLoading(true);
      setError(undefined);
      if (!cursor) {
        setFsEntries([]);
        setPage(1);
        setNextCursor(undefined);
      }
      const query = {
        root: path,
        ...(isRemoteOwner ? { pageSize: PageSize, cursor } : {}),
      };
      try {
        const response = await BApi.file.getChildrenIwFsInfo(query, {
          showErrorToast: (r) => (r.code >= 404 || r.code < 200) && r.code != 404,
        });
        if (version !== requestVersion.current) return;
        if (response.code) throw new Error(response.message);
        const result = response.data as
          | { entries?: Entry[]; nextCursor?: string; hasMore?: boolean }
          | undefined;

        setFsEntries((previous) =>
          cursor ? [...previous, ...(result?.entries ?? [])] : (result?.entries ?? []),
        );
        setNextCursor(isRemoteOwner && result?.hasMore ? result.nextCursor : undefined);
        if (cursor) setPage((previous) => previous + 1);
      } catch (failure) {
        if (version === requestVersion.current) setError(String(failure));
      } finally {
        if (version === requestVersion.current) setLoading(false);
      }
    },
    [BApi, isRemoteOwner],
  );

  useEffect(() => {
    setCurrentPath(path);
    void previewPath(path);

    return () => {
      ++requestVersion.current;
    };
  }, [path, previewPath]);

  const skipCount = (page - 1) * PageSize;
  const pageCount = Math.ceil(fsEntries.length / PageSize);
  const showPagination = pageCount > 1;

  let relativePathSegments: string[] = [];

  if (!isFile && currentPath) {
    relativePathSegments = currentPath
      .replace(path, "")
      .replace(/\\/g, "/")
      .split("/")
      .filter((a) => a);
    if (relativePathSegments.length > 0) {
      relativePathSegments.splice(0, 0, ".");
    }
  }

  const changePath = useCallback(
    (path: string) => {
      setCurrentPath(path);
      void previewPath(path);
    },
    [previewPath],
  );

  return (
    <div>
      <div>
        <span className={"text-base"}>{t<string>("Files")}</span>
        {loading && <Spinner size={"sm"} />}
      </div>
      <div className={"flex items-center justify-between"}>
        <div>
          {relativePathSegments.length > 0 &&
            relativePathSegments.map((s, i) => {
              return (
                <>
                  <span
                    className={"hover:font-bold"}
                    role="button"
                    tabIndex={0}
                    onClick={() => {
                      if (i == relativePathSegments.length - 1) {
                        return;
                      }
                      let segments = [path];

                      if (i > 0) {
                        segments = segments.concat(relativePathSegments.slice(1, i + 1));
                      }
                      changePath(segments.join(PathLibrary.sep));
                    }}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        if (i == relativePathSegments.length - 1) {
                          return;
                        }
                        let segments = [path];

                        if (i > 0) {
                          segments = segments.concat(relativePathSegments.slice(1, i + 1));
                        }
                        changePath(segments.join(PathLibrary.sep));
                      }
                    }}
                  >
                    {s}
                  </span>
                  {i < relativePathSegments.length - 1 && <span className={"opacity-60"}>/</span>}
                </>
              );
            })}
        </div>
        <div>
          {showPagination && (
            <Pagination
              boundaries={3}
              page={page}
              size={"sm"}
              total={pageCount}
              onChange={setPage}
            />
          )}
        </div>
      </div>
      <div className={"grid grid-cols-7"}>
        {fsEntries.slice(skipCount, skipCount + PageSize).map((e) => (
          <FileSystemEntry key={e.path} entry={e} onEnterDirectory={changePath} />
        ))}
      </div>
      {error && (
        <p role="alert" className="text-xs text-danger">
          {error}
        </p>
      )}
      <div className={"flex items-center justify-end gap-2"}>
        {showPagination && (
          <Pagination boundaries={3} page={page} size={"sm"} total={pageCount} onChange={setPage} />
        )}
        {nextCursor && (
          <Button
            size="sm"
            variant="light"
            isLoading={loading}
            isDisabled={loading}
            onPress={() => void previewPath(currentPath, nextCursor)}
          >
            {t("management.loadMore")}
          </Button>
        )}
      </div>
    </div>
  );
};

FileSystemEntries.displayName = "FileSystemEntries";

export default FileSystemEntries;
