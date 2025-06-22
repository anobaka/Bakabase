import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import _ from 'lodash';
import {
  AiOutlineAppstore,
  AiOutlineDelete, AiOutlineDoubleRight,
  AiOutlineLike,
  AiOutlinePlusCircle,
  AiOutlineQuestionCircle,
} from 'react-icons/ai';
import { QuestionCircleOutlined } from '@ant-design/icons';
import { Button, Chip, Divider, Input, Modal, Select, Spinner, Tooltip } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { components } from '@/sdk/BApi2';
import BApi from '@/sdk/BApi';
import type { EnhancerId, PresetProperty, PresetResourceType } from '@/sdk/constants';
import { PropertyPool } from '@/sdk/constants';
import BriefProperty from '@/components/Chips/Property/BriefProperty';
import { EnhancerIcon } from '@/components/Enhancer';

type DataPool = components['schemas']['Bakabase.Modules.Presets.Abstractions.Models.MediaLibraryTemplatePresetDataPool'];

type Props = {
  onSubmitted?: (id: number) => any;
} & DestroyableProps;

type Form = components['schemas']['Bakabase.Modules.Presets.Abstractions.Models.MediaLibraryTemplateCompactBuilder'];

const validate = (form: Partial<Form>): Form | undefined => {
  if (!form.name || form.name.length == 0) {
    return;
  }
  if (!form.resourceType || !form.resourceLayer) {
    return;
  }
  return form as Form;
};

