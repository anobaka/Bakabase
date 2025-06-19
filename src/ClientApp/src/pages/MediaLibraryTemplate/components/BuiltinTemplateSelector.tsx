import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import _ from 'lodash';
import { AiOutlineCheckCircle } from 'react-icons/ai';
import {
  Accordion,
  AccordionItem,
  Button,
  ButtonGroup,
  Card,
  CardBody,
  CardFooter,
  CardHeader,
  Chip,
  Divider,
  Modal,
} from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { components } from '@/sdk/BApi2';
import BApi from '@/sdk/BApi';
import { EnhancerId, MediaType } from '@/sdk/constants';
import { PropertyLabel } from '@/components/Property/v2';
import BriefProperty from '@/components/Chips/Property/BriefProperty';

type BuiltinTemplate = components['schemas']['Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate.BuiltinMediaLibraryTemplateDescriptor'];

type Props = {
  onSelect: (template: BuiltinTemplate) => void;
} & DestroyableProps;

type FilterForm = {
  typeNames?: string[];
  mediaTypes?: MediaType[];
  propertyNames?: string[];
};

export default ({
                  onDestroyed,
                  onSelect,
                }: Props) => {
  const { t } = useTranslation();
  const [builtinTemplates, setBuiltinTemplates] = useState<BuiltinTemplate[]>([]);
  const [selectedTemplate, setSelectedTemplate] = useState<BuiltinTemplate>();

  const [filterForm, setFilterForm] = useState<FilterForm>({});

  useEffect(() => {
    BApi.mediaLibraryTemplate.getBuiltinMediaLibraryTemplates().then(r => {
      setBuiltinTemplates(r.data ?? []);
    });
  }, []);

  const templateGroups = _.groupBy(builtinTemplates, t => t.typeName);
  const typeNames = _.keys(templateGroups);
  const mediaTypes = _.uniq(builtinTemplates.map(tpl => tpl.mediaType));
  const propertyNames = _.uniq(_.flatten(builtinTemplates.map(tpl => tpl.propertyNames)));

  const filteredTemplates = builtinTemplates.filter(tpl => {
    const matchesType = !filterForm.typeNames || filterForm.typeNames.length == 0 ||
      filterForm.typeNames.includes(tpl.typeName);
    const matchesMediaType = !filterForm.mediaTypes || filterForm.mediaTypes.length == 0 ||
      filterForm.mediaTypes.includes(tpl.mediaType);
    const matchesProperties = !filterForm.propertyNames || filterForm.propertyNames.length == 0 ||
      _.intersection(tpl.propertyNames, filterForm.propertyNames).length > 0;

    return matchesType && matchesMediaType && matchesProperties;
  });
  const filteredTemplateGroups = _.groupBy(filteredTemplates, t => t.typeName);
  const filteredTypeNames = _.keys(filteredTemplateGroups);

  return (
    <Modal
      title={(
        <div className={'flex items-center gap-1'}>
          <div>
            {t('Builtin media library templates')}
          </div>
          {selectedTemplate && (
            <div>{t('Selected')}&nbsp;{selectedTemplate.name}</div>
          )}
        </div>
      )}
      defaultVisible
      onDestroyed={onDestroyed}
      size={'full'}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          isDisable: !selectedTemplate,
        },
      }}
      onOk={() => {
        onSelect(selectedTemplate!);
      }}
    >
      <div className={'grid gap-2 items-center'} style={{ gridTemplateColumns: 'auto 1fr' }}>
        <div>{t('Resource type')}</div>
        <div>
          <ButtonGroup size={'sm'}>
            {typeNames.map(tn => (<Button
              onPress={() => {
                const newTypeNames = filterForm.typeNames ? [...filterForm.typeNames] : [];
                if (newTypeNames.includes(tn)) {
                  _.remove(newTypeNames, t => t === tn);
                } else {
                  newTypeNames.push(tn);
                }
                setFilterForm({
                  ...filterForm,
                  typeNames: newTypeNames,
                });
              }}
              color={filterForm.typeNames?.includes(tn) ? 'primary' : 'default'}
            >{tn}</Button>))}
          </ButtonGroup>
        </div>
        <div>{t('Media type')}</div>
        <div>
          <ButtonGroup size={'sm'}>
            {mediaTypes.map(tn => (<Button
              onPress={() => {
                const newMediaTypes = filterForm.mediaTypes ? [...filterForm.mediaTypes] : [];
                if (newMediaTypes.includes(tn)) {
                  _.remove(newMediaTypes, t => t === tn);
                } else {
                  newMediaTypes.push(tn);
                }
                setFilterForm({
                  ...filterForm,
                  mediaTypes: newMediaTypes,
                });
              }}
              color={filterForm.mediaTypes?.includes(tn) ? 'primary' : 'default'}
            >{t(`MediaType.${MediaType[tn]}`)}</Button>))}
          </ButtonGroup>
        </div>
        <div>{t('Properties')}</div>
        <div>
          <ButtonGroup size={'sm'}>
            {propertyNames.map(pn => (
              <Button
                onPress={() => {
                  const newPropertyNames = filterForm.propertyNames ? [...filterForm.propertyNames] : [];
                  if (newPropertyNames.includes(pn)) {
                    _.remove(newPropertyNames, p => p === pn);
                  } else {
                    newPropertyNames.push(pn);
                  }
                  setFilterForm({
                    ...filterForm,
                    propertyNames: newPropertyNames,
                  });
                }}
                color={filterForm.propertyNames?.includes(pn) ? 'primary' : 'default'}
              >{pn}</Button>
            ))}
          </ButtonGroup>
        </div>
      </div>
      {(filteredTypeNames && filteredTypeNames.length > 0) ? (
        <Accordion variant="splitted" selectionMode={'multiple'} defaultSelectedKeys={filteredTypeNames}>
          {filteredTypeNames.map((typeName, gIdx) => {
            const templates = filteredTemplateGroups[typeName]!;
            return (
              <AccordionItem key={typeName} title={typeName}>
                <div className={'grid grid-cols-4 gap-1'}>
                  {templates.map(bt => {
                    return (
                      <Card
                        isPressable
                        onPress={() => {
                          setSelectedTemplate(bt);
                        }}
                        className={'relative '}
                      >
                        <CardHeader>
                          <div className={'w-full flex items-center'}>
                            <div className="flex flex-col">
                              <p className="text-base text-left">{bt.name}</p>
                              <p className="text-small text-default-500 flex items-center gap-1">
                                {t(`MediaType.${MediaType[bt.mediaType]}`)}
                                {bt.propertyNames.map(pn => (
                                  <Chip size={'sm'} variant={'flat'} radius={'sm'}>
                                    {pn}
                                  </Chip>
                                ))}
                              </p>
                            </div>
                            {selectedTemplate == bt && (
                              <Chip
                                className={'absolute top-[10px] right-[10px]'}
                                size={'lg'}
                                variant={'light'}
                                radius={'sm'}
                                color={'success'}
                              >
                                <AiOutlineCheckCircle className={'text-2xl'} />
                              </Chip>
                            )}
                          </div>
                        </CardHeader>
                        <Divider />
                        <CardBody>
                          <div className={'flex flex-col'}>
                            {[...(bt.layeredPropertyNames ?? []), t('Resource')].map((pn, i) => {
                              return (
                                <div className={'text-left'}>{t('The {{layer}}th layer of path -> {{propertyName}}', {
                                  layer: i + 1,
                                  propertyName: pn,
                                })}</div>
                              );
                            })}
                          </div>
                        </CardBody>
                        <Divider />
                        <CardFooter>
                          {_.keys(bt.enhancerTargets).length > 0 ? (
                            <div className={'flex flex-col gap-1'}>
                              {_.keys(bt.enhancerTargets).map(eIdStr => {
                                const targets = bt.enhancerTargets![eIdStr] ?? [];
                                return (
                                  <div className={'flex items-center gap-1 flex-wrap'}>
                                    <div>{t(`${EnhancerId[parseInt(eIdStr, 10)]}${t('Enhancer')}`)}</div>
                                    {targets.length > 0 ? (
                                      <>
                                        <div>{t('enhance')}</div>
                                        {targets.map(target => {
                                          const { property } = target;
                                          return (
                                            <BriefProperty
                                              hideType
                                              property={{
                                                pool: property.pool,
                                                type: property.type,
                                                name: property.name,
                                              }}
                                            />
                                          );
                                        })}
                                      </>
                                    ) : t('No properties enhanced')}
                                  </div>
                                );
                              })}
                            </div>
                          ) : t('No enhancer configured')}
                        </CardFooter>
                      </Card>
                    );
                  })}
                </div>
              </AccordionItem>
            );
          })}
        </Accordion>
      ) : (t('No built-in media library templates found'))}
    </Modal>
  );
};
