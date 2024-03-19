import { useTranslation } from 'react-i18next';
import { Button, Dropdown, Menu } from '@alifd/next';
import React from 'react';
import type { IGroup } from '../../models';
import { GroupCombinator } from '../../models';
import styles from './index.module.scss';
import Filter from './components/Filter';
import ClickableIcon from '@/components/ClickableIcon';
import CustomIcon from '@/components/CustomIcon';

interface IProps {
  group: IGroup;
  isRoot: boolean;
  onRemove?: () => void;
}

const FilterGroup = ({
                       group: propsGroup,
                       isRoot,
                       onRemove,
                     }: IProps) => {
  const { t } = useTranslation();
  const [group, setGroup] = React.useState<IGroup>(propsGroup);

  const {
    filters,
    groups,
    combinator,
  } = group;

  const conditionElements: any[] = (filters || []).map(f => (
    <Filter
      filter={f}
      onRemove={() => {
        setGroup(
          {
            ...group,
            filters: (group.filters || []).filter(fil => fil !== f),
          },
        );
      }}
    />
  )).concat((groups || []).map(g => (
    <FilterGroup
      group={g}
      isRoot={false}
      onRemove={() => {
        setGroup({
          ...group,
          groups: (group.groups || []).filter(gr => gr !== g),
        });
      }}
    />
  )));

  const allElements = conditionElements.reduce((acc, el, i) => {
    acc.push(el);
    if (i < conditionElements.length - 1) {
      acc.push(
        <Button
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
      <Dropdown
        trigger={(
          <ClickableIcon colorType={'normal'} type={'plus-circle'} />
        )}
        align={'tl tr'}
        triggerType={'click'}
      >
        <Menu>
          <Menu.Item
            className={styles.addMenuItem}
            onClick={() => {
              setGroup({
                ...group,
                filters: [
                  ...(group.filters || []),
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
                ...group,
                groups: [
                  ...(group.groups || []),
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
    </div>
  );
};

export default FilterGroup;
