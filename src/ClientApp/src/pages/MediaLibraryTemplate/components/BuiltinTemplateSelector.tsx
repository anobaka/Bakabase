import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Input, Modal, Select, Card, CardBody, CardFooter, CardHeader, Divider, Chip } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import type { components } from '@/sdk/BApi2';
import BApi from '@/sdk/BApi';
import { MediaType } from '@/sdk/constants';

type Props = {
  onSelect: (id: string) => void;
} & DestroyableProps;

type BuiltinTemplate = components['schemas']['Bakabase.InsideWorld.Business.Components.BuiltinMediaLibraryTemplate.BuiltinMediaLibraryTemplateDescriptor'];

export default ({
                  onDestroyed,
                  onSelect,
                }: Props) => {
  const { t } = useTranslation();
  const [builtinTemplates, setBuiltinTemplates] = useState<BuiltinTemplate[]>([]);
  const [id, setId] = useState<string>();

  useEffect(() => {
    BApi.mediaLibraryTemplate.getBuiltinMediaLibraryTemplates().then(r => {
      setBuiltinTemplates(r.data ?? []);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      onDestroyed={onDestroyed}
      size={'xl'}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          isDisable: !id,
        },
      }}
      onOk={() => {
        onSelect(id!);
      }}
    >
      <div className={'flex flex-wrap gap-2'}>
        {builtinTemplates.map(bt => {
          return (
            <Card onPress={() => {
              setId(bt.id);
            }}
            >
              <CardHeader>
                <div className="flex flex-col">
                  <p className="text-md">{bt.name}</p>
                  <p className="text-small text-default-500">{t(`MediaType.${MediaType[bt.mediaType]}`)}</p>
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
                  {bt.layeredPropertyNames?.map((pn, i) => {
                    return (
                      <div>{t('Set the {{layer}}th layer after the media library path to {{propertyName}}', {
                        layer: i + 1,
                        propertyName: pn,
                      })}</div>
                    );
                  })}
                  <div>{t('Set the {{layer}}th layer after the media library path to {{propertyName}}', {
                    layer: (bt.layeredPropertyNames?.length ?? 0) + 1,
                    propertyName: t('Resource'),
                  })}</div>
                </div>
              </CardFooter>
            </Card>
        );
})}
      </div>
    </Modal>
  );
};
