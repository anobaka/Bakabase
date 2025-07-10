import { useTranslation } from 'react-i18next';
import React, { useEffect, useRef, useState } from 'react';
import { useUpdate, useUpdateEffect } from 'react-use';
import { FaRegSave, FaSort } from 'react-icons/fa';
import toast from 'react-hot-toast';
import {
  AiOutlineEdit,
  AiOutlineFolderOpen,
  AiOutlineImport,
  AiOutlinePlusCircle,
  AiOutlineProduct,
  AiOutlineSearch,
} from 'react-icons/ai';
import { MdOutlineDelete } from 'react-icons/md';
import { IoIosSync, IoMdExit } from 'react-icons/io';
import { TbTemplate } from 'react-icons/tb';
import { PiEmpty } from 'react-icons/pi';
import type { Key } from '@react-types/shared';
import type { MediaLibraryTemplate } from '../MediaLibraryTemplate/models';
import SyncStatus from './components/SyncStatus';
import { Button, Chip, Input, Modal, Select, Tooltip, ColorPicker } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import BApi from '@/sdk/BApi';
import { isNotEmpty } from '@/components/utils';
import type { components } from '@/sdk/BApi2';
import PresetTemplateBuilder from '@/pages/MediaLibraryTemplate/components/PresetTemplateBuilder';
import TemplateModal from '@/pages/MediaLibraryTemplate/components/TemplateModal';
import { history } from 'ice';
import { InternalProperty, PropertyPool, SearchOperation } from '@/sdk/constants';
import colors from '@/components/bakaui/colors';
import { buildColorValueString } from '@/components/bakaui/components/ColorPicker';

type MediaLibrary = components['schemas']['Bakabase.Abstractions.Models.Domain.MediaLibraryV2'];

enum SortBy {
  Path = 1,
  Template = 2,
}

