import React, { useCallback, useEffect } from 'react';
import { Dialog, Dropdown, Input, Menu, Message } from '@alifd/next';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import {
  DeleteOutlined,
  FolderOpenOutlined,
  PlusCircleOutlined,
  QuestionCircleOutlined,
  SyncOutlined,
  UnorderedListOutlined,
} from '@ant-design/icons';
import { useUpdate } from 'react-use';
import { AutoTextSize } from 'auto-text-size';
import CustomIcon from '@/components/CustomIcon';
import DragHandle from '@/components/DragHandle';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';
import PathConfigurationDialog from '@/pages/Category/components/PathConfigurationDialog';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import AddRootPathsInBulkDialog from '@/pages/Category/components/AddRootPathsInBulkDialog';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Button, Chip, Modal, Tooltip } from '@/components/bakaui';

export default (({
                   library,
                   loadAllMediaLibraries,
                   reloadMediaLibrary,
                 }) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
  } = useSortable({ id: library.id });

  const log = buildLogger('SortableMediaLibrary');

  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const style = {
    transform: CSS.Translate.toString({
      ...transform!,
      scaleY: 1,
    }),
    transition,
  };

  const forceUpdate = useUpdate();
  useEffect(() => {

  }, []);

  // useTraceUpdate({
  //   library,
  //   loadAllMediaLibraries,
  //   regexInputVisible,
  //   testResult,
  //   checkingPathRelations,
  //   relativeLibraries,
  //   pathConfiguration
  // }, 'MediaLibrary')

  const renderFilter = useCallback((pc: any) => {
    const resourceValue = pc.rpmValues?.find(r => !r.isCustomProperty && r.propertyId == ResourceProperty.Resource);
    let valueComponent: any;
    if (resourceValue) {
      switch (resourceValue.valueType) {
        case ResourceMatcherValueType.Layer:
          valueComponent = (
            <div>{t(resourceValue.layer > 0 ? 'The {{layer}} layer after root path' : 'The {{layer}} layer to the resource', { layer: Math.abs(resourceValue.layer) })}</div>
          );
          break;
        case ResourceMatcherValueType.Regex:
          valueComponent = (
            <div>{resourceValue.regex}</div>
          );
          break;
        case ResourceMatcherValueType.FixedText:
          valueComponent = (
            <div>{resourceValue.fixedText}</div>
          );
          break;
      }
    }
    if (valueComponent) {
      return (
        <div className={'filter'}>
          <Chip
            size={'sm'}
            radius={'sm'}
          >{t(ResourceMatcherValueType[resourceValue.valueType])}</Chip>
          {valueComponent}
        </div>
      );
    }
    return (<div className={'unset filter'}>{t('Not set')}</div>);
  }, []);

  const renderCustomProperties = useCallback(p => {
    const properties = p.rpmValues?.filter((a, j) => j == p.rpmValues.findIndex((b) => b.isCustomProperty && b.propertyId == a.propertyId)) || [];
    return properties.length > 0 ? properties.map((s) => s.customProperty?.name)
      .map(n => {
        return (
          <Chip
            size={'sm'}
            radius={'sm'}
          >
            {n ?? t('Unknown property')}
          </Chip>
        );
      }) : t('Not set');
  }, []);

  const renderAddRootPathInBulkModal = () => {
    AddRootPathsInBulkDialog.show({
      libraryId: library.id,
      onSubmitted: () => loadAllMediaLibraries(),
    });
  };

  const renderAddRootPathModal = () => {
    FileSystemSelectorDialog.show({
      targetType: 'folder',
      onSelected: e => {
        BApi.mediaLibrary.addMediaLibraryPathConfiguration(library.id,
          {
            path: e.path,
          })
          .then((b) => {
            if (!b.code) {
              loadAllMediaLibraries();
            }
          });
      },
    });
  };

  return (
    <div
      className={'category-page-draggable-media-library libraries-grid'}
      ref={setNodeRef}
      style={style}
    >
      <div className="library">
        <DragHandle {...listeners} {...attributes} />
        <div className="flex items-center gap-1">
          <div
            className={'edit relative'}
            onClick={() => {
              let n = library.name;
              Dialog.show({
                title: t('Change name'),
                content: (<Input
                  size={'large'}
                  style={{ width: '100%' }}
                  defaultValue={n}
                  onChange={(v) => {
                    n = v;
                  }}
                />),
                style: { width: 800 },
                onOk: () => {
                  return new Promise(((resolve, reject) => {
                    if (n?.length > 0) {
                      BApi.mediaLibrary.patchMediaLibrary(library.id,
                        {
                          name: n,
                        },
                      )
                        .then((t) => {
                          if (!t.code) {
                            resolve(t);
                            library.name = n;
                            forceUpdate();
                          } else {
                            reject();
                          }
                        });
                    } else {
                      Message.error(t('Invalid data'));
                    }
                  }));
                },
                closeable: true,
              });
            }}
          >
            <AutoTextSize maxFontSizePx={14}>
              {library.name}
            </AutoTextSize>
            {library.resourceCount > 0 ? (
              <Tooltip
                content={t('Count of resources')}
              >
                <Chip
                  color={'success'}
                  size={'sm'}
                  variant={'light'}
                  className={'p-0 absolute right-[-14px] top-[-14px]'}
                >
                  {library.resourceCount}
                </Chip>
              </Tooltip>
            ) : (
              library.pathConfigurations?.length > 0 && (
                <Tooltip content={t('Resource not found? Please try to perform the synchronization operation.')}>
                  <QuestionCircleOutlined className={'text-base'} />
                </Tooltip>
              )
            )}
          </div>
          <div className={'pl-2'}>
            <Tooltip content={t('Sync current media library')}>
              <Button
                isIconOnly
                variant={'light'}
                color={'secondary'}
                size={'sm'}
                onClick={() => {
                  BApi.mediaLibrary.startSyncingMediaLibraryResources(library.id);
                }}
                className={'w-auto min-w-fit px-1'}
              >
                <SyncOutlined className={'text-base'} />
              </Button>
            </Tooltip>
            <Dropdown
              trigger={(
                <Button
                  variant={'light'}
                  size={'sm'}
                  isIconOnly
                  className={'w-auto min-w-fit px-1'}
                >
                  <PlusCircleOutlined
                    className={'text-base'}
                    onClick={() => {
                      renderAddRootPathModal();
                    }}
                  />
                </Button>
              )}
              triggerType={['hover']}
            >
              <Menu>
                <Menu.Item
                  onClick={() => {
                    renderAddRootPathInBulkModal();
                  }}
                >
                  <CustomIcon
                    type="playlist_add"
                    className={'text-base'}
                  />
                  {t('Add root paths in bulk')}
                </Menu.Item>
              </Menu>
            </Dropdown>
            <Dropdown
              trigger={(
                <Button
                  variant={'light'}
                  size={'sm'}
                  isIconOnly
                  className={'w-auto min-w-fit px-1'}
                >
                  <UnorderedListOutlined
                    className={'text-base'}
                  />
                </Button>

              )}
              className={'category-page-media-library-more-operations-popup'}
              triggerType={['click']}
            >
              <Menu>
                <Menu.Item
                  className={'warning'}
                  onClick={() => {
                    Dialog.confirm({
                      title: `${t('Removing all enhancement records of resources under this media library')}`,
                      closeable: true,
                      onOk: () => new Promise(((resolve, reject) => {
                        BApi.mediaLibrary.deleteByEnhancementsMediaLibrary(library.id)
                          .then((a) => {
                            if (!a.code) {
                              resolve(a);
                            }
                          });
                      })),
                    });
                  }}
                >
                  <CustomIcon
                    type="flashlight"
                    className={'text-base'}
                  />
                  {t('Remove all enhancement records')}
                </Menu.Item>
                <Menu.Item
                  className={'warning'}
                  onClick={() => {
                    Dialog.confirm({
                      title: `${t('Deleting')} ${library.name}`,
                      closeable: true,
                      onOk: () => new Promise(((resolve, reject) => {
                        BApi.mediaLibrary.deleteMediaLibrary(library.id)
                          .then((a) => {
                            if (!a.code) {
                              loadAllMediaLibraries();
                              resolve(a);
                            }
                          });
                      })),
                    });
                  }}
                >
                  <CustomIcon
                    type="delete"
                    className={'text-base'}
                  />
                  {t('Remove')}
                </Menu.Item>
              </Menu>
            </Dropdown>
          </div>
        </div>
      </div>
      <div className="path-configurations">
        {library.pathConfigurations?.length > 0 ? library.pathConfigurations?.map((p, i) => {
          return (
            <div
              className={'path-configuration item'}
              key={i}
              onClick={() => {
                createPortal(PathConfigurationDialog, {
                  onClosed: (pc) => {
                    Object.assign(library.pathConfigurations[i], pc);
                    loadAllMediaLibraries();
                  },
                  libraryId: library.id,
                  pcIdx: i,
                });
              }}
            >
              <div className="flex items-center">
                <span>
                  {p.path}
                </span>
                <Chip
                  size={'sm'}
                  radius={'sm'}
                  color={'success'}
                  variant={'light'}
                >
                  {library.fileSystemInformation?.[p.path]?.freeSpaceInGb}GB
                </Chip>
                <div className={'flex items-center'}>
                  <Button
                    size={'sm'}
                    isIconOnly
                    variant={'light'}
                    className={'w-auto min-w-fit px-1'}
                  >
                    <FolderOpenOutlined
                      className={'text-base'}
                      onClick={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        BApi.tool.openFileOrDirectory({ path: p.path });
                      }}
                    />
                  </Button>
                  <Button
                    size={'sm'}
                    isIconOnly
                    variant={'light'}
                    color={'danger'}
                    className={'w-auto min-w-fit px-1'}
                  >
                    <DeleteOutlined
                      className={'text-base'}
                      onClick={(e) => {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: `${t('Deleting')} ${p.path}`,
                          onOk: async () => {
                            const rsp = await BApi.mediaLibrary.removeMediaLibraryPathConfiguration(library.id, {
                              index: i,
                            });
                            if (rsp.code) {
                              throw new Error(rsp.message!);
                            } else {
                              loadAllMediaLibraries();
                            }
                          },
                        });
                      }}
                    />
                  </Button>
                </div>
              </div>
              {renderFilter(p)}
              <div className="flex flex-wrap gap-1">
                {renderCustomProperties(p)}
              </div>
            </div>
          );
        }) : (
          <div className={'flex flex-col gap-2'}>
            <div className={'text-center'}>
              {t('To get your resources loaded, you must add at least one root path containing your local resources to this media library')}
            </div>
            <div
              className={'flex items-center gap-4 justify-center'}
            >
              <Button
                size={'sm'}
                color={'primary'}
                onClick={() => {
                  renderAddRootPathModal();
                }}
              >
                {t('Add root path')}
              </Button>
              <Button
                size={'sm'}
                color={'secondary'}
                onClick={() => {
                  renderAddRootPathInBulkModal();
                }}
              >
                {t('Add root paths in bulk')}
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
});
