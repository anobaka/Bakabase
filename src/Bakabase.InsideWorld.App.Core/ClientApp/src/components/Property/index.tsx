import { useTranslation } from 'react-i18next';
import { Dialog } from '@alifd/next';
import styles from './index.module.scss';
import type { IProperty } from './models';
import PropertyDialog from '@/pages/CustomProperty/components/PropertyDialog';
import CustomIcon from '@/components/CustomIcon';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import { PropertyTypeIconMap } from '@/pages/CustomProperty/models';
import ClickableIcon from '@/components/ClickableIcon';
import SimpleLabel from '@/components/SimpleLabel';

interface IProps {
  property: IProperty;
  // onSaved?: (property: IProperty) => any;
  // onRemoved?: () => any;
  onClick?: () => any;
  renderTopRight?: () => any;
  renderBottom?: () => any;
}

export default ({ property, renderTopRight, onClick, renderBottom }: IProps) => {
  const { t } = useTranslation();

  const icon = property.type == undefined ? undefined : PropertyTypeIconMap[property.type];

  return (
    <div
      key={property.id}
      className={styles.property}
      onClick={onClick}
    >
      <div className={styles.line1}>
        <div className={styles.left}>
          <div className={styles.name}>{property.name}</div>
          {icon != undefined && (
            <div className={styles.type}>
              <CustomIcon type={icon} size={'small'} />
            </div>
          )}
        </div>
        {renderTopRight && (
          <div className={styles.right}>
            {renderTopRight?.()}
          </div>
        )}
      </div>
      {renderBottom?.()}
    </div>
  );
};