const validate = (mls: Partial<MediaLibrary>[]): boolean => {
  return mls.every(ml => isNotEmpty(ml.path) && isNotEmpty(ml.name) && ml.templateId != undefined && ml.templateId > 0);
};

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [mediaLibraries, setMediaLibraries] = useState<MediaLibrary[]>([]);
  const [templates, setTemplates] = useState<MediaLibraryTemplate[]>([]);
  const templatesRef = useRef<Record<number, MediaLibraryTemplate>>({});
  const [sortBy, setSortBy] = useState<SortBy>(SortBy.Path);
  const [editingMediaLibraries, setEditingMediaLibraries] = useState<Partial<MediaLibrary>[]>();
  const forceUpdate = useUpdate();

  const loadMediaLibraries = async () => {
    const r = await BApi.mediaLibraryV2.getAllMediaLibraryV2();
    setMediaLibraries(r.data ?? []);
  };

  const loadTemplates = async () => {
    const r = await BApi.mediaLibraryTemplate.getAllMediaLibraryTemplates();
    const tpls = r.data ?? [];
    // @ts-ignore
    setTemplates(tpls);
    templatesRef.current = tpls.reduce((s, t) => {
      s[t.id] = t;
      return s;
    }, {});
  };

  useEffect(() => {
    loadTemplates();
    loadMediaLibraries();
  }, []);

  useUpdateEffect(() => {
    switch (sortBy) {
      case SortBy.Path:
        mediaLibraries.sort((a, b) => a.path.localeCompare(b.path));
        break;
      case SortBy.Template:
        mediaLibraries.sort((a, b) => (templatesRef.current[a.templateId ?? 0]?.name ?? '').localeCompare(templatesRef.current[b.templateId ?? 0]?.name ?? ''));
        break;
    }
    forceUpdate();
  }, [sortBy, mediaLibraries]);

  const renderPath = (ml: MediaLibrary) => {
    return (
      <Button
        className={'justify-start'}
        size={'sm'}
        color={'default'}
        variant={'flat'}
        onPress={() => BApi.tool.openFileOrDirectory({ path: ml.path })}
      >
        <AiOutlineFolderOpen className={'text-base'} />
        {ml.path}
      </Button>
    );
  };

  // console.log(mediaLibraries);

  return (
    <div className={'h-full flex flex-col'}>
      <div className={'flex items-center justify-between'}>
        <div className={'flex items-center gap-1'}>
          {editingMediaLibraries ? null : (
            <>
              <Button
                size={'sm'}
                color={'primary'}
                onPress={() => setEditingMediaLibraries(mediaLibraries.length == 0 ? [{}]
                  : JSON.parse(JSON.stringify(mediaLibraries)))}
              >
                <AiOutlineEdit className={'text-medium'} />
                {t('Add or edit')}
              </Button>
              <Button
                // variant={'flat'}
                size={'sm'}
                color={'secondary'}
                onPress={() => {
                  BApi.mediaLibraryV2.syncAllMediaLibrariesV2();
                }}
              >
                <IoIosSync className={'text-lg'} />
                {t('Synchronize all media libraries')}
              </Button>
            </>
          )}
        </div>
        {!editingMediaLibraries && (
          <div>
            <Button
              size={'sm'}
              color={'default'}
              variant={'flat'}
              onPress={() => setSortBy(SortBy.Path == sortBy ? SortBy.Template : SortBy.Path)}
            >
              <FaSort className={'text-medium'} />
              {t('Sort by {{sortBy}}', { sortBy: t(SortBy[sortBy]) })}
            </Button>
          </div>
        )}
      </div>
      {editingMediaLibraries ? (
        <>
          <div className={'inline-grid gap-1 mt-2 items-center'} style={{ gridTemplateColumns: 'auto 1fr 2fr 1fr 1fr auto' }}>
            {editingMediaLibraries.map((e, i) => {
              return (
                <>
                  <div className={'flex justify-center items-center'}>
                    #{i + 1}
                  </div>
                  <Input
                    variant="underlined"
                    size={'sm'}
                    label={t('Name')}
                    isRequired
                    placeholder={t('Name of media library')}
                    isInvalid={e.name == undefined || e.name.length == 0}
                    // errorMessage={t('Name is required')}
                    value={e.name}
                    onValueChange={v => {
                      e.name = v;
                      forceUpdate();
                    }}
                  />
                  <Input
                    variant="underlined"
                    size={'sm'}
                    label={t('Path')}
                    placeholder={t('Path of media library')}
                    isInvalid={e.path == undefined || e.path.length == 0}
                    // errorMessage={t('Path is required')}
                    isRequired
                    value={e.path}
                    onValueChange={v => {
                      e.path = v;
                      forceUpdate();
                    }}
                  />
                  <Select
                    size={'sm'}
                    label={t('Template')}
                    placeholder={t('Template for media library')}
                    dataSource={templates.map(t => ({
                      textValue: `#${t.id} ${t.name}`,
                      label: `#${t.id} ${t.name}`,
                      value: t.id,
                    }) as { label?: any; value: Key; textValue?: string; isDisabled?: boolean }).concat([{
                      label: (
                        <div className={'flex items-center gap-1'}>
                          <AiOutlineImport className={'text-lg'} />
                          {t('Create new template')}
                        </div>
                      ),
                      value: -1,
                      textValue: t('Create new template'),
                    }])}
                    variant="underlined"
                    isInvalid={e.templateId == undefined || e.templateId <= 0}
                    isRequired
                    selectedKeys={e.templateId ? [e.templateId.toString()] : undefined}
                    onSelectionChange={keys => {
                      const arr = Array.from(keys);
                      if (arr.length > 0) {
                        const idStr = arr[0] as string;
                        const value = parseInt(idStr, 10);
                        if (value == -1) {
                          createPortal(PresetTemplateBuilder, {
                            onSubmitted: async (id) => {
                              e.templateId = id;
                              await loadTemplates();
                            },
                          });
                        } else {
                          e.templateId = value;
                          forceUpdate();
                        }
                      }
                    }}
                  />
                  <div className={'flex items-center'}>
                    <ColorPicker
                      color={e.color}
                      onChange={v => {
                        if (typeof v === 'string') {
                          e.color = v;
                        } else if ('r' in v && 'g' in v && 'b' in v && 'a' in v) {
                          e.color = `rgba(${v.r},${v.g},${v.b},${v.a})`;
                        } else if ('h' in v && 's' in v && 'l' in v && 'a' in v) {
                          e.color = `hsla(${v.h},${v.s}%,${v.l}%,${v.a})`;
                        } else {
                          e.color = '';
                        }
                        forceUpdate();
                      }}
                    />
                  </div>
                  <div className={'flex items-center gap-1'}>
                    <Button
                      color={'danger'}
                      variant={'light'}
                      isIconOnly
                      onPress={() => {
                        editingMediaLibraries!.splice(i, 1);
                        forceUpdate();
                      }}
                    >
                      <MdOutlineDelete className={'text-lg'} />
                    </Button>
                  </div>
                </>
              );
            })}
          </div>
          <div className={'flex items-center gap-2 mt-2 justify-between'}>
            <div className={'flex items-center gap-2 mt-2'}>
              <Button
                size={'sm'}
                color={'default'}
                onPress={() => setEditingMediaLibraries(editingMediaLibraries!.concat([{}]))}
              >
                <AiOutlinePlusCircle className={'text-medium'} />
                {t('Add')}
              </Button>
            </div>
            <div className={'flex items-center gap-2 mt-2'}>
              <Button
                size={'sm'}
                color={'primary'}
                onPress={async () => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t('Save all media libraries'),
                    children: t('Deleted media libraries will not be restored. Are you sure you want to save?'),
                    onOk: async () => {
                      const data = editingMediaLibraries as MediaLibrary[];
                      const r = await BApi.mediaLibraryV2.saveAllMediaLibrariesV2(data);
                      if (!r.code) {
                        setEditingMediaLibraries(undefined);
                        toast.success(t('Saved'));
                        await loadMediaLibraries();
                      }
                    },
                  });
                }}
                isDisabled={!validate(editingMediaLibraries)}
              >
                <FaRegSave className={'text-medium'} />
                {t('Save')}
              </Button>
              <Button
                variant={'flat'}
                size={'sm'}
                color={'default'}
                onPress={() => setEditingMediaLibraries(undefined)}
              >
                <IoMdExit className={'text-medium'} />
                {t('Exit editing mode')}
              </Button>
            </div>
          </div>
        </>
      ) : (
        (mediaLibraries && mediaLibraries.length > 0) ? (
          <div>
            <div
              className={'inline-grid gap-1 mt-2 items-center'}
              style={{ gridTemplateColumns: 'repeat(6, auto)' }}
            >
              {mediaLibraries.map(ml => {
                const currentColor = ml.color ?? colors.color;
                console.log(ml, currentColor)
                return (
                  <>
                    <div style={{ color: currentColor }}>{ml.name}</div>
                    <ColorPicker
                      color={currentColor}
                      onChange={async color => {
                        const strColor = buildColorValueString(color);
                        await BApi.mediaLibraryV2.putMediaLibraryV2(ml.id, {
                          ...ml,
                          color: strColor
                        })
                        ml.color = strColor;
                        forceUpdate();
                      }}
                    />
                    <Chip
                      size={'sm'}
                      variant={'light'}
                      color={'success'}
                      startContent={<AiOutlineProduct className={'text-lg'} />}
                    >
                      {ml.resourceCount}
                    </Chip>
                    <Tooltip content={t('Search resources in current media library')} placement="top">
                      <Button
                        onPress={() => {
                          createPortal(Modal, {
                            title: t('Confirm'),
                            children: t('Are you sure you want to leave the current page?'),
                            defaultVisible: true,
                            onOk: async () => {
                              // 先调用GetFilterValueProperty接口获取valueProperty
                              const valuePropertyResponse = await BApi.resource.getFilterValueProperty({
                                propertyPool: PropertyPool.Internal,
                                propertyId: InternalProperty.MediaLibraryV2, // MediaLibrary 属性ID
                                operation: SearchOperation.Equals, // Equal
                              });

                              // 创建搜索表单，包含媒体库ID过滤条件
                              const searchForm = {
                                group: {
                                  combinator: 1, // And
                                  disabled: false,
                                  filters: [{
                                    propertyPool: PropertyPool.Internal,
                                    propertyId: InternalProperty.MediaLibraryV2, // MediaLibrary 属性ID
                                    operation: SearchOperation.Equals, // Equal
                                    dbValue: ml.id.toString(),
                                    bizValue: ml.name,
                                    valueProperty: valuePropertyResponse.data,
                                    disabled: false
                                  }]
                                },
                                page: 1,
                                pageSize: 100
                              };

                              // 跳转到Resource页面并带上搜索参数
                              const query = encodeURIComponent(JSON.stringify(searchForm));
                              history!.push(`/resource?query=${query}`);
                            },
                            footer: {
                              actions: ['ok', 'cancel'],
                              okProps: {
                                children: t('Continue')
                              },
                              cancelProps: {
                                children: t('Cancel')
                              }
                            }
                          });
                        }}
                        size={'sm'}
                        radius={'sm'}
                        className={''}
                        variant={'light'}
                        isIconOnly
                      >
                        <AiOutlineSearch className={'text-lg'} />
                      </Button>
                    </Tooltip>
                    {renderPath(ml)}
                    <Button
                      size={'sm'}
                      radius={'sm'}
                      className={'text-left'}
                      startContent={<TbTemplate className={'text-medium'} />}
                      variant={'light'}
                      onPress={() => {
                        if (ml.templateId) {
                          createPortal(
                            TemplateModal, {
                            id: ml.templateId,
                            onDestroyed: loadTemplates,
                          },
                          );
                        }
                      }}
                    >
                      {templates.find(t => t.id == ml.templateId)?.name ?? t('Unknown')}
                    </Button>
                    <SyncStatus
                      id={ml.id}
                      onSyncCompleted={() => {
                        BApi.mediaLibraryV2.getMediaLibraryV2(ml.id).then(r => {
                          const updatedMediaLibraries = mediaLibraries.map(m => (m.id === ml.id ? r.data : m));
                          setMediaLibraries(updatedMediaLibraries);
                        });
                      }}
                    />
                  </>
                );
              })}
            </div>
          </div>
        ) : (
          <div className={'flex items-center gap-2 grow justify-center'}>
            <PiEmpty className={'text-2xl'} />
            {t('No media libraries found. You must add at least one media library and synchronize it to manage your resources.')}
          </div>
        )
      )}
    </div>
  );
};
