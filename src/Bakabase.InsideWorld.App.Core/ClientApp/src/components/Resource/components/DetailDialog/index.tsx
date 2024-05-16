import path from 'path';
import React, { useCallback, useEffect, useState } from 'react';
import {
  DatePicker2,
  Dialog,
  Input,
  Loading,
  Message,
  NumberPicker,
  Pagination,
  Range,
  Select,
} from '@alifd/next';
import i18n from 'i18next';

import './index.scss';
import dayjs from 'dayjs';
import { useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import { DeleteOutlined, FolderOpenOutlined, PlayCircleOutlined } from '@ant-design/icons';
import { IwFsType, ResourceLanguage, resourceLanguages } from '@/sdk/constants';
import Property from '@/components/Resource/components/DetailDialog/PropertyValue';
import PublisherProperty from '@/components/Resource/components/DetailDialog/PublisherPropertyValue';
import { PlayFileURL, SearchResources } from '@/sdk/apis';
import TagList from '@/components/Resource/components/DetailDialog/TagPropertyValue';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import serverConfig from '@/serverConfig';
import CustomIcon from '@/components/CustomIcon';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import Resource from '@/components/Resource';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import { createPortalOfComponent } from '@/components/utils';
import BApi from '@/sdk/BApi';
import CustomPropertyValue from '@/components/Resource/components/DetailDialog/CustomPropertyValue';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import { ButtonGroup, Button, Chip, Modal, Divider } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';


interface Props extends DestroyableProps{
  dialogProps?: any;
  resource: ResourceModel;
  onReloaded: (resource: any) => any;
  onPlay?: () => void;
  onOpen?: () => void;
  onRemove?: () => void;
  noPlayableFile?: boolean;
  onTagSearch?: (tagId: number, append: boolean) => void;
  ct: AbortSignal;
}

const ResourceDetailDialog = ({
                                resource: propsResource,
                                onReloaded = (resource: any) => {
                                },
                                onPlay = () => {
                                },
                                onOpen = () => {
                                },
                                onRemove = () => {
                                },
                                noPlayableFile = false,
                                onTagSearch = (tagId: number, append: boolean) => {
                                },
                                ct,
  ...props
                              }: Props) => {
  const { t } = useTranslation();

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

  const [resource, setResource] = useState(propsResource);

  const reload = async (cb?: any) => {
    const newResourceRsp = await BApi.resource.getResourcesByKeys({ ids: [resource.id] });
    const newResource = newResourceRsp.data![0];
    setResource(newResource);
    if (cb) {
      cb();
    }
    onReloaded(newResource);
  };

  const [visible, setVisible] = useState(true);

  useEffect(() => {
    setPreviewingPath(resource.path);

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
    BApi.file.getChildrenIwFsInfo({
      root: previewingPath,
    })
      .then((a) => {
        // @ts-ignore
        setFilesystemEntries(a.data?.entries ?? []);
      })
      .finally(() => {
        setLoadingFiles(false);
      });
  };

  useUpdateEffect(() => {
    previewCurrentPath();
  }, [previewingPath]);

  const renderProperties = () => {
    const propertyComponents: {
      label: string;
      component?: React.ReactNode;
    }[] = [];

    const cps = resource.customPropertiesV2 ?? [];
    const cpvs = resource.customPropertyValues ?? [];

    if (cps.length > 0) {
      cps.forEach((property, idx) => {
        propertyComponents.push({
          label: property.name!,
          component: (
            <CustomPropertyValue
              resourceId={resource.id}
              property={property}
              value={cpvs[idx]?.value}
              onSaved={reload}
            />
          ),
        });
      });
    }

    const dateTimes = [
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

    dateTimes.forEach((dateTime) => {
      propertyComponents.push({
        label: t(dateTime.label),
        component: (
          <Property
            renderValue={() => dayjs(resource[dateTime.key])
              .format('YYYY-MM-DD HH:mm:ss')}
          />
        ),
      });
    });
    return propertyComponents
      .map((a) => (
        <>
          <Chip
            size={'sm'}
            radius={'sm'}
          >
            {a.label}
          </Chip>
          <div>{a.component}</div>
        </>
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
    if (!resource.isFile && previewingPath) {
      relativePathSegments = previewingPath.replace(resource.path, '')
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
                {t('Files')}
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
                            let segments = [resource.path];
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
                onProcess={(v) => {
                  if (typeof v == 'number') {
                    setFsEntriesColumnCount(v);
                  }
                }}
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
            className={`grid grid-cols-${fsEntriesColumnCount} gap-1`}
            // style={{ gridTemplateColumns: `repeat(${fsEntriesColumnCount}, minmax(0, 1fr))` }}
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
          <div className="text-lg">
            {t('Children resources')}
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
          <div className={`grid grid-cols-${fsEntriesColumnCount}`} style={{}}>
            {currentChildrenResources.map((a) => {
              return (
                <Resource
                  resource={a}
                  ct={ct}
                />
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
    return;
  };


  const close = useCallback(() => {
    setVisible(false);
  }, []);

  return (
    <Modal
      size={'xl'}
      footer={{
        actions: ['ok'],
      }}
      defaultVisible
      onDestroyed={props.onDestroyed}
      title={resource.displayName}
    >
      <div className="flex items-start gap-5">
        <div className="rounded flex items-center justify-center w-[400px] h-[400px] max-w-[400px] max-h-[400px] overflow-hidden">
          <ResourceCover resourceId={resource.id} />
        </div>
        <div className="right">
          <div
            className={'grid gap-2'}
            style={{ gridTemplateColumns: 'auto 1fr' }}
          >
            {renderProperties()}
          </div>

          <div className={'mt-5'}>
            <ButtonGroup size={'sm'}>
              <Button
                color="primary"
                onClick={() => !noPlayableFile && onPlay()}
                disabled={noPlayableFile}
              >
                <PlayCircleOutlined />
                {t('Play')}
              </Button>
              <Button
                color="default"
                onClick={() => onOpen()}
              >
                <FolderOpenOutlined />
                {t('Open')}
              </Button>
              <Button
                color={'danger'}
                onClick={() => onRemove()}
              >
                <DeleteOutlined />
                {t('Remove')}
              </Button>
            </ButtonGroup>
          </div>
        </div>
      </div>
      <Divider />
      {renderChildren()}
      {renderFileEntries()}
    </Modal>
  );
};

ResourceDetailDialog.show = (props: Props) => createPortalOfComponent(ResourceDetailDialog, props);

export default ResourceDetailDialog;
