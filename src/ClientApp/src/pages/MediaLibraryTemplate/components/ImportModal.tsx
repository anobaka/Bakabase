import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import toast from 'react-hot-toast';
import { AiOutlineCheckCircle, AiOutlineCloseCircle } from 'react-icons/ai';
import { Button, Checkbox, Chip, Modal, Select, Textarea } from '@/components/bakaui';
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
  toPropertyPool?: PropertyPool;
  toPropertyId?: number;
  autoBinding?: boolean;
};

type ExtensionGroupConversion = {
  toExtensionGroupId?: number;
  autoBinding?: boolean;
};

type Validation = components['schemas']['Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.View.MediaLibraryTemplateValidationViewModel'];

const MissingDataMessage = 'The current template contains data missing from the application. Please configure how to handle this data before proceeding with the import.';

export default ({ onImported }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [shareCode, setShareCode] = useState<string>('');
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
              toast.error(t(MissingDataMessage));
              await initDataForImport();
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
          isReadOnly={!!validation}
          isRequired
          onValueChange={setShareCode}
          label={t('Share code')}
          placeholder={t('Paste share code here')}
        />
        {validation && (
          <Button
            size={'sm'}
            color={'default'}
            onPress={() => {
              setShareCode('');
              setValidation(undefined);
            }}
          >{t('Change share code')}</Button>
        )}
      </div>
      {validation && !validation.passed && (
        <div>
          <div>
            <Chip size={'sm'} variant={'flat'} color={'danger'}>{MissingDataMessage}</Chip>
          </div>
          {validation.unhandledProperties && validation.unhandledProperties.length > 0 && (
            <div>
              <div>{t('New properties')}</div>
              <div className={'grid gap-1'} style={{ gridTemplateColumns: '1fr 2fr auto' }}>
                {validation.unhandledProperties.map(p => {
                  const conversion = propertyConversionsMap?.[p.id];
                  const property = (conversion?.toPropertyPool && conversion?.toPropertyId)
                    ? propertyMap?.[conversion.toPropertyPool]?.[conversion.toPropertyId] : undefined;
                  const isSet = conversion?.autoBinding || property;
                  return (
                    <>
                      <div className={'flex items-center gap-1'}>
                        <PropertyPoolIcon pool={p.pool} />
                        <PropertyTypeIcon type={p.type} />
                        {p.name}
                      </div>
                      <div>
                        <Checkbox
                          checked={conversion?.autoBinding}
                          onValueChange={c => {
                            setPropertyConversionsMap({
                              ...propertyConversionsMap,
                              [p.id]: {
                                ...conversion,
                                autoBinding: c,
                              },
                            });
                          }}
                        >{t('Auto bind')}</Checkbox>
                        <Button
                          size={'sm'}
                          variant={'light'}
                          isDisabled={conversion?.autoBinding}
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
                          ) : t('Manually select')}
                        </Button>
                      </div>
                      <div>
                        <Chip color={isSet ? 'success' : 'danger'} size={'sm'} variant={'light'}>
                          {isSet ? (
                            <AiOutlineCheckCircle className={'text-medium'} />
                          ) : (
                            <AiOutlineCloseCircle className={'text-medium'} />
                          )}
                        </Chip>
                      </div>
                    </>
                  );
                })}
              </div>
            </div>
          )}
          {validation.unhandledExtensionGroups && validation.unhandledExtensionGroups.length > 0 && (
            <div>
              <div>{t('New properties')}</div>
              <div className={'grid gap-1'} style={{ gridTemplateColumns: '1fr 2fr auto' }}>
                {validation.unhandledExtensionGroups.map(eg => {
                  const conversion = extensionGroupConversionsMap?.[eg.id];
                  const leg = conversion?.toExtensionGroupId
                    ? extensionGroups?.[conversion.toExtensionGroupId] : undefined;
                  const isSet = conversion?.autoBinding || leg;
                  return (
                    <>
                      <div className={'flex flex-wrap gap-1'}>
                        {eg.name}
                        {eg.extensions?.map(e => {
                          return (
                            <Chip size={'sm'} radius={'sm'}>{e}</Chip>
                          );
                        })}
                      </div>
                      <div>
                        <Checkbox
                          checked={conversion?.autoBinding}
                          onValueChange={c => {
                            setPropertyConversionsMap({
                              ...propertyConversionsMap,
                              [eg.id]: {
                                ...conversion,
                                autoBinding: c,
                              },
                            });
                          }}
                        >{t('Auto bind')}</Checkbox>
                        <Select
                          size={'sm'}
                          variant={'underlined'}
                          isDisabled={conversion?.autoBinding}
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
                            label: x.name,
                            value: x.id.toString(),
                          }))}
                        />
                      </div>
                      <div>
                        <Chip color={isSet ? 'success' : 'danger'} size={'sm'} variant={'light'}>
                          {isSet ? (
                            <AiOutlineCheckCircle className={'text-medium'} />
                          ) : (
                            <AiOutlineCloseCircle className={'text-medium'} />
                          )}
                        </Chip>
                      </div>
                    </>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
};
