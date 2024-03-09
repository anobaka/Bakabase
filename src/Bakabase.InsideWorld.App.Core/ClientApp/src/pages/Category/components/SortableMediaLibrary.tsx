import React, { useCallback, useEffect, useReducer, useState } from 'react';
import i18n from 'i18next';
import { Badge, Balloon, Dialog, Dropdown, Input, Menu, Message } from '@alifd/next';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import IceLabel from '@icedesign/label';
import CustomIcon from '@/components/CustomIcon';
import {
  AddMediaLibraryPathConfiguration,
  OpenFolderSelector,
  RemoveMediaLibrary,
  RemoveMediaLibraryEnhancementRecords,
  RemoveMediaLibraryPathConfiguration,
} from '@/sdk/apis';
import DragHandle from '@/components/DragHandle';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import { buildLogger, parseLayerCountFromLayerBasedPathRegexString } from '@/components/utils';
import BApi from '@/sdk/BApi';
import PathConfigurationDialog from '@/pages/Category/components/PathConfigurationDialog';
import ClickableIcon from '@/components/ClickableIcon';
import SimpleLabel from '@/components/SimpleLabel';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import AddRootPathsInBulkDialog from '@/pages/Category/components/AddRootPathsInBulkDialog';

export default (({
                   library,
                   loadAllMediaLibraries,
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

  const style = {
    transform: CSS.Translate.toString({
      ...transform!,
      scaleY: 1,
    }),
    transition,
  };

  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const [pathConfiguration, setPathConfiguration] = useState();
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
    const resourceValue = pc.rpmValues?.find(r => r.property == ResourceProperty.Resource);
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
          <SimpleLabel
            status={'default'}
          >{t(ResourceMatcherValueType[resourceValue.valueType])}</SimpleLabel>
          {valueComponent}
        </div>
      );
    }
    return (<div className={'unset filter'}>{t('Not set')}</div>);
  }, []);

  const renderAdditionalProperties = useCallback(p => {
    const properties = p.rpmValues?.filter((a, j) => j == p.rpmValues.findIndex((b) => b.property == a.property) && a.property != ResourceProperty.Resource) || [];
    return properties.length > 0 ? properties.map((s) => t(ResourceProperty[s.property]))
      .join(',') : (<div className={'unset'}>{t('Not set')}</div>);
  }, []);

  return (
    <div
      className={'category-page-draggable-media-library libraries-grid'}
      ref={setNodeRef}
      style={style}
    >
      <PathConfigurationDialog
        onSaved={() => loadAllMediaLibraries()}
        library={library}
        afterClose={() => {
          setPathConfiguration(undefined);
        }}
        value={pathConfiguration}
      />
      <div className="library">
        <DragHandle {...listeners} {...attributes} />
        <div className="name">
          <div
            className={'edit'}
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
            {library.name}
          </div>
          <div className="opt">
            <Balloon.Tooltip
              trigger={(
                <Badge
                  count={library.resourceCount}
                  overflowCount={9999999}
                  className={'count'}
                />
              )}
              triggerType={'hover'}
              align={'t'}
            >
              {t('Count of resources')}
            </Balloon.Tooltip>
            <Dropdown
              trigger={(
                <ClickableIcon
                  colorType={'normal'}
                  type={'plus-circle'}
                  onClick={() => {
                    FileSystemSelectorDialog.show({
                      targetType: 'folder',
                      onSelected: e => {
                        AddMediaLibraryPathConfiguration({
                          id: library.id,
                          model: {
                            path: e.path,
                          },
                        })
                          .invoke((b) => {
                            if (!b.code) {
                              loadAllMediaLibraries();
                            }
                          });
                      },
                    });
                  }}
                />
              )}
              triggerType={['hover']}
            >
              <Menu>
                <Menu.Item
                  onClick={() => {
                    AddRootPathsInBulkDialog.show({ libraryId: library.id, onSubmitted: () => loadAllMediaLibraries() });
                  }}
                >
                  <CustomIcon type="playlist_add" />
                  {t('Add root paths in bulk')}
                </Menu.Item>
              </Menu>
            </Dropdown>
            <Dropdown
              trigger={(
                <ClickableIcon
                  colorType={'normal'}
                  type={'ellipsis-circle'}
                />
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
                        RemoveMediaLibraryEnhancementRecords({
                          id: library.id,
                        })
                          .invoke((a) => {
                            if (!a.code) {
                              resolve(a);
                            }
                          });
                      })),
                    });
                  }}
                >
                  <CustomIcon type="flashlight" />
                  {t('Remove all enhancement records')}
                </Menu.Item>
                <Menu.Item
                  className={'warning'}
                  onClick={() => {
                    Dialog.confirm({
                      title: `${t('Deleting')} ${library.name}`,
                      closeable: true,
                      onOk: () => new Promise(((resolve, reject) => {
                        RemoveMediaLibrary({
                          id: library.id,
                        })
                          .invoke((a) => {
                            if (!a.code) {
                              loadAllMediaLibraries();
                              resolve(a);
                            }
                          });
                      })),
                    });
                  }}
                >
                  <CustomIcon type="delete" />
                  {t('Remove')}
                </Menu.Item>
              </Menu>
            </Dropdown>
          </div>
        </div>
      </div>
      <div className="path-configurations">
        {library.pathConfigurations?.map((p, i) => {
          return (
            <div
              className={'path-configuration item'}
              key={i}
              onClick={() => {
                setPathConfiguration(p);
              }}
            >
              <div className="path">
                <span>
                  {p.path}
                </span>
                <SimpleLabel status={'default'}>
                  {library.fileSystemInformation?.[p.path]?.freeSpaceInGb}GB
                </SimpleLabel>
                <ClickableIcon
                  type="delete"
                  colorType={'danger'}
                  onClick={(e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    Dialog.confirm({
                      title: `${t('Deleting')} ${p.path}`,
                      closeable: true,
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
                <ClickableIcon
                  colorType={'normal'}
                  type="folder-open"
                  onClick={(e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    BApi.tool.openFileOrDirectory({ path: p.path });
                  }}
                />
              </div>
              {renderFilter(p)}
              <div className={`tags fixed ${p.fixedTags?.length > 0 ? '' : 'not-set'}`}>
                {p.fixedTags?.length > 0 ? p.fixedTags.map((b) => {
                  return (
                    <div className={'tag'} key={b.id}>{b.groupName ? `${b.groupName}:${b.name}` : b.name}</div>
                  );
                }) : (<div className={'unset'}>{t('Not set')}</div>)}
              </div>
              <div className="segment-configurations">
                {renderAdditionalProperties(p)}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
});