export default ({
                  onDestroyed,
                  onSubmitted,
                }: Props) => {
  const { t } = useTranslation();
  const [dataPool, setDataPool] = useState<DataPool>();

  const [form, setForm] = useState<Partial<Form>>({});

  useEffect(() => {
    BApi.mediaLibraryTemplate.getMediaLibraryTemplatePresetDataPool().then(r => {
      setDataPool(r.data);
    });
  }, []);

  const propertyMap = _.keyBy(dataPool?.properties ?? [], x => x.id);
  const resourceTypeGroups = _.groupBy(dataPool?.resourceTypes || [], t => Math.floor(t.type / 1000));

  const recommendProperties = dataPool?.resourceTypePresetPropertyIds[form.resourceType ?? 0] || [];
  const recommendEnhancerIds = dataPool?.resourceTypeEnhancerIds[form.resourceType ?? 0] || [];

  const validForm = validate(form);

  console.log(form, validForm);

  return (
    <Modal
      title={(
        <div className={'flex items-center gap-1'}>
          <div>
            {t('Preset media library template builder')}
          </div>
        </div>
      )}
      defaultVisible
      onDestroyed={onDestroyed}
      size={'full'}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          isDisabled: !validForm,
        },
      }}
      onOk={async () => {
        const r = await BApi.mediaLibraryTemplate.addMediaLibraryTemplateFromPresetBuilder(validForm!);
        if (!r.code) {
          onSubmitted?.(r.data);
        }
      }}
    >
      {dataPool ? (<div className={'grid gap-x-4 gap-y-2 items-center'} style={{ gridTemplateColumns: 'auto 1fr' }}>
        <div className={'text-right'}>{t('Resource type')}</div>
        <div className={'flex flex-col gap-2'}>
          {_.keys(resourceTypeGroups).map(type => {
              const resourceTypes = resourceTypeGroups[type]!;
              return (
                <div className={'grid gap-2 items-center'} style={{ gridTemplateColumns: 'auto 1fr' }}>
                  <AiOutlineDoubleRight className={'text-base'} />
                  <div className={'flex items-center gap-1 flex-wrap'}>
                    {resourceTypes.map(tn => (<Button
                      size={'sm'}
                      onPress={() => {
                        if (form.resourceType != tn.type) {
                          setForm({
                            name: form.name,
                            resourceType: tn.type,
                            properties: dataPool.resourceTypePresetPropertyIds[tn.type] || [],
                            enhancerIds: dataPool.resourceTypeEnhancerIds[tn.type] || [],
                            resourceLayer: 1,
                          });
                        }
                      }}
                      color={form.resourceType == tn.type ? 'primary' : 'default'}
                    >
                      {tn.name}
                      {tn.description && (
                        <Tooltip
                          content={tn.description}
                        >
                          <AiOutlineQuestionCircle className={'text-base'} />
                        </Tooltip>
                      )}
                    </Button>))}
                  </div>
                </div>

              );
            })}
        </div>
        <div />
        <Divider />
        <div className={'text-right'}>{t('Properties')}</div>
        <div>
          <div className={'flex items-center gap-1 flex-wrap'}>
            {dataPool.properties.map(p => (<Button
              size={'sm'}
              onPress={() => {
                  const newProperties = form.properties ? [...form.properties] : [];
                  if (newProperties.includes(p.id)) {
                    _.remove(newProperties, t => t === p.id);
                  } else {
                    newProperties.push(p.id);
                  }
                  setForm({
                    ...form,
                    properties: newProperties,
                  });
                }}
              variant={recommendProperties.includes(p.id) ? 'solid' : 'flat'}
              color={form.properties?.includes(p.id) ? 'primary' : 'default'}
            >
              <BriefProperty
                hidePool
                property={{
                    pool: PropertyPool.Custom,
                    name: p.name,
                    type: p.type,
                  }}
              />
              {recommendProperties.includes(p.id) && (<AiOutlineLike className={'text-base'} />)}
            </Button>))}
          </div>
        </div>
        <div />
        <Divider />
        <div className={'flex items-center gap-1'}>
          {t('Resource layer')}
          <Tooltip
            content={t('You can configure how many levels deep the resource path should be under the media library path, and you can assign the intermediate directories as property values of the resource')}
          >
            <QuestionCircleOutlined className={'text-base'} />
          </Tooltip>
        </div>
        <div className={'flex flex-wrap items-center'}>
          <Chip variant={'light'} color={'success'}>{t('Media library')}</Chip>
          <Chip variant={'light'} color={'warning'}>/</Chip>
          {(form.resourceLayer && form.resourceLayer > 0) ? (
            <>
              {_.range(0, form.resourceLayer - 1).map((lp, idx) => {
                  return (
                    <div className={'flex items-center gap-1'}>
                      <Select
                        className={'min-w-[200px]'}
                        size={'sm'}
                        label={t('The {{layer}}th layer', { layer: idx + 1 })}
                        value={lp}
                        dataSource={form.properties?.map(p => ({
                          label: propertyMap?.[p]!.name,
                          value: p,
                        }))}
                        onSelectionChange={keys => {
                          form.layeredProperties![idx] = parseInt(Array.from(keys)[0] as string, 10) as PresetProperty;
                          setForm({
                            ...form,
                            layeredProperties: form.layeredProperties!.slice(),
                          });
                        }}
                      />
                      <Button
                        isIconOnly
                        variant={'light'}
                        size={'sm'}
                        color={'danger'}
                        onPress={() => {
                          const lps = (form.layeredProperties ?? []).slice();
                          lps.splice(idx, 1);
                          setForm({
                            ...form,
                            layeredProperties: lps,
                            resourceLayer: lps.length + 1,
                          });
                        }}
                      >
                        <AiOutlineDelete className={'text-base'} />
                      </Button>
                      <Chip variant={'light'} color={'warning'}>/</Chip>
                    </div>
                  );
                })}
            </>
            ) : null}
          <Button
            isDisabled={!form.properties || form.properties.length == 0}
            isIconOnly
            variant={'light'}
            size={'sm'}
            color={'primary'}
            onPress={() => {
                const lps = (form.layeredProperties ?? []);
                // @ts-ignore
                lps.push(undefined);
                setForm({
                  ...form,
                  layeredProperties: lps,
                  resourceLayer: lps.length + 1,
                });
              }}
          >
            <AiOutlinePlusCircle className={'text-base'} />
          </Button>
          <Chip variant={'light'} color={'success'}>{t('Resource')}</Chip>
        </div>
        <div />
        <Divider />
        <div className={'text-right'}>{t('Enhancers')}</div>
        <div>
          <div className={'flex items-center gap-1 flex-wrap'}>
            {dataPool.enhancers.map(e => (
              <Button
                size={'sm'}
                onPress={() => {
                    const newEnhancerIds = form.enhancerIds ? [...form.enhancerIds] : [];
                    if (newEnhancerIds.includes(e.id)) {
                      _.remove(newEnhancerIds, p => p === e.id);
                    } else {
                      newEnhancerIds.push(e.id);
                    }
                    setForm({
                      ...form,
                      enhancerIds: newEnhancerIds,
                    });
                  }}
                variant={recommendEnhancerIds.includes(e.id) ? 'solid' : 'flat'}
                color={form.enhancerIds?.includes(e.id) ? 'primary' : 'default'}
              >
                <EnhancerIcon id={e.id} />
                {e.name}
                {recommendEnhancerIds.includes(e.id) && (<AiOutlineLike className={'text-base'} />)}
              </Button>
              ))}
          </div>
        </div>
        <div className={'text-right'}>{t('Name')}</div>
        <div>
          <Input
            className={'w-[320px]'}
            fullWidth={false}
            isRequired
            placeholder={t('Set a name for this template')}
            value={form.name}
            onValueChange={name => setForm({
                ...form,
                name,
              })}
          />
        </div>
      </div>)
        : (
          <div className={'flex items-center justify-center gap-2 text-lg grow'}>
            <Spinner size={'md'} />
            {t('Initializing')}
          </div>
        )}

    </Modal>
  );
};
