import { useTranslation } from 'react-i18next';
import { Card, CardBody, CardHeader, Chip } from '@/components/bakaui';
import Item from '@/pages/SynchronizationOptions/components/Item';
import { EnhancerIcon } from '@/components/Enhancer';

type Props = {
  header: string;
  selectors?: string[];
  enhancers?: {label: string; value: any}[];
};

const YesOrNo = ['Yes', 'No'];

export default ({ header, selectors, enhancers }: Props) => {
  const { t } = useTranslation();
  return (
    <Card>
      <CardHeader>
        <div className={'text-xl'}>
          {header}
        </div>
      </CardHeader>
      <CardBody>
        <div className={'grid gap-2 items-center'} style={{ gridTemplateColumns: 'repeat(2, auto)' }}>
          <Item
            options={YesOrNo}
            subject={t('Delete resources with unknown path')}
            selectors={selectors}
          />

          <Item
            options={YesOrNo}
            subject={t('Delete resources with unknown media library')}
            selectors={selectors}
          />

          {enhancers?.map(e => {
            const enhancerChip = (
              <Chip
                size={'sm'}
                radius={'sm'}
                variant={'flat'}
              >
                <div className={'flex items-center gap-1'}>
                  {t('Enhancer')}
                  <EnhancerIcon id={e.value} />
                  {t(e.label)}
                </div>
              </Chip>
            );

            const subjects = ['ReApply', 'ReEnhance'];

            return subjects.map(e => {
              return (
                <Item
                  options={YesOrNo}
                  subject={(
                    <>
                      {enhancerChip}
                      {t(e)}
                    </>
                  )}
                  selectors={selectors}
                />
              );
            });
          })}
        </div>
      </CardBody>
    </Card>
  );
};
