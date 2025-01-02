import { useTranslation } from 'react-i18next';
import { WarningOutlined } from '@ant-design/icons';
import { PropertyType } from '@/sdk/constants';
import { StandardValueType } from '@/sdk/constants';
import { Button, Chip, Tooltip } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import TypeConversionRuleOverviewDialog from '@/pages/CustomProperty/components/TypeConversionRuleOverviewDialog';

type Props = {
  toType: PropertyType;
  fromType: PropertyType;
};

export default (props: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    toType,
    fromType,
  } = props;

  if (toType == fromType) {
    return null;
  }

  return (
    <Tooltip
      content={(
        <div className={'flex flex-col gap-1'}>
          <div>
            {t('We will automatically convert the value from {{fromType}} to {{toType}}.', {
              fromType: t(`PropertyType.${PropertyType[fromType]}`),
              toType: t(`PropertyType.${PropertyType[toType]}`),
            })}
          </div>
          <div>
            <Button
              size={'sm'}
              variant={'light'}
              color={'primary'}
              onPress={() => {
                createPortal(TypeConversionRuleOverviewDialog, {});
              }}
            >
              {t('You can check type conversion rules here')}
            </Button>
          </div>
        </div>
      )}
    >
      <Chip
        size={'sm'}
        variant={'light'}
        radius={'sm'}
        className={'ml-2'}
        classNames={{
          content: 'flex items-center gap-1',
        }}
        color={'warning'}
      >
        <WarningOutlined className={'text-base'} />
        {t('Type transformation')}
      </Chip>
    </Tooltip>
  );
};
