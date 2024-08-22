import { useTranslation } from 'react-i18next';
import { StandardValueIcon } from '@/components/StandardValue';
import type { IProperty } from '@/components/Property/models';

interface IProps {
  property: IProperty;
}

export default ({ property }: IProps) => {
const { t } = useTranslation();
  return (
    <>
      <StandardValueIcon valueType={property.dbValueType} className={'text-small'} />
      <span>{property.name}</span>
    </>
  );
};
