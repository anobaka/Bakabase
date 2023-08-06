import path from 'path';
import React, { useEffect, useState } from 'react';
import { Button, DatePicker2, Dialog, Input, Loading, Message, NumberPicker, Pagination, Range, Select } from '@alifd/next';
import i18n from 'i18next';

import './index.scss';
import dayjs from 'dayjs';
import { useUpdateEffect } from 'react-use';
import { IwFsType, ResourceLanguage, resourceLanguages } from '@/sdk/constants';
import Property from '@/components/Resource/components/DetailDialog/PropertyValue';
import PublisherProperty from '@/components/Resource/components/DetailDialog/PublisherPropertyValue';
import { GetResourceFiles, PlayFileURL, PreviewPath, SearchResources } from '@/sdk/apis';
import TagList from '@/components/Resource/components/DetailDialog/TagPropertyValue';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import serverConfig from '@/serverConfig';
import CustomIcon from '@/components/CustomIcon';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import Resource from '@/components/Resource';
import ResourceCover from '@/components/Resource/components/ResourceCover';

export default ({
  dialogProps = {},
  resource,
  coverComponent,
  reloadResource = (cb) => {
  },
  onPlay = () => {
  },
  onOpen = () => {
  },
  onRemove = () => {
  },
  noPlayableFile = false,
} = {}) => {
  const [filesystemEntries, setFilesystemEntries] = useState<Entry[]>([]);
  const [filesystemEntriesForm, setFilesystemEntriesForm] = useState({
    pageIndex: 1,
    pageSize: 100,
  });
  const [loadingFiles, setLoadingFiles] = useState(false);
  const [previewingPath, setPreviewingPath] = useState<string | undefined>();
  const [fsEntriesColumnCount, setFsEntriesColumnCount] = useState<number>(8);

  const [childrenResources, setChildrenResources] = useState([]);
  const [childrenForm, setChildrenForm] = useState({
    pageIndex: 1,
    pageSize: 20,
  });

  useEffect(() => {
    setPreviewingPath(resource.rawFullname);

    console.log(resource);

    if (resource.hasChildren > 0) {
      SearchResources({
        model: {
          parentId: resource.id,
          pageSize: 100,
        },
      })
        .invoke((a) => {
          setChildrenResources(a.data);
        });
    }
  }, []);

  const previewCurrentPath = () => {
    if (!previewingPath) {
      return;
    }
    setLoadingFiles(true);
    PreviewPath({
      path: previewingPath,
    })
      .invoke((a) => {
        setFilesystemEntries(a.data.entries);
      })
      .finally(() => {
        setLoadingFiles(false);
      });
  };

  useUpdateEffect(() => {
    previewCurrentPath();
  }, [previewingPath]);

  const renderOtherProperties = () => {
    const propertyComponents = {
      'Release Date': (
        <Property
          label={'Release Date'}
          requestKey={'releaseDt'}
          renderValue={() => (resource.releaseDt ? dayjs(resource.releaseDt)
            .format('YYYY-MM-DD') : '')}
          initValue={resource.releaseDt ? dayjs(resource.releaseDt) : undefined}
          editable
          EditComponent={DatePicker2}
          editComponentProps={{
            showTime: false,
            popupProps: { v2: true },
            hasClear: true,
          }}
          convertToRequesting={(dj) => dj.format('YYYY-MM-DD')}
          resourceId={resource.id}
          reloadResource={reloadResource}
        />
      ),
      Publishers: (
        <PublisherProperty
          resource={resource}
          reloadResource={reloadResource}
        />),
      Series: (
        <Property
          label={'Series'}
          renderValue={() =>
            resource.series?.name && (
              <span
                className={'series'}
                onClick={() => {
                  Message.notice(i18n.t('Under developing'));
                }}
              >{resource.series?.name}
              </span>
            )}
          resourceId={resource.id}
          reloadResource={reloadResource}
          initValue={resource.series?.name}
          requestKey={'series'}
          EditComponent={Input}
          editComponentProps={{
            hasClear: true,
          }}
          editable
        />),
      Language: (
        <Property
          label={'Language'}
          renderValue={() => i18n.t(ResourceLanguage[resource.language])}
          resourceId={resource.id}
          reloadResource={reloadResource}
          initValue={resource.language}
          requestKey={'language'}
          EditComponent={Select}
          editComponentProps={{
            autoWidth: true,
            style: { width: 180 },
            dataSource: resourceLanguages.map((t) => ({
              label: i18n.t(t.label),
              value: t.value,
            })),
          }}
          editable
        />
      ),
      Original: (
        <Property
          label={'Original'}
          renderValue={() => resource.originals?.map((o) => (
            <span
              className={'original'}
              onClick={() => {
                Message.notice(i18n.t('Under developing'));
              }}
            >{o.name}
            </span>
          ))}
          resourceId={resource.id}
          reloadResource={reloadResource}
          initValue={resource.originals?.map((t, i) => t.name) || []}
          requestKey={'originals'}
          EditComponent={Select}
          editComponentProps={{
            mode: 'tag',
            placeholder: i18n.t('Add a original'),
          }}
          defaultValueKeyOfEditComponent={'value'}
          editable
        />
      ),
      Rate: (
        <Property
          label={'Rate'}
          renderValue={() => resource.rate}
          resourceId={resource.id}
          reloadResource={reloadResource}
          initValue={resource.rate}
          requestKey={'rate'}
          EditComponent={NumberPicker}
          editComponentProps={{
            max: 5,
            precision: 2,
          }}
          editable
        />
      ),
      // Tags: (
      //   <TagPropertyValue resource={resource} reloadResource={reloadResource} />
      // ),
    };

    if (resource.customProperties) {
      const keys = Object.keys(resource.customProperties);
      if (keys.length > 0) {
        keys.forEach((k) => {
          if (!(k in propertyComponents)) {
            propertyComponents[k] = (
              <Property
                label={k}
                renderValue={() => JSON.stringify(resource.customProperties[k])}
                resourceId={resource.id}
                reloadResource={reloadResource}
                requestKey={k}
                isCustomProperty
              />
            );
          }
        });
      }
    }


    const times = [
      {
        key: 'fileCreateDt',
        label: 'File Add Date',
      },
      {
        key: 'fileModifyDt',
        label: 'File Modify Date',
      },
      {
        key: 'createDt',
        label: 'Resource Create Date',
      },
      {
        key: 'updateDt',
        label: 'Resource Update Date',
      },
    ];

    times.forEach((t) => {
      propertyComponents[t.label] = (
        <Property
          label={t.label}
          renderValue={() => dayjs(resource[t.key])
            .format('YYYY-MM-DD HH:mm:ss')}
        />
      );
    });
    return Object.keys(propertyComponents)
      .map((t) => (
        <div className={'property'}>
          <div className={'label'}>{i18n.t(t)}</div>
          <div className="value-container">
            {propertyComponents[t]}
          </div>
        </div>
      ));
  };

  const renderFileEntries = () => {
    const totalPage = Math.ceil((filesystemEntries?.length ?? 0) / filesystemEntriesForm.pageSize);
    const hasPagination = totalPage > 1;

    const startIndex = filesystemEntriesForm.pageSize * (filesystemEntriesForm.pageIndex - 1);
    const currentPageEntries = filesystemEntries.slice(startIndex, startIndex + filesystemEntriesForm.pageSize);

    const onPageChange = (page) => {
      setFilesystemEntriesForm({
        ...filesystemEntriesForm,
        pageIndex: page,
      });
    };

    let relativePathSegments;
    if (!resource.isSingleFile && previewingPath) {
      relativePathSegments = previewingPath.replace(resource.rawFullname, '')
        .replace(/\\/g, '/')
        .split('/')
        .filter((a) => a);
      if (relativePathSegments.length > 0) {
        relativePathSegments.splice(0, 0, '.');
      }
    }

    return (
      <div className={'file-entries-block'}>
        <Loading visible={loadingFiles}>
          <div className="label">
            <div className="left">
              <span>
                {i18n.t('Files')}
              </span>
              <div className="path-segments">
                {relativePathSegments && relativePathSegments
                  .map((s, i) => {
                    return (
                      <>
                        <span
                          className={'path-segment'}
                          onClick={() => {
                            if (i == relativePathSegments.length - 1) {
                              return;
                            }
                            let segments = [resource.rawFullname];
                            if (i > 0) {
                              segments = segments.concat(relativePathSegments.slice(1, i + 1));
                            }
                            setPreviewingPath(segments.join(path.sep));
                          }}
                        >{s}
                        </span>
                        {i < relativePathSegments.length - 1 && (
                          <span>/</span>
                        )}
                      </>
                    );
                  })}
              </div>
            </div>
            <div className="right">
              <CustomIcon type={'zoom'} size={'small'} />
              <span>{fsEntriesColumnCount}</span>
              <Range
                value={fsEntriesColumnCount}
                min={1}
                max={15}
                onProcess={(v) => setFsEntriesColumnCount(parseInt(v))}
              />
            </div>
          </div>
          {hasPagination && (
            <Pagination
              size={'small'}
              total={filesystemEntries?.length ?? 0}
              pageSize={filesystemEntriesForm.pageSize}
              current={filesystemEntriesForm.pageIndex}
              onChange={onPageChange}
            />
          )}
          <div
            className="entries"
            style={{ gridTemplateColumns: `repeat(${fsEntriesColumnCount}, minmax(0, 1fr))` }}
          >
            {currentPageEntries.map((e) => {
              let comp;
              switch (e.type) {
                case IwFsType.Directory:
                  comp = (
                    <svg aria-hidden="true">
                      <use xlinkHref="#icon-folder1" />
                    </svg>
                  );
                  break;
                case IwFsType.Image:
                  comp = (
                    <img src={`${serverConfig.apiEndpoint}${PlayFileURL({
                      fullname: e.path,
                    })}`}
                    />
                  );
                  break;
                case IwFsType.Invalid:
                  comp = (
                    <CustomIcon type={'close-circle'} size={'small'} />
                  );
                  break;
                case IwFsType.CompressedFileEntry:
                case IwFsType.CompressedFilePart:
                case IwFsType.Symlink:
                case IwFsType.Video:
                case IwFsType.Audio:
                case IwFsType.Unknown:
                  comp = (
                    <FileSystemEntryIcon path={e.path} />
                  );
                  break;
              }

              return (
                <div className={'entry'}>
                  <div className="square">
                    <div
                      className="cover-container"
                      onMouseDown={(evt) => {
                        evt.preventDefault();
                      }}
                      onDoubleClick={(evt) => {
                        evt.preventDefault();
                        evt.stopPropagation();
                        if (e.type == IwFsType.Directory) {
                          setPreviewingPath(e.path);
                        } else {
                          Message.error(i18n.t('Under development'));
                        }
                      }}
                    >
                      {comp}
                    </div>
                  </div>
                  <div className="name">{e.name}</div>
                </div>
              );
            })}
          </div>
          {hasPagination && (
            <Pagination
              size={'small'}
              total={filesystemEntries?.length ?? 0}
              pageSize={filesystemEntriesForm.pageSize}
              current={filesystemEntriesForm.pageIndex}
              onChange={onPageChange}
            />
          )}
        </Loading>
      </div>
    );
  };

  const renderChildren = () => {
    if (resource.hasChildren) {
      const totalPage = Math.ceil((childrenResources?.length ?? 0) / childrenForm.pageSize);
      const hasPagination = totalPage > 1;

      console.log(totalPage);

      const startIndex = childrenForm.pageSize * (childrenForm.pageIndex - 1);
      const currentChildrenResources = childrenResources.slice(startIndex, startIndex + childrenForm.pageSize);

      const onPageChange = (page) => {
        setChildrenForm({
          ...childrenForm,
          pageIndex: page,
        });
      };

      return (
        <div className={'children'}>
          <div className="label">
            {i18n.t('Children resources')}
          </div>
          {hasPagination && (
            <Pagination
              size={'small'}
              total={childrenResources?.length ?? 0}
              pageSize={childrenForm.pageSize}
              current={childrenForm.pageIndex}
              onChange={onPageChange}
            />
          )}
          <div className="resources">
            {currentChildrenResources.map((a) => {
              // resource,
              //   coverHash: propCoverHash,
              //   category = {},
              //   mediaLibrary = {},
              //   openTagsDialog = (id) => {
              //   },
              //   onRemove = (id) => {
              //   },
              //   showCoverOnHover = true,
              //   onRequested = (resource) => {
              //   },
              //   onMove = (resource) => {},
              //   onAddToFavorites = (resource) => {},
              //   searchEngines = [],
              //   index,
              return (
                <Resource
                  resource={a}
                  requestTicketToRequest={() => true}
                />

              // <div className={'item'}>
              //   <div className="square">
              //   </div>
              // </div>
              );
            })}
          </div>
          {hasPagination && (
            <Pagination
              size={'small'}
              total={childrenResources?.length ?? 0}
              pageSize={childrenForm.pageSize}
              current={childrenForm.pageIndex}
              onChange={onPageChange}
            />
          )}
        </div>
      );
    }
  };

  console.log(dialogProps);

  return (
    <Dialog
      closeable
      footerActions={['cancel']}
      {...dialogProps}
      onKeyDown={(e) => {
        // e.preventDefault();
        e.stopPropagation();
      }}
      className={'resource-component-detail-dialog'}
    >
      <div className="top">
        <div className="left">
          {/* {coverComponent} */}
          <ResourceCover
            resourceId={resource.id}
            loadImmediately
          />
        </div>
        <div className="right">
          <div className="property">
            <div className="value-container name">
              <Property
                initValue={resource.name || resource.rawName}
                resourceId={resource.id}
                reloadResource={reloadResource}
                renderValue={() => (
                  resource.name || resource.rawName
                )}
                EditComponent={Input}
                editComponentProps={{
                  size: 'large',
                  style: { width: 600 },
                }}
                editable
                requestKey={'name'}
              />
            </div>
          </div>
          {renderOtherProperties()}
          <div className="property">
            <div className="label">{i18n.t('Tags')}</div>
            <div className="value-container">
              <TagList resource={resource} reloadResource={reloadResource} />
            </div>
          </div>
          <div className="opt">
            <Button.Group>
              <Button
                type="primary"
                onClick={() => !noPlayableFile && onPlay()}
                disabled={noPlayableFile}
              >{i18n.t('Play')}
              </Button>
              <Button
                type="secondary"
                onClick={() => onOpen()}
              >{i18n.t('Open')}
              </Button>
              <Button
                warning
                onClick={() => onRemove()}
              >{i18n.t('Remove')}
              </Button>
            </Button.Group>
          </div>
        </div>
      </div>
      <div className="introduction-container">
        <div className="label">{i18n.t('Introduction')}</div>
        <Property
          initValue={resource.introduction}
          resourceId={resource.id}
          reloadResource={reloadResource}
          renderValue={() => resource.introduction && (
            <pre className={'introduction'}>{resource.introduction}</pre>
          )}
          EditComponent={Input.TextArea}
          editComponentProps={{
            autoHeight: true,
            style: { width: '100%' },
          }}
          editable
          requestKey={'introduction'}
          className={'introduction'}
        />
      </div>
      {renderChildren()}
      {renderFileEntries()}
    </Dialog>
  );
};
