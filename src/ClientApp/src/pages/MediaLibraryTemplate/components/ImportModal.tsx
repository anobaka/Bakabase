import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import toast from 'react-hot-toast';
import { AiOutlineCheckCircle, AiOutlineCloseCircle } from 'react-icons/ai';
import { IoSync } from 'react-icons/io5';
import { TbSectionSign } from 'react-icons/tb';
import { TiChevronRightOutline } from 'react-icons/ti';
import { MdAutoFixHigh } from 'react-icons/md';
import _ from 'lodash';
import { Alert, Button, Chip, Modal, Select, Textarea, Tooltip } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { IProperty } from '@/components/Property/models';
import type { ExtensionGroup } from '@/pages/ExtensionGroup';
import BApi from '@/sdk/BApi';
import PropertyPoolIcon from '@/components/Property/components/PropertyPoolIcon';
import PropertyTypeIcon from '@/components/Property/components/PropertyTypeIcon';
import { PropertyPool } from '@/sdk/constants';
import type { PropertyMap } from '@/components/types';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import PropertySelector from '@/components/PropertySelector';
import type { components } from '@/sdk/BApi2';

type Props = {
  onImported?: () => any;
} & DestroyableProps;

type PropertyConversion = {
  toPropertyPool: PropertyPool;
  toPropertyId: number;
};

type ExtensionGroupConversion = {
  toExtensionGroupId: number;
};

type Validation = components['schemas']['Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.View.MediaLibraryTemplateValidationViewModel'];

const MissingDataMessage = 'The current template contains data missing from the application. Please configure how to handle this data before proceeding with the import.';

const findProperProperty = (property: IProperty, propertyMap: PropertyMap): IProperty | undefined => {
  const candidates = _.values(propertyMap[PropertyPool.Reserved]).concat(_.values(propertyMap[PropertyPool.Custom]))
    .filter(p => p.type == property.type);
  const best = candidates.find(p => p.name == property.name);
  return best ?? candidates[0];
};

const findProperExtensionGroup = (extensionGroup: ExtensionGroup, extensionGroups: ExtensionGroup[]): ExtensionGroup | undefined => {
  const candidates = extensionGroups.filter(eg => _.xor(extensionGroup.extensions, eg.extensions).length === 0);
  const best = candidates.find(eg => eg.name === extensionGroup.name);
  return best ?? candidates[0];
};

