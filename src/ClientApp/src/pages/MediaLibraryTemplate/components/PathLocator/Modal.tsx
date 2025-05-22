import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { useUpdate } from 'react-use';
import { Accordion, AccordionItem, CardBody } from '@heroui/react';
import { AiOutlineDelete } from 'react-icons/ai';
import { Button, Card, Input, Modal, Radio, RadioGroup, Select } from '@/components/bakaui';
import type { PathLocator } from '@/pages/MediaLibraryTemplate/models';
import { PathPositioner, pathPositioners } from '@/sdk/constants';
import type { DestroyableProps } from '@/components/bakaui/types';

type Props = {
  locators?: PathLocator[];
  onSubmit?: (locators: PathLocator[]) => any;
} & DestroyableProps;

export default ({
                  locators: propsLocators,
                  onSubmit,
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const [locators, setLocators] = useState<Partial<PathLocator>[]>(propsLocators ?? []);

  const renderPositioner = (locator: Partial<PathLocator>) => {
    switch (locator.positioner) {
      case PathPositioner.Layer:
        return (
          <Select
            isRequired
            label={t('Layer')}
            dataSource={[0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map(l => ({
              label: l,
              value: l.toString(),
            }))}
            selectedKeys={locator.layer == undefined ? undefined : [locator.layer.toString()]}
            onSelectionChange={keys => {
              const layer = parseInt(Array.from(keys)[0] as string, 10);
              locator.layer = layer;
              forceUpdate();
            }}
            description={t('Layer 0 is current path')}
          />
        );
      case PathPositioner.Regex:
        return (
          <Input
            isRequired
            label={t('Regex')}
            placeholder={t('Regex to match sub path')}
            value={locator.regex}
            onValueChange={v => {
              locator.regex = v;
              forceUpdate();
            }}
          />
        );
      default:
        return t('Not supported');
    }
  };

  const isValid = () => {
    return locators.every(locator => {
      switch (locator.positioner) {
        case PathPositioner.Layer:
          return locator.layer != undefined && locator.layer >= 0;
        case PathPositioner.Regex:
          return locator.regex != undefined && locator.regex.length > 0;
        default:
          return false;
      }
    });
  };

  return (
    <Modal
      size={'lg'}
      title={t('Extract property values from resource path')}
      defaultVisible
      onDestroyed={onDestroyed}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          isDisabled: !isValid(),
        },
      }}
      onOk={() => onSubmit?.(locators!)}
    >
      <div className={'flex flex-col gap-2 min-h-0 overflow-auto'}>
        <Accordion variant="splitted" selectedKeys={locators.map((l, i) => i.toString())}>
          {locators.map((locator, i) => {
            return (
              <AccordionItem
                key={i}
                title={(
                  <div>
                    {t('Rule')} {i + 1}
                    <Button
                      isIconOnly
                      size={'sm'}
                      color={'danger'}
                      variant={'light'}
                      onPress={() => {
                        locators.splice(i, 1);
                        forceUpdate();
                      }}
                    >
                      <AiOutlineDelete className={'text-medium'} />
                    </Button>
                  </div>
                )}
              >
                <RadioGroup
                  label={t('Positioning')}
                  onValueChange={v => {
                    const nv = parseInt(v, 10);
                    if (locator.positioner != nv) {
                      locators[i] = { positioner: nv };
                      forceUpdate();
                    }
                  }}
                  value={locator.positioner?.toString()}
                  orientation="horizontal"
                  isRequired
                >
                  {pathPositioners.map(p => (
                    <Radio value={p.value.toString()}>{p.label}</Radio>
                  ))}
                </RadioGroup>
                {locator.positioner && renderPositioner(locator)}
              </AccordionItem>
            );
          })}
        </Accordion>
      </div>
      <div className={'flex flex-col gap-0.5 '}>
        <div>
          <Button
            size={'sm'}
            onPress={() => {
              setLocators([
                ...locators,
                { positioner: PathPositioner.Layer },
              ]);
            }}
          >{t('Add a rule')}</Button>
        </div>
        <div>{t('For layer-based rules, level 0 represents the current directory; for regex rules, the text to be matched starts from the next level under the current directory up to the resource path portion.')}</div>
        <div>{t('All rules will be run independently, and the results will be merged')}</div>
      </div>
      <div>{JSON.stringify(locators)}</div>
    </Modal>
  );
};
