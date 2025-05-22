import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AiOutlineCheckCircle, AiOutlineQuestionCircle } from 'react-icons/ai';
import { Card, CardBody, CardHeader, Chip, Modal, Tooltip } from '@/components/bakaui';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import BApi from '@/sdk/BApi';
import type { DestroyableProps } from '@/components/bakaui/types';

type Props = {
  selectedIds?: number[];
  onSubmit?: (ids: number[]) => any;
} & DestroyableProps;

export default ({
                  selectedIds: propSelectedIds,
                  onSubmit,
                }: Props) => {
  const { t } = useTranslation();

  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);
  const [selectedIds, setSelectedIds] = useState<number[]>(propSelectedIds ?? []);
  useEffect(() => {
    // createPortal(
    //   PathFilterModal, {
    //
    //   },
    // );
    BApi.enhancer.getAllEnhancerDescriptors().then(r => {
      setEnhancers(r.data || []);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onOk={() => onSubmit?.(selectedIds)}
    >
      <div className={'flex flex-col gap-2'}>
        {enhancers.map(e => {
          const isSelected = selectedIds.includes(e.id);
          return (
            <Card
              isPressable
              onPress={() => {
                if (isSelected) {
                  setSelectedIds(selectedIds.filter(id => id !== e.id));
                } else {
                  setSelectedIds([...selectedIds, e.id]);
                }
              }}
            >
              <CardBody>
                <div className={'text-medium flex items-center gap-1'}>
                  {e.name}
                  {isSelected && (
                    <Chip
                      variant={'light'}
                      size={'sm'}
                      radius={'sm'}
                      color={'success'}
                    >
                      <AiOutlineCheckCircle className={'text-lg'} />
                    </Chip>
                  )}
                </div>
                <div className={'opacity-60'}>
                  {e.description}
                </div>
                <div className={'flex flex-wrap gap-1'}>
                  <div className={'font-bold'}>
                    {t('Enhance properties')}:&nbsp;
                  </div>
                  {e.targets.map(target => {
                    if (target.description) {
                      return (
                        <Tooltip
                          content={target.description}
                        >
                          <Chip size={'sm'} radius={'sm'} color={'default'}>
                            <div className={'flex items-center'}>
                              {target.name}
                              <AiOutlineQuestionCircle className={'text-medium'} />
                            </div>
                          </Chip>
                        </Tooltip>
                      );
                    }
                    return (
                      <Chip size={'sm'} radius={'sm'} color={'default'}>
                        {target.name}
                      </Chip>
                    );
                  })}
                </div>
              </CardBody>
            </Card>
          );
        })}
      </div>
    </Modal>
  );
};
