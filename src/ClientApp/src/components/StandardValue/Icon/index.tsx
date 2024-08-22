import { StandardValueType } from '@/sdk/constants';
import type { CustomIconProps } from '@/components/CustomIcon';
import CustomIcon from '@/components/CustomIcon';

interface IProps extends Omit<CustomIconProps, 'type'>{
  valueType: StandardValueType;
}

const StandardValueTypeIconMap: Record<StandardValueType, string> = {
  [StandardValueType.String]: 'single-line-text',
  [StandardValueType.ListString]: 'multiline-text',
  [StandardValueType.Decimal]: 'number',
  [StandardValueType.Link]: 'link',
  [StandardValueType.Boolean]: 'checkbox',
  [StandardValueType.DateTime]: 'date-time',
  [StandardValueType.Time]: 'time',
  [StandardValueType.ListListString]: 'multi_level',
};

export default ({ valueType, ...props }: IProps) => {
  return (
    <CustomIcon
      type={StandardValueTypeIconMap[valueType]}
      {...props}
    />
  );
};
