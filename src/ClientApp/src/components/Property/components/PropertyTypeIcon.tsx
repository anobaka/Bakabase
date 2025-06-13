import { useTranslation } from 'react-i18next';
import type { IconType } from 'react-icons';
import {
  MdOutlineAccessTime,
  MdOutlineCheckBox,
  MdOutlinePercent,
  MdOutlineTextFormat,
} from 'react-icons/md';
import { PiImages, PiTreeView } from 'react-icons/pi';
import { TbNumber123, TbSelect } from 'react-icons/tb';
import { LuCalendarClock, LuCalendarDays, LuLink, LuTags } from 'react-icons/lu';
import { GrTextWrap } from 'react-icons/gr';
import { FaRegStar } from 'react-icons/fa6';
import { RiFormula } from 'react-icons/ri';
import { CiCircleList } from 'react-icons/ci';
import { Tooltip } from '@/components/bakaui';
import { PropertyType } from '@/sdk/constants';

type Props = {
  type: PropertyType;
  textVariant?: 'none' | 'default' | 'tooltip';
} & Omit<IconType, 'type'>;

const PropertyTypeIconMap: Record<PropertyType, IconType> = {
  [PropertyType.SingleLineText]: MdOutlineTextFormat,
  [PropertyType.MultilineText]: GrTextWrap,
  [PropertyType.SingleChoice]: TbSelect,
  [PropertyType.MultipleChoice]: CiCircleList,
  [PropertyType.Multilevel]: PiTreeView,
  [PropertyType.Number]: TbNumber123,
  [PropertyType.Percentage]: MdOutlinePercent,
  [PropertyType.Rating]: FaRegStar,
  [PropertyType.Boolean]: MdOutlineCheckBox,
  [PropertyType.Link]: LuLink,
  [PropertyType.Attachment]: PiImages,
  [PropertyType.Formula]: RiFormula,
  [PropertyType.Time]: MdOutlineAccessTime,
  [PropertyType.Date]: LuCalendarDays,
  [PropertyType.DateTime]: LuCalendarClock,
  [PropertyType.Tags]: LuTags,
};

export default ({ type, textVariant = 'default', ...props }: Props) => {
  const { t } = useTranslation();
  const Icon = PropertyTypeIconMap[type];

  switch (textVariant!) {
    case 'none':
      return (
        <Icon
          className={'text-medium'}
          {...props}
        />
      );
    case 'default':
      return (
        <div className={'flex items-center gap-1'}>
          <Icon
            className={'text-medium'}
            {...props}
          />
          <div className={'text-xs'}>{t(PropertyType[type])}</div>
        </div>
      );
    case 'tooltip':
      return (
        <Tooltip
          color={'foreground'}
          content={t(PropertyType[type])}
        >
          <Icon
            className={'text-medium'}
            {...props}
          />
        </Tooltip>
      );
  }
};