export default ({ onImported }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [shareCode, setShareCode] = useState<string>('{"name":"视频类（状态/资源）","author":"anobaka@qq.com","description":"asdasdsdasdas","resourceFilters":[{"fsType":2,"extensionGroups":[],"extensions":[],"positioner":1,"layer":2}],"properties":[{"property":{"pool":2,"id":12,"name":"介绍","type":2,"order":2147483647},"valueLocators":[]},{"property":{"pool":2,"id":13,"name":"评分","type":7,"order":2147483647},"valueLocators":[]},{"property":{"pool":2,"id":22,"name":"封面","type":10,"order":2147483647},"valueLocators":[]},{"property":{"pool":4,"id":12,"name":"标题","type":1,"order":0},"valueLocators":[]},{"property":{"pool":4,"id":31,"name":"状态","type":3,"options":{"choices":[],"allowAddingNewDataDynamically":true},"order":0},"valueLocators":[{"positioner":1,"layer":1}]},{"property":{"pool":4,"id":32,"name":"分类","type":3,"options":{"choices":[],"allowAddingNewDataDynamically":false},"order":0},"valueLocators":[{"positioner":1,"layer":0}]}],"playableFileLocator":{"extensionGroups":[{"id":1,"name":"视频文件","extensions":[".mp4",".mkv",".avi",".rmvb",".ts"]},{"id":2,"name":"audio","extensions":[]}],"extensions":[".mkv",".mp10"]},"enhancers":[{"enhancerId":1}],"displayNameTemplate":"{文件名}"}');
  const [validation, setValidation] = useState<Validation>();

  const [propertyConversionsMap, setPropertyConversionsMap] = useState<Record<number, PropertyConversion>>();
  const [extensionGroupConversionsMap, setExtensionGroupConversionsMap] = useState<Record<number, ExtensionGroupConversion>>();

  const [propertyMap, setPropertyMap] = useState<PropertyMap | undefined>(undefined);
  const [extensionGroups, setExtensionGroups] = useState<ExtensionGroup[] | undefined>(undefined);

  const initDataForImport = async () => {
    if (!propertyMap) {
      // @ts-ignore
      const pr = await BApi.property.getPropertiesByPool(PropertyPool.Custom | PropertyPool.Reserved);
      const pm = (pr.data ?? []).reduce<PropertyMap>((s, t) => {
        const m = (s[t.pool] ??= {});
        m[t.id] = t;
        return s;
      }, {});
      setPropertyMap(pm);
    }
    if (!extensionGroups) {
      const eg = await BApi.extensionGroup.getAllExtensionGroups();
      setExtensionGroups(eg.data ?? []);
    }
  };

  const $import = async () => {
    const model = {
      shareCode: shareCode,
      customPropertyConversionsMap: propertyConversionsMap,
      extensionGroupConversionsMap,
    };
    return await BApi.mediaLibraryTemplate.importMediaLibraryTemplate(model);
  };

  return (
    <Modal
      defaultVisible
      title={t('Import media library template')}
      size={'lg'}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          children: t('Import'),
        },
      }}
      onOk={async () => {
        if (!validation) {
          const validationResult = await BApi.mediaLibraryTemplate.validateMediaLibraryTemplateShareCode(shareCode);
          if (validationResult.code) {
            const msg = `${t('Failed to validate media library template')}:${validationResult.message}`;
            toast.error(msg);
            throw new Error(msg);
          } else {
            setValidation(validationResult.data!);
            if (!validationResult.data!.passed) {
              await initDataForImport();
              throw new Error(t(MissingDataMessage));
            }
          }
        }
        const importResult = await $import();
        if (importResult.code) {
          const msg = `${t('Failed to import media library template')}:${importResult.message}`;
          toast.error(msg);
          throw new Error(msg);
        } else {
          onImported?.();
        }
      }}
    >
      <div>
        <Textarea
          value={shareCode}
          isDisabled={!!validation}
          isRequired
          onValueChange={setShareCode}
          label={t('Share code')}
          placeholder={t('Paste share code here')}
        />
        {validation && (
          <Button
            className={'mt-2'}
            size={'sm'}
            color={'default'}
            onPress={() => {
              setShareCode('');
              setValidation(undefined);
            }}
          >
            <IoSync />
            {t('Change share code')}
          </Button>
        )}
      </div>
      {validation && !validation.passed && (
        <div className={'flex flex-col gap-2'}>
          <div>
            <Alert color={'warning'} title={MissingDataMessage} />
          </div>
          {validation.unhandledProperties && validation.unhandledProperties.length > 0 && (
            <div className={'flex flex-col gap-2'}>
              <div className={'flex items-center gap-1 text-lg font-bold'}>
                <TbSectionSign className={''} />
                {t('New properties')}
              </div>
              <div>
                <div
                  className={'inline-grid gap-1 items-center'}
                  style={{ gridTemplateColumns: 'auto auto auto auto auto' }}
                >
                  {validation.unhandledProperties.map(p => {
                    const conversion = propertyConversionsMap?.[p.id];
                    const property = (conversion?.toPropertyPool && conversion?.toPropertyId)
                      ? propertyMap?.[conversion.toPropertyPool]?.[conversion.toPropertyId] : undefined;
                    const isSet = !!property;
                    return (
                      <>
                        <div>
                          <Chip color={isSet ? 'success' : 'danger'} size={'sm'} variant={'light'}>
                            {isSet ? (
                              <AiOutlineCheckCircle className={'text-medium'} />
                            ) : (
                              <AiOutlineCloseCircle className={'text-medium'} />
                            )}
                          </Chip>
                        </div>
                        <div className={'flex items-center gap-1'}>
                          <PropertyPoolIcon pool={p.pool} />
                          <PropertyTypeIcon type={p.type} />
                          {p.name}
                        </div>
                        <TiChevronRightOutline className={'text-medium'} />
                        <Tooltip content={t('Automatically process')}>
                          <Button
                            size="sm"
                            color={'primary'}
                            isIconOnly
                            variant={'light'}
                            onPress={async () => {
                              const candidate = findProperProperty(p, propertyMap ?? []);
                              if (candidate) {
                                setPropertyConversionsMap({
                                  ...propertyConversionsMap,
                                  [p.id]: {
                                    toPropertyPool: candidate.pool,
                                    toPropertyId: candidate.id,
                                  },
                                });
                              } else {
                                createPortal(Modal, {
                                  defaultVisible: true,
                                  title: t('No proper property found'),
                                  children: t('Should we create a new property for this?'),
                                  onOk: async () => {
                                    const r = await BApi.customProperty.addCustomProperty({
                                      name: p.name,
                                      type: p.type,
                                      options: p.options ? JSON.stringify(p.options) : undefined,
                                    });
                                    if (r.code) {
                                      throw new Error(r.message);
                                    }
                                    const np = r.data!;
                                    setPropertyMap({
                                      ...propertyMap,
                                      [np.pool]: {
                                        ...propertyMap![np.pool],
                                        [np.id]: np,
                                      },
                                    });
                                    setPropertyConversionsMap({
                                      ...propertyConversionsMap,
                                      [p.id]: {
                                        toPropertyPool: np.pool,
                                        toPropertyId: np.id,
                                      },
                                    });
                                  },
                                });
                              }
                            }}
                          >
                            <MdAutoFixHigh className={'text-medium'} />
                          </Button>
                        </Tooltip>
                        <div>
                          <Button
                            size={'sm'}
                            color={'primary'}
                            variant={'light'}
                            onPress={() => {
                              createPortal(PropertySelector, {
                                pool: PropertyPool.Custom | PropertyPool.Reserved,
                                multiple: false,
                                onSubmit: async selection => {
                                  setPropertyConversionsMap({
                                    ...propertyConversionsMap,
                                    [p.id]: {
                                      ...conversion,
                                      toPropertyPool: selection[0]!.pool,
                                      toPropertyId: selection[0]!.id,
                                    },
                                  });
                                },
                                v2: true,
                              });
                            }}
                          >
                            {property ? (
                              <div className={'flex items-center gap-1'}>
                                <PropertyPoolIcon pool={property.pool} />
                                <PropertyTypeIcon type={property.type} />
                                {property.name}
                              </div>
                            ) : t('Select a property manually')}
                          </Button>
                        </div>
                      </>
                    );
                  })}
                </div>
              </div>
            </div>
          )}
          {validation.unhandledExtensionGroups && validation.unhandledExtensionGroups.length > 0 && (
            <div className={'flex flex-col gap-2'}>
              <div className={'flex items-center gap-1 text-lg font-bold'}>
                <TbSectionSign className={''} />
                {t('New extension groups')}
              </div>
              <div>
                <div
                  className={'inline-grid gap-1 items-center'}
                  style={{ gridTemplateColumns: 'auto auto auto auto auto' }}
                >
                  {validation.unhandledExtensionGroups.map(eg => {
                    const conversion = extensionGroupConversionsMap?.[eg.id];
                    const leg = conversion?.toExtensionGroupId
                      ? extensionGroups?.find(g => g.id == conversion.toExtensionGroupId) : undefined;
                    const isSet = !!leg;
                    // console.log(conversion, leg, isSet);
                    return (
                      <>
                        <div>
                          <Chip color={isSet ? 'success' : 'danger'} size={'sm'} variant={'light'}>
                            {isSet ? (
                              <AiOutlineCheckCircle className={'text-medium'} />
                            ) : (
                              <AiOutlineCloseCircle className={'text-medium'} />
                            )}
                          </Chip>
                        </div>
                        <div className={'flex items-center flex-wrap gap-1'}>
                          {eg.name}
                          {eg.extensions?.map(e => {
                            return (
                              <Chip size={'sm'} variant={'flat'} radius={'sm'}>{e}</Chip>
                            );
                          })}
                        </div>
                        <TiChevronRightOutline className={'text-medium'} />
                        <Tooltip content={t('Automatically process')}>
                          <Button
                            color={'primary'}
                            isIconOnly
                            variant={'light'}
                            onPress={async () => {
                              const candidate = findProperExtensionGroup(eg, extensionGroups ?? []);
                              if (candidate) {
                                setExtensionGroupConversionsMap({
                                  ...extensionGroupConversionsMap,
                                  [eg.id]: {
                                    toExtensionGroupId: candidate.id,
                                  },
                                });
                              } else {
                                createPortal(Modal, {
                                  defaultVisible: true,
                                  title: t('No proper extension group found'),
                                  children: t('Should we create a new extension group for this?'),
                                  onOk: async () => {
                                    const r = await BApi.extensionGroup.addExtensionGroup({
                                      name: eg.name,
                                      extensions: eg.extensions,
                                    });
                                    if (r.code) {
                                      throw new Error(r.message);
                                    } else {
                                      const newEg = r.data!;
                                      setExtensionGroups([...extensionGroups!, newEg]);
                                      setExtensionGroupConversionsMap({
                                        ...extensionGroupConversionsMap,
                                        [eg.id]: {
                                          toExtensionGroupId: newEg.id,
                                        },
                                      });
                                    }
                                  },
                                });
                              }
                            }}
                          >
                            <MdAutoFixHigh className={'text-medium'} />
                          </Button>
                        </Tooltip>
                        <div className={'min-w-[240px]'}>
                          <Select
                            fullWidth
                            size={'sm'}
                            isMultiline
                            variant={'bordered'}
                            placeholder={t('Select an extension group manually')}
                            selectedKeys={conversion?.toExtensionGroupId
                              ? [conversion.toExtensionGroupId.toString()] : undefined}
                            onSelectionChange={selection => {
                              const id = parseInt(Array.from(selection)[0] as string, 10);
                              setExtensionGroupConversionsMap({
                                ...extensionGroupConversionsMap,
                                [eg.id]: {
                                  ...conversion,
                                  toExtensionGroupId: id,
                                },
                              });
                            }}
                            multiple={false}
                            dataSource={extensionGroups?.map(x => ({
                              label: (
                                <div className={'flex flex-col gap-1'}>
                                  <div>{x.name}</div>
                                  {x.extensions && x.extensions.length > 0 && (
                                    <div className={'flex items-center gap-1 flex-wrap'}>{x.extensions.map(e => {
                                      return (
                                        <Chip
                                          size={'sm'}
                                          radius={'sm'}
                                          variant={'flat'}
                                        >{e}</Chip>
                                      );
                                    })}</div>
                                  )}
                                </div>
                              ),
                              textValue: x.name,
                              value: x.id.toString(),
                            }))}
                          />
                        </div>
                      </>
                    );
                  })}
                </div>
              </div>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
};
