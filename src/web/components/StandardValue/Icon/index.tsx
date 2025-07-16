import { MdOutlineAccessTime, MdOutlineCheckBox, MdOutlineTextFormat, MdOutlineTextSnippet } from 'react-icons/md';
import type { IconType } from 'react-icons';
import { TbNumber123 } from 'react-icons/tb';
import { LuCalendarDays, LuLink, LuTags } from 'react-icons/lu';
import { PiTreeView } from 'react-icons/pi';
import { StandardValueType } from '@/sdk/constants';

type Props = IconType & {
  valueType: StandardValueType;
};

const StandardValueTypeIconMap: Record<StandardValueType, IconType> = {
  [StandardValueType.String]: MdOutlineTextFormat,
  [StandardValueType.ListString]: MdOutlineTextSnippet,
  [StandardValueType.Decimal]: TbNumber123,
  [StandardValueType.Link]: LuLink,
  [StandardValueType.Boolean]: MdOutlineCheckBox,
  [StandardValueType.DateTime]: LuCalendarDays,
  [StandardValueType.Time]: MdOutlineAccessTime,
  [StandardValueType.ListListString]: PiTreeView,
  [StandardValueType.ListTag]: LuTags,
};

export default ({ valueType, ...props }: Props) => {
  const Icon = StandardValueTypeIconMap[valueType];
  return (
    <Icon
      className={'text-medium'}
      {...props}
    />
  );
};
