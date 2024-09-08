import { useTranslation } from 'react-i18next';
import { Radio, RadioGroup } from '@/components/bakaui';
import { CoverSelectOrder } from '@/sdk/constants';

type Props = {
  coverSelectOrder?: CoverSelectOrder;
  onChange?: (coverSelectOrder: CoverSelectOrder) => void;
};

export default ({
                  coverSelectOrder = CoverSelectOrder.FilenameAscending,
                  onChange,
                }: Props) => {
  const { t } = useTranslation();
  return (
    <RadioGroup
      value={coverSelectOrder.toString()}
      onValueChange={c => {
        onChange?.(parseInt(c, 10));
      }}
      size={'sm'}
      orientation="horizontal"
    >
      {Object.keys(CoverSelectOrder).filter(x => !Number.isNaN(parseInt(x, 10))).map(x => {
        return (
          <Radio value={x}>{t(`CoverSelectOrder.${CoverSelectOrder[x]}`)}</Radio>
        );
      })}
    </RadioGroup>
  );
};
