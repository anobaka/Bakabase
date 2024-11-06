import { FolderOutlined, PlayCircleOutlined } from '@ant-design/icons';
import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useState } from 'react';
import { Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import toast from 'react-hot-toast';
import { Button, Chip, Modal } from '@/components/bakaui';
import { splitPathIntoSegments, standardizePath } from '@/components/utils';
import BApi from '@/sdk/BApi';
import BusinessConstants from '@/components/BusinessConstants';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import store from '@/store';

type Props = {
  resource: ResourceModel;
  PortalComponent: React.FC<{ onClick: () => any }>;
  autoInitialize?: boolean;
};

type Directory = {
  relativePath: string;
  groups: Group[];
};

type Group = {
  extension: string;
  files: File[];
  showAll?: boolean;
};

type File = {
  name: string;
  path: string;
};

type PlayableFilesCtx = {
  files: string[];
  hasMore: boolean;
};

const splitIntoDirs = (paths: string[], prefix: string): Directory[] => {
  const groups: Directory[] = [];
  const stdPrefix = standardizePath(prefix)!;
  for (const path of paths) {
    const stdPath = standardizePath(path)!;
    const relativePath = stdPath.replace(stdPrefix, '');
    const segments = splitPathIntoSegments(relativePath);
    const relativeDir = segments.slice(0, segments.length - 1).join(BusinessConstants.pathSeparator);
    let dir = groups.find(g => g.relativePath == relativeDir);
    if (!dir) {
      dir = {
        relativePath: relativeDir,
        groups: [],
      };
      groups.push(dir);
    }
    const extension = segments[segments.length - 1].split('.').pop()!;
    let group = dir.groups.find(g => g.extension == extension);
    if (!group) {
      group = {
        extension,
        files: [],
      };
      dir.groups.push(group);
    }
    group.files.push({
      name: segments[segments.length - 1],
      path,
    });
  }
  return groups;
};

const DefaultVisibleFileCount = 5;

export type PlayableFilesRef = {
  initialize: () => Promise<void>;
};

export default forwardRef<PlayableFilesRef, Props>(({
                                                      autoInitialize,
                                                      resource,
                                                      PortalComponent,
                                                    }, ref) => {
  const { t } = useTranslation();
  const useCache = !store.useModelState('uiOptions').resource?.disableCache;

  const [portalCtx, setPortalCtx] = useState<PlayableFilesCtx>();
  const [dirs, setDirs] = useState<Directory[]>();
  const [modalVisible, setModalVisible] = useState(false);

  const initialize = useCallback(async () => {
    if (useCache) {
      setPortalCtx({
        files: resource.cache?.playableFilePaths ?? [],
        hasMore: resource.cache?.hasMorePlayableFiles ?? false,
      });
    } else {
      await BApi.resource.getResourcePlayableFiles(resource.id).then((a) => {
        setPortalCtx({
          files: a.data || [],
          hasMore: false,
        });
        setDirs(splitIntoDirs(a.data || [], resource.path));
      });
    }
  }, [useCache]);

  useImperativeHandle(ref, () => ({
    initialize,
  }), [initialize]);

  useEffect(() => {
    if (autoInitialize) {
      initialize();
    }
  }, []);

  const play = (file: string) =>
    BApi.resource.playResourceFile(resource.categoryId, {
      file,
    }).then((a) => {
      if (!a.code) {
        toast.success(t('Opened'));
      }
    });

  if (portalCtx?.files && portalCtx.files.length > 0) {
    return (
      <>
        <PortalComponent
          onClick={() => {
            if (portalCtx.files.length == 1 && !portalCtx.hasMore) {
              play(portalCtx.files[0]);
            } else {
              if (dirs) {
                setModalVisible(true);
              } else {
                BApi.resource.getResourcePlayableFiles(resource.id).then((a) => {
                  setDirs(splitIntoDirs(a.data || [], resource.path));
                  setModalVisible(true);
                });
              }
            }
          }}
        />
        <Modal
          visible={modalVisible}
          onClose={() => {
            setModalVisible(false);
          }}
          footer={false}
          size={'lg'}
          title={t('Please select a file to play')}
        >
          <div className={'flex flex-col gap-2 pb-2'}>
            {dirs?.map(d => {
              return (
                <div>
                  {dirs.length > 1 && (
                    <div className={'flex items-center'}>
                      <FolderOutlined className={'text-base'} />
                      <Chip
                        size={'sm'}
                        variant={'light'}
                        radius={'sm'}
                      >
                        {d.relativePath}
                      </Chip>
                    </div>
                  )}
                  <div className={'flex gap-1 flex-col'}>
                    {d.groups.map(g => {
                      const showCount = g.showAll ? g.files.length : Math.min(DefaultVisibleFileCount, g.files.length);
                      return (
                        <div className={'flex flex-wrap items-center gap-1'}>
                          {d.groups.length > 1 && (
                            <Chip
                              size={'sm'}
                              radius={'sm'}
                              variant={'flat'}
                            >
                              {g.extension}
                            </Chip>
                          )}
                          {g.files.slice(0, showCount).map((file) => {
                            return (
                              <Button
                                radius={'sm'}
                                size={'sm'}
                                className={'whitespace-break-spaces py-2 h-auto text-left'}
                                onClick={() => {
                                  BApi.resource.playResourceFile(resource.categoryId, {
                                    file: file.path,
                                  }).then((a) => {
                                    if (!a.code) {
                                      Message.success(t('Opened'));
                                    }
                                  });
                                }}
                              >
                                <PlayCircleOutlined className={'text-base'} />
                                <span className={'break-all overflow-hidden text-ellipsis'}>{file.name}</span>
                              </Button>
                            );
                          })}
                          {g.files.length > DefaultVisibleFileCount && !g.showAll && (
                            <Button
                              size={'sm'}
                              color={'primary'}
                              variant={'light'}
                              onClick={() => {
                                g.showAll = true;
                                setDirs([...dirs]);
                              }}
                            >
                              {t('Show all {{count}} files', { count: g.files.length })}
                            </Button>
                          )}
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        </Modal>
      </>
    );
  }
  return null;
});
