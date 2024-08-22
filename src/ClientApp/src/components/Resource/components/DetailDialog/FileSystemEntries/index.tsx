import * as PathLibrary from 'path';
import React, { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import FileSystemEntry from './FileSystemEntry';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import BApi from '@/sdk/BApi';
import { ResponseCode } from '@/sdk/constants';
import { Pagination, Spinner } from '@/components/bakaui';

type Props = {
  path: string;
  isFile: boolean;
};

const PageSize = 36;

export default ({
                  path,
                  isFile,
                }: Props) => {
  const { t } = useTranslation();
  const [currentPath, setCurrentPath] = useState(path);
  const [fsEntries, setFsEntries] = useState<Entry[]>([]);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);

  const previewPath = (path: string) => {
    setLoading(true);
    BApi.file.getChildrenIwFsInfo({
      root: path,
      // @ts-ignore
    }, { ignoreError: r => r.code == ResponseCode.NotFound })
      .then((a) => {
        // @ts-ignore
        setFsEntries(a.data?.entries ?? []);
      })
      .finally(() => {
        setLoading(false);
      });
  };

  useEffect(() => {
    previewPath(currentPath);
  }, []);

  const skipCount = (page - 1) * PageSize;
  const pageCount = Math.ceil(fsEntries.length / PageSize);
  const showPagination = pageCount > 1;

  let relativePathSegments: string[] = [];
  if (!isFile && currentPath) {
    relativePathSegments = currentPath.replace(path, '')
      .replace(/\\/g, '/')
      .split('/')
      .filter((a) => a);
    if (relativePathSegments.length > 0) {
      relativePathSegments.splice(0, 0, '.');
    }
  }

  const changePath = useCallback((path: string) => {
    setCurrentPath(path);
    previewPath(path);
  }, []);

  return (
    <div>
      <div>
        <span className={'text-base'}>
          {t('Files')}
        </span>
        {loading && (
          <Spinner size={'sm'} />
        )}
      </div>
      <div className={'flex items-center justify-between'}>
        <div>
          {relativePathSegments.length > 0 && relativePathSegments.map((s, i) => {
            return (
              <>
                <span
                  className={'hover:font-bold'}
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
                >{s}
                </span>
                {i < relativePathSegments.length - 1 && (
                  <span className={'opacity-60'}>/</span>
                )}
              </>
            );
          })}
        </div>
        <div>
          {showPagination && (
            <Pagination
              size={'sm'}
              boundaries={3}
              total={pageCount}
              page={page}
              onChange={setPage}
            />
          )}
        </div>
      </div>
      <div className={'grid grid-cols-7'}>
        {fsEntries.slice(skipCount, PageSize).map(e => (
          <FileSystemEntry
            key={e.path}
            entry={e}
            onEnterDirectory={changePath}
          />
        ))}
      </div>
      <div className={'flex items-center justify-end'}>
        {showPagination && (
          <Pagination
            size={'sm'}
            boundaries={3}
            total={pageCount}
            page={page}
            onChange={setPage}
          />
        )}
      </div>
    </div>
  );
};
