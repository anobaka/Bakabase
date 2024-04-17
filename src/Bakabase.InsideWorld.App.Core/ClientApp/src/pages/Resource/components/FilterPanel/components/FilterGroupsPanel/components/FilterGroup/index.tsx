import { useTranslation } from 'react-i18next';
import { Button, Dropdown, Menu } from '@alifd/next';
import React, { useEffect, useRef } from 'react';
import { useUpdateEffect } from 'react-use';
import ReactDOM from 'react-dom';
import type { IGroup } from '../../models';
import { GroupCombinator } from '../../models';
import styles from './index.module.scss';
import Filter from './components/Filter';
import ClickableIcon from '@/components/ClickableIcon';
import CustomIcon from '@/components/CustomIcon';
import type { IProperty } from '@/components/Property/models';

interface IProps {
  group: IGroup;
  onRemove?: () => void;
  onChange?: (group: IGroup) => void;
  isRoot?: boolean;
  portalContainer?: any;
  propertyMap: Record<number, IProperty>;
}

const FilterGroup = ({
                       group: propsGroup,
                       onRemove,
                       onChange,
                       isRoot = false,
                       portalContainer,
                       propertyMap,
                     }: IProps) => {
  const { t } = useTranslation();
  const [group, setGroup] = React.useState<IGroup>(propsGroup);
  const groupRef = useRef(group);

  useEffect(() => {
    if (portalContainer) {
      ReactDOM.render(renderAddHandler(), portalContainer);
    }

    return () => {
      if (portalContainer) {
        ReactDOM.unmountComponentAtNode(portalContainer);
      }
    };
  }, []);

  useUpdateEffect(() => {
    groupRef.current = group;
    onChange?.(group);
  }, [group]);

  useUpdateEffect(() => {
    setGroup(propsGroup);
    console.log(propsGroup, 123);
  }, [propsGroup]);

  const {
    filters,
    groups,
    combinator,
  } = group;

  const conditionElements: any[] = (filters || []).map((f, i) => (
    <Filter
      propertyMap={propertyMap}
      key={`f-${i}`}
      filter={f}
      onRemove={() => {
        setGroup(
          {
            ...group,
            filters: (group.filters || []).filter(fil => fil !== f),
          },
        );
      }}
      onChange={tf => {
        setGroup(
          {
            ...group,
            filters: (group.filters || []).map(fil => (fil === f ? tf : fil)),
          },
        );
      }}
    />
  )).concat((groups || []).map((g, i) => (
    <FilterGroup
      propertyMap={propertyMap}
      key={`g-${i}`}
      group={g}
      onRemove={() => {
        setGroup({
          ...group,
          groups: (group.groups || []).filter(gr => gr !== g),
        });
      }}
      onChange={tg => {
        setGroup({
          ...group,
          groups: (group.groups || []).map(gr => (gr === g ? tg : gr)),
        });
      }}
    />
  )));

  const allElements = conditionElements.reduce((acc, el, i) => {
    acc.push(el);
    if (i < conditionElements.length - 1) {
      acc.push(
        <Button
          key={`c-${i}`}
          type={'primary'}
          text
          className={styles.combinator}
          onClick={() => {
            setGroup({
              ...group,
              combinator: GroupCombinator.And == group.combinator ? GroupCombinator.Or : GroupCombinator.And,
            });
          }}
        >{t(GroupCombinator[combinator])}</Button>,
      );
    }
    return acc;
  }, []);

  const renderAddHandler = () => {
    return (
      <Dropdown
        trigger={(
          <ClickableIcon
            colorType={'normal'}
            type={'add-filter'}
            className={'text-xl'}
          />
        )}
        align={'tl tr'}
        triggerType={'click'}
      >
        <Menu>
          <Menu.Item
            className={styles.addMenuItem}
            onClick={() => {
              setGroup({
                ...groupRef.current,
                filters: [
                  ...(groupRef.current.filters || []),
                  {},
                ],
              });
            }}
          >
            <div className={styles.text}>
              <CustomIcon
                type={'filter-records'}
              />
              {t('Filter')}
            </div>
          </Menu.Item>
          <Menu.Item
            className={styles.addMenuItem}
            onClick={() => {
              setGroup({
                ...groupRef.current,
                groups: [
                  ...(groupRef.current.groups || []),
                  { combinator: GroupCombinator.And },
                ],
              });
            }}
          >
            <div className={styles.text}>
              <CustomIcon
                type={'unorderedlist'}
              />
              {t('Filter group')}
            </div>
          </Menu.Item>
        </Menu>
      </Dropdown>
    );
  };

  return (
    <div className={`${styles.filterGroup} ${isRoot ? styles.root : ''} ${styles.removable}`}>
      <ClickableIcon
        colorType={'danger'}
        className={styles.remove}
        type={'delete'}
        size={'small'}
        onClick={() => {
          onRemove?.();
        }}
      />
      {allElements}
      {!portalContainer && (
        renderAddHandler()
      )}
    </div>
  );
};

export default FilterGroup;
