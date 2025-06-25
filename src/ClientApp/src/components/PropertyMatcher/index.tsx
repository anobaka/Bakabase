import { MdAutoFixHigh } from 'react-icons/md';
import { useTranslation } from 'react-i18next';
import { useCallback, useEffect, useState } from 'react';
import { AiOutlineDisconnect, AiOutlineSearch } from 'react-icons/ai';
import { IoLocate } from 'react-icons/io5';
import type { IProperty } from '../Property/models';
import { Button, Card, Modal, Tooltip } from '@/components/bakaui';
import type { PropertyType } from '@/sdk/constants';
import { PropertyPool } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import PropertySelector from '@/components/PropertySelector';
import BriefProperty from '@/components/Chips/Property/BriefProperty';

type Props = {
  matchedProperty?: IProperty;
  name: string;
  type: PropertyType;
  options?: any;
  isClearable?: boolean;
  onValueChanged?: (property?: IProperty) => any;
};

export default ({
                  matchedProperty,
                  type,
                  name,
                  options,
                  isClearable,
                  onValueChanged: propsOnValueChanged,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [property, setProperty] = useState<IProperty>();

  useEffect(() => {
    setProperty(matchedProperty);
  }, [matchedProperty]);

  const onValueChange = useCallback((property?: IProperty) => {
    setProperty(property);
    console.log(property);
    propsOnValueChanged?.(property);
  }, [propsOnValueChanged]);

  return (
    <div className={'inline-flex items-center gap-1'}>
      {property && (
        <>
          <Button
            size={'sm'}
            // color={'primary'}
            variant={'light'}
            onPress={() => {
              createPortal(PropertySelector, {
                pool: PropertyPool.Custom | PropertyPool.Reserved,
                multiple: false,
                onSubmit: async selection => {
                  onValueChange(selection[0]!);
                },
                v2: true,
              });
            }}
          >
            <BriefProperty property={property} />
          </Button>
          {isClearable && (
            <Button
              isIconOnly
              size={'sm'}
              color={'warning'}
              variant={'light'}
              onPress={() => {
                onValueChange();
              }}
            >
              <AiOutlineDisconnect className={'text-base'} />
            </Button>
          )}
        </>
      )}
      <Tooltip content={t('Automatically match property')}>
        <Button
          size={'sm'}
          isIconOnly
          color={'primary'}
          variant={'light'}
          onPress={async () => {
            const candidate = await BApi.property.findBestMatchingProperty({
              type,
              name,
            });
            if (candidate.data) {
              onValueChange(candidate.data);
            } else {
              const modal = createPortal(Modal, {
                defaultVisible: true,
                title: t('No proper property found'),
                children: (
                  <div className={'flex flex-col gap-2'}>
                    {t('You can')}:
                    <div className={'grid grid-cols-2 gap-2'}>
                      <Card
                        isPressable
                        onPress={async () => {
                          const r = await BApi.customProperty.addCustomProperty({
                            name: name,
                            type: type,
                            options: options ? JSON.stringify(options) : undefined,
                          });
                          if (r.code) {
                            throw new Error(r.message);
                          }
                          const np = r.data!;
                          onValueChange(np);
                          modal.destroy();
                        }}
                        className={'flex flex-col items-center gap-2 justify-center py-2'}
                      >
                        <MdAutoFixHigh className={'text-2xl'} />
                        {t('Automatically create a new property')}
                      </Card>
                      <Card
                        isPressable
                        onPress={() => {
                          modal.destroy();
                          createPortal(PropertySelector, {
                            pool: PropertyPool.Custom | PropertyPool.Reserved,
                            multiple: false,
                            onSubmit: async selection => {
                              onValueChange(selection[0]!);
                            },
                            v2: true,
                          });
                        }}
                        className={'flex flex-col items-center gap-2 justify-center py-2'}
                      >
                        <AiOutlineSearch className={'text-2xl'} />
                        {t('Select manually')}
                      </Card>
                    </div>
                  </div>
                ),
                footer: false,
              });
            }
          }}
        >
          <IoLocate className={'text-base'} />
        </Button>
      </Tooltip>
    </div>
  );
};
