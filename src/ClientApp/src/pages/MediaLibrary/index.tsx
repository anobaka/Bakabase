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
  return mls.every(ml => Array.isArray(ml.paths) && ml.paths.length > 0 && ml.paths.every(p => !!p) && new Set(ml.paths).size === ml.paths.length && isNotEmpty(ml.name) && ml.templateId != undefined && ml.templateId > 0);
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
        mediaLibraries.sort((a, b) => (a.paths[0] ?? '').localeCompare(b.paths[0] ?? ''));
        break;
      case SortBy.Template:
        mediaLibraries.sort((a, b) => (templatesRef.current[a.templateId ?? 0]?.name ?? '').localeCompare(templatesRef.current[b.templateId ?? 0]?.name ?? ''));
        break;
    }
    forceUpdate();
  }, [sortBy, mediaLibraries]);

  const renderPath = (ml: MediaLibrary) => {
    return (
      <div className={'flex flex-col gap-1'}>
        {(ml.paths ?? []).map((p, idx) => (
          <Button
            key={idx}
            className={'justify-start'}
            size={'sm'}
            color={'default'}
            variant={'flat'}
            onPress={() => BApi.tool.openFileOrDirectory({ path: p })}
          >
            <AiOutlineFolderOpen className={'text-base'} />
            {p}
          </Button>
        ))}
      </div>
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
                  : JSON.parse(JSON.stringify(mediaLibraries)) as Partial<MediaLibrary>[])}
              >
                <AiOutlineEdit className={'text-medium'} />
                {t('MediaLibrary.AddOrEdit')}
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
                {t('MediaLibrary.SynchronizeAll')}
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
              {t('MediaLibrary.SortBy', { sortBy: t(`MediaLibrary.SortBy.${SortBy[sortBy]}`) })}
            </Button>
          </div>
        )}
      </div>
      {editingMediaLibraries ? (
        <>
          <div className={'grid gap-1 mt-2 items-center'} style={{ gridTemplateColumns: 'auto 1fr 2fr auto 1fr auto' }}>
            {editingMediaLibraries.map((e, i) => {
              const paths = Array.isArray(e.paths) ? e.paths : (e.paths = []);
              const pathsValid = paths.length > 0 && paths.every(p => !!p) && new Set(paths).size === paths.length;
              return (
                <>
                  <div className={'flex justify-center items-center'}>
                    #{i + 1}
                  </div>
                  <Input
                    variant="underlined"
                    size={'sm'}
                    label={t('MediaLibrary.Name')}
                    isRequired
                    placeholder={t('MediaLibrary.NamePlaceholder')}
                    isInvalid={e.name == undefined || e.name.length == 0}
                    value={e.name}
                    onValueChange={v => {
                      e.name = v;
                      forceUpdate();
                    }}
                  />
                  <div className={'flex flex-col gap-1'}>
                    {paths.map((p, idx) => (
                      <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                        <Input
                          variant="underlined"
                          size="sm"
                          label={paths.length > 1 ? `${t('MediaLibrary.Path')}${idx + 1}` : t('MediaLibrary.Path')}
                          placeholder={t('MediaLibrary.PathPlaceholder')}
                          isInvalid={!p}
                          isRequired={idx === 0}
                          value={p}
                          onValueChange={v => {
                            paths[idx] = v;
                            // 保证无空字符串
                            e.paths = paths.filter(Boolean);
                            forceUpdate();
                          }}
                        />
                        {paths.length > 1 && (
                          <Button
                            size="sm"
                            color="danger"
                            isIconOnly
                            variant='light'
                            onPress={() => {
                              paths.splice(idx, 1);
                              e.paths = paths;
                              forceUpdate();
                            }}
                          >
                            <MdOutlineDelete className={'text-lg'} />
                          </Button>
                        )}
                      </div>
                    ))}
                  </div>
                  <Button
                    size="sm"
                    color="primary"
                    variant='light'
                    isIconOnly
                    onPress={() => {
                      paths.push('');
                      e.paths = paths;
                      forceUpdate();
                    }}
                  >
                    <AiOutlinePlusCircle className='text-lg' />
                  </Button>
                  <Select
                    size={'sm'}
                    label={t('MediaLibrary.Template')}
                    placeholder={t('MediaLibrary.TemplatePlaceholder')}
                    dataSource={templates.map(t => ({
                      textValue: `#${t.id} ${t.name}`,
                      label: `#${t.id} ${t.name}`,
                      value: t.id,
                    }) as { label?: any; value: Key; textValue?: string; isDisabled?: boolean }).concat([{
                      label: (
                        <div className={'flex items-center gap-1'}>
                          <AiOutlineImport className={'text-lg'} />
                          {t('MediaLibrary.CreateNewTemplate')}
                        </div>
                      ),
                      value: -1,
                      textValue: t('MediaLibrary.CreateNewTemplate'),
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
                  {/* <div className={'flex items-center'}>
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
                  </div> */}
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
                onPress={() => setEditingMediaLibraries(editingMediaLibraries!.concat([{} as Partial<MediaLibrary>]))}
              >
                <AiOutlinePlusCircle className={'text-medium'} />
                {t('MediaLibrary.Add')}
              </Button>
            </div>
            <div className={'flex items-center gap-2 mt-2'}>
              <Button
                size={'sm'}
                color={'primary'}
                onPress={async () => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t('MediaLibrary.SaveAll'),
                    children: t('MediaLibrary.SaveAllConfirm'),
                    onOk: async () => {
                      const data = editingMediaLibraries as MediaLibrary[];
                      const r = await BApi.mediaLibraryV2.saveAllMediaLibrariesV2(data);
                      if (!r.code) {
                        setEditingMediaLibraries(undefined);
                        toast.success(t('MediaLibrary.Saved'));
                        await loadMediaLibraries();
                      }
                    },
                  });
                }}
                isDisabled={!validate(editingMediaLibraries)}
              >
                <FaRegSave className={'text-medium'} />
                {t('MediaLibrary.Save')}
              </Button>
              <Button
                variant={'flat'}
                size={'sm'}
                color={'default'}
                onPress={() => setEditingMediaLibraries(undefined)}
              >
                <IoMdExit className={'text-medium'} />
                {t('MediaLibrary.ExitEditingMode')}
              </Button>
            </div>
          </div>
        </>
      ) : (
        (mediaLibraries && mediaLibraries.length > 0) ? (
          <div>
            <div
              className={'inline-grid gap-2 mt-2 items-center'}
              style={{ gridTemplateColumns: 'repeat(7, auto)' }}
            >
              {mediaLibraries.map(ml => {
                const template = templates.find(t => t.id == ml.templateId);
                const currentColor = ml.color ?? colors.color;
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
                    <Tooltip content={t('MediaLibrary.SearchResources')} placement="top">
                      <Button
                        onPress={() => {
                          createPortal(Modal, {
                            title: t('MediaLibrary.Confirm'),
                            children: t('MediaLibrary.LeavePageConfirm'),
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
                                children: t('MediaLibrary.Continue')
                              },
                              cancelProps: {
                                children: t('MediaLibrary.Cancel')
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
                      isDisabled={!template}
                    >
                      {template?.name ?? t('MediaLibrary.Unknown')}
                    </Button>
                    <SyncStatus
                      id={ml.id}
                      onSyncCompleted={() => {
                        BApi.mediaLibraryV2.getMediaLibraryV2(ml.id).then(r => {
                          if (!r.data) return;
                          const updatedMediaLibraries = mediaLibraries.map(m => (m.id === ml.id ? r.data! : m));
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
            {t('MediaLibrary.NoMediaLibrariesFound')}
          </div>
        )
      )}
    </div>
  );
};
