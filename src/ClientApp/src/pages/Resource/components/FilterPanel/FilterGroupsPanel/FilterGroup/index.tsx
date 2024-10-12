import { useTranslation } from 'react-i18next';
import React, { useEffect, useRef } from 'react';
import { useUpdateEffect } from 'react-use';
import type { Root } from 'react-dom/client';
import { createRoot } from 'react-dom/client';
import { AppstoreOutlined, FilterOutlined, SearchOutlined } from '@ant-design/icons';
import type { ResourceSearchFilter, ResourceSearchFilterGroup } from '../models';
import { GroupCombinator } from '../models';
import styles from './index.module.scss';
import Filter from './Filter';
import ClickableIcon from '@/components/ClickableIcon';
import { Button, Popover } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import QuickFilter from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/QuickFilter';

interface IProps {
  group: ResourceSearchFilterGroup;
  onRemove?: () => void;
  onChange?: (group: ResourceSearchFilterGroup) => void;
  isRoot?: boolean;
  portalContainer?: any;
}

const FilterGroup = ({
                       group: propsGroup,
                       onRemove,
                       onChange,
                       isRoot = false,
                       portalContainer,
                     }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [group, setGroup] = React.useState<ResourceSearchFilterGroup>(propsGroup);
  const groupRef = useRef(group);


  useEffect(() => {
    let portal: Root;
    if (portalContainer) {
      portal = createRoot(portalContainer);
      portal.render(renderAddHandler());
    }

    return () => {
      portal?.unmount();
    };
  }, []);

  useUpdateEffect(() => {
    groupRef.current = group;
    onChange?.(group);
  }, [group]);

  useUpdateEffect(() => {
    setGroup(propsGroup);
    // console.log(propsGroup, 123);
  }, [propsGroup]);

  const {
    filters,
    groups,
    combinator,
  } = group;

  const conditionElements: any[] = (filters || []).map((f, i) => (
    <Filter
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
          className={'min-w-fit pl-2 pr-2'}
          color={'default'}
          variant={'light'}
          size={'sm'}
          onClick={() => {
            setGroup({
              ...group,
              combinator: GroupCombinator.And == group.combinator ? GroupCombinator.Or : GroupCombinator.And,
            });
          }}
        >
          {t(`Combinator.${GroupCombinator[combinator]}`)}
        </Button>,
      );
    }
    return acc;
  }, []);

  const renderAddHandler = () => {
    return (
      <Popover
        showArrow
        trigger={(
          <Button
            size={'sm'}
            isIconOnly
          >
            <ClickableIcon
              colorType={'normal'}
              type={'add-filter'}
              className={'text-xl'}
            />
          </Button>
        )}
        placement={'bottom'}
        style={{ zIndex: 10 }}
      >
        <div
          className={'grid items-center gap-2 my-3 mx-1'}
          style={{ gridTemplateColumns: 'auto auto' }}
        >
          <QuickFilter onAdded={newFilter => {
            setGroup({
              ...groupRef.current,
              filters: [
                ...(groupRef.current.filters || []),
                newFilter,
              ],
            });
          }}
          />
          <div>{t('Advance filter')}</div>
          <div className={'flex items-center gap-2'}>
            <Button
              size={'sm'}
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
              <FilterOutlined className={'text-base'} />
              {t('Filter')}
            </Button>
            <Button
              size={'sm'}
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
              <AppstoreOutlined className={'text-base'} />
              {t('Filter group')}
            </Button>
          </div>
        </div>
      </Popover>
    );
  };

  return (
    <div className={`${styles.filterGroup} p-1 ${isRoot ? styles.root : ''} ${styles.removable}`}>
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
