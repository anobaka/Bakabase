'use strict';

import { useTranslation } from 'react-i18next';
import React, { useEffect, useRef } from 'react';
import { useUpdateEffect } from 'react-use';
import type { Root } from 'react-dom/client';
import { createRoot } from 'react-dom/client';
import { ApiOutlined, AppstoreOutlined, DeleteOutlined, DisconnectOutlined, FilterOutlined } from '@ant-design/icons';
import type { ResourceSearchFilterGroup } from '../models';
import { GroupCombinator } from '../models';
import styles from './index.module.scss';
import Filter from './Filter';
import ClickableIcon from '@/components/ClickableIcon';
import { Button, Divider, Popover, Tooltip } from '@/components/bakaui';
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
  }, [group]);

  useUpdateEffect(() => {
    setGroup(propsGroup);
  }, [propsGroup]);

  const changeGroup = (newGroup: ResourceSearchFilterGroup) => {
    setGroup(newGroup);
    onChange?.(newGroup);
  };

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
        changeGroup(
          {
            ...group,
            filters: (group.filters || []).filter(fil => fil !== f),
          },
        );
      }}
      onChange={tf => {
        changeGroup(
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
        changeGroup({
          ...group,
          groups: (group.groups || []).filter(gr => gr !== g),
        });
      }}
      onChange={tg => {
        changeGroup({
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
            changeGroup({
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
            changeGroup({
              ...groupRef.current,
              filters: [
                ...(groupRef.current.filters || []),
                newFilter,
              ],
            });
          }}
          />
          <div />
          <Divider orientation={'horizontal'} />
          <div>{t('Advance filter')}</div>
          <div className={'flex items-center gap-2'}>
            <Button
              size={'sm'}
              onClick={() => {
                changeGroup({
                  ...groupRef.current,
                  filters: [
                    ...(groupRef.current.filters || []),
                    { disabled: false },
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
                changeGroup({
                  ...groupRef.current,
                  groups: [
                    ...(groupRef.current.groups || []),
                    {
                      combinator: GroupCombinator.And,
                      disabled: false,
                    },
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

  const renderGroup = () => {
    return (
      <div
        className={`${styles.filterGroup} p-1 ${isRoot ? styles.root : ''} ${styles.removable} relative`}
      >
        {group.disabled && (
          <div
            className={'absolute top-0 left-0 w-full h-full flex items-center justify-center z-20 group/group-disable-cover rounded cursor-not-allowed'}
            style={{ backgroundColor: 'hsla(from var(--bakaui-color) h s l / 50%)' }}
          >
            <Tooltip
              content={t('Click to enable')}
            >
              <Button
                size={'sm'}
                variant={'light'}
                isIconOnly
                onClick={() => {
                  changeGroup({
                    ...group,
                    disabled: false,
                  });
                }}
              >
                <ApiOutlined className={'text-base group-hover/group-disable-cover:block text-success hidden'} />
                <DisconnectOutlined className={'text-base group-hover/group-disable-cover:hidden block'} />
              </Button>
            </Tooltip>
          </div>
        )}
        {allElements}
        {
          !portalContainer && (
            renderAddHandler()
          )
        }
      </div>
    );
  };

  return isRoot ? renderGroup() : (
    <Tooltip content={(
      <div
        className={'flex items-center gap-1'}
      >
        <Button
          size={'sm'}
          variant={'light'}
          color={'warning'}
          onClick={() => {
            changeGroup({
              ...group,
              disabled: true,
            });
          }}
        >
          <DisconnectOutlined className={'text-base'} />
          {t('Disable group')}
        </Button>
        <Button
          size={'sm'}
          variant={'light'}
          color={'danger'}
          onClick={onRemove}
        >
          <DeleteOutlined className={'text-base'} />
          {t('Delete group')}
        </Button>
      </div>
    )}
    >
      {renderGroup()}
    </Tooltip>
  );
};

export default FilterGroup;
