import {
  AppstoreAddOutlined,
  DashboardOutlined,
  EyeOutlined,
  FullscreenOutlined,
  PlayCircleOutlined,
  QuestionCircleOutlined,
  RightSquareOutlined,
  ZoomInOutlined,
} from '@ant-design/icons';
import React, { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Dropdown, DropdownItem, DropdownMenu, DropdownTrigger, Tooltip } from '@/components/bakaui';
import { CoverFit, ResourceDisplayContent, resourceDisplayContents } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import store from '@/store';
import type { BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions } from '@/sdk/Api';
import { buildLogger } from '@/components/utils';

type Props = {
  rearrangeResources?: () => any;
};

const log = buildLogger('MiscellaneousOptions');

type Item = {
  key: ListBoxItemKey;
  label: string;
  Icon: React.FC<{ className?: string }>;
  tip?: any;
};

type ListBoxItemKey = 'FillCover' | 'ShowLargerCoverOnHover' | 'PreviewOnHover' | 'UseCache' ;

export default ({ rearrangeResources }: Props) => {
  const { t } = useTranslation();
  const uiOptions = store.useModelState('uiOptions');
  const options = uiOptions.resource;

  const currentResourceDisplayContents = uiOptions.resource?.displayContents ?? ResourceDisplayContent.All;
  const selectableResourceDisplayContents = resourceDisplayContents.filter(d => d.value != ResourceDisplayContent.All).map(x => ({
    label: t('Show') + t(x.label),
    value: x.value.toString(),
  }));

  const items: Item[] = [
    ...selectableResourceDisplayContents.map(d => ({
      key: `DisplayContent-${d.value.toString()}` as ListBoxItemKey,
      label: d.label,
      Icon: EyeOutlined,
    })),
    {
      key: 'FillCover',
      label: t('Fill cover'),
      Icon: FullscreenOutlined,
    },
    {
      key: 'ShowLargerCoverOnHover',
      label: t('Show larger cover on mouse hover'),
      Icon: ZoomInOutlined,
    },
    {
      key: 'PreviewOnHover',
      label: t('Preview files of a resource on mouse hover'),
      Icon: PlayCircleOutlined,
    },
    {
      key: 'UseCache',
      label: t('Use cache'),
      Icon: DashboardOutlined,
      tip: (
        <div className={'max-w-[400px]'}>
          {t('Enabling cache can improve loading speed, but your covers and playable files will not be updated in time unless you clear or disable cache manually.')}
        </div>
      ),
    },
    {
      key: 'CoverCarousel',
      label: t('Cover carousel'),
      Icon: RightSquareOutlined,
    },
  ];

  const buildSelectedKeys = () => {
    const keys: ListBoxItemKey[] = [];
    if (options?.coverFit === CoverFit.Cover) {
      keys.push('FillCover');
    }
    if (!options?.disableCache) {
      keys.push('UseCache');
    }
    if (options?.showBiggerCoverWhileHover) {
      keys.push('ShowLargerCoverOnHover');
    }
    if (!options?.disableMediaPreviewer) {
      keys.push('PreviewOnHover');
    }
    for (const d of selectableResourceDisplayContents) {
      if (currentResourceDisplayContents & d.value) {
        keys.push(`DisplayContent-${d.value}` as ListBoxItemKey);
      }
    }

    if (!options?.disableCoverCarousel) {
      keys.push('CoverCarousel');
    }

    return keys;
  };

  const selectedKeys = buildSelectedKeys();

  return (
    <Dropdown>
      <DropdownTrigger>
        <Button
          size={'sm'}
          isIconOnly
          variant={'light'}
        >
          <AppstoreAddOutlined
            className={'text-xl'}
          />
        </Button>
      </DropdownTrigger>
      <DropdownMenu
        aria-label="Multiple selection example"
        variant="flat"
        color={'primary'}
        closeOnSelect={false}
        selectionMode="multiple"
        // selectedKeys={selectableResourceDisplayContents.filter(c => currentResourceDisplayContents & c.value).map(c => c.value.toString())}
        selectedKeys={selectedKeys}
        onSelectionChange={keys => {
          if (keys instanceof Set) {
            const stringKeys: ListBoxItemKey[] = Array.from(keys).map(k => k.toString() as ListBoxItemKey);
            let dc: ResourceDisplayContent = 0;
            for (const key of stringKeys) {
              if (key.startsWith('DisplayContent-')) {
                dc |= parseInt(key.replace('DisplayContent-', ''), 10);
              }
            }

            const newOptions: BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions = {
              ...options,
              coverFit: stringKeys.includes('FillCover') ? CoverFit.Cover : CoverFit.Contain,
              disableCache: !stringKeys.includes('UseCache'),
              showBiggerCoverWhileHover: stringKeys.includes('ShowLargerCoverOnHover'),
              disableMediaPreviewer: !stringKeys.includes('PreviewOnHover'),
              displayContents: dc,
              disableCoverCarousel: !stringKeys.includes('CoverCarousel'),
            };

            log(keys, newOptions);
            BApi.options.patchUiOptions({
              resource: newOptions,
            }).then(r => {
              if ((currentResourceDisplayContents & ResourceDisplayContent.Tags) != (dc & ResourceDisplayContent.Tags)) {
                rearrangeResources?.();
              }
            });
          }
        }}
      >
        {items.map(item => {
          return (
            <DropdownItem
              startContent={(
                <item.Icon className={'text-base'} />
              )}
              className={selectedKeys.includes(item.key) ? 'text-primary' : ''}
              key={item.key}
            >
              {item.tip ? (
                <div className={'flex items-center gap-1'}>
                  {item.label}
                  <Tooltip
                    placement={'left'}
                    content={item.tip}
                    color={'primary'}
                  >
                    <QuestionCircleOutlined className={'text-base'} />
                  </Tooltip>
                </div>
              ) : (
                item.label
              )}
            </DropdownItem>
          );
        })}
      </DropdownMenu>
    </Dropdown>
  );
};
