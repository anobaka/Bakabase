import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import _ from 'lodash';
import { AiOutlineCheckCircle } from 'react-icons/ai';
import { Accordion, AccordionItem, Card, CardBody, CardFooter, CardHeader, Chip, Divider, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { components } from '@/sdk/BApi2';
import BApi from '@/sdk/BApi';
import { MediaType } from '@/sdk/constants';

type BuiltinTemplate = components['schemas']['Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate.BuiltinMediaLibraryTemplateDescriptor'];

type Props = {
  onSelect: (template: BuiltinTemplate) => void;
} & DestroyableProps;

export default ({
                  onDestroyed,
                  onSelect,
                }: Props) => {
  const { t } = useTranslation();
  const [builtinTemplates, setBuiltinTemplates] = useState<BuiltinTemplate[]>([]);
  const [selectedTemplate, setSelectedTemplate] = useState<BuiltinTemplate>();

  useEffect(() => {
    BApi.mediaLibraryTemplate.getBuiltinMediaLibraryTemplates().then(r => {
      setBuiltinTemplates(r.data ?? []);
    });
  }, []);

  const templateGroups = _.groupBy(builtinTemplates, t => t.typeName);
  const typeNames = _.keys(templateGroups);

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
      size={'xl'}
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
      {(typeNames && typeNames.length > 0) ? (
        <Accordion variant="splitted" selectionMode={'multiple'} defaultSelectedKeys={typeNames}>
          {typeNames.map((typeName, gIdx) => {
            const templates = templateGroups[typeName]!;
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
                      >
                        <CardHeader>
                          <div className={'w-full flex items-center justify-between'}>
                            <div className="flex flex-col">
                              <p className="text-base">{bt.name}</p>
                              <p className="text-small text-left text-default-500">{t(`MediaType.${MediaType[bt.mediaType]}`)}</p>
                            </div>
                            <div>
                              {selectedTemplate == bt && (
                                <Chip
                                  size={'lg'}
                                  variant={'light'}
                                  radius={'sm'}
                                  color={'success'}
                                >
                                  <AiOutlineCheckCircle className={'text-2xl'} />
                                </Chip>
                              )}
                            </div>
                          </div>
                        </CardHeader>
                        <Divider />
                        <CardBody>
                          <div className={'flex flex-wrap gap-1 items-center'}>
                            {t('Properties')}
                            {bt.propertyNames.map(pn => (
                              <Chip size={'sm'} variant={'flat'} radius={'sm'}>
                                {pn}
                              </Chip>
                            ))}
                          </div>
                        </CardBody>
                        <Divider />
                        <CardFooter>
                          <div className={'flex flex-col'}>
                            {[...(bt.layeredPropertyNames ?? []), t('Resource')].map((pn, i) => {
                              return (
                                <div className={'text-left'}>{t('The {{layer}}th layer in media library -> {{propertyName}}', {
                                  layer: i + 1,
                                  propertyName: pn,
                                })}</div>
                              );
                            })}
                          </div>
                        </CardFooter>
                      </Card>
                    );
                  })}
                </div>
              </AccordionItem>
            );
          })}
        </Accordion>
      ) : (t('No builtin media library templates found'))}
    </Modal>
  );
};
