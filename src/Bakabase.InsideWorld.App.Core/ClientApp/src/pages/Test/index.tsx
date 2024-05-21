import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { useTranslation } from 'react-i18next';
import { ListboxItem } from '@nextui-org/react';
import Psc from './cases/Psc';
import Tour from './cases/Tour';
import Sortable from './cases/Sortable';
import MediaPreviewer from './cases/MediaPreviewer';
import { Button, Divider, Listbox } from '@/components/bakaui';
import SimpleLabel from '@/components/SimpleLabel';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import PropertySelector from '@/components/PropertySelector';
import AntdMenu from '@/layouts/BasicLayout/components/PageNav/components/AntdMenu';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IFilter } from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';
import OrderSelector from '@/pages/Resource/components/FilterPanel/OrderSelector';


const components = {
  Psc: <Psc />,
  Tour: <Tour />,
  ResourceOrderSelector: <OrderSelector />,
  Menu: <AntdMenu />,
  Sortable: <Sortable />,
  FileSelector: (
    <Button
      type={'normal'}
      onClick={() => {
        FileSystemSelectorDialog.show({
          targetType: 'file',
          startPath: 'D:\\FE Test',
        });
      }}
    >File Selector</Button>
  ),
  FolderSelector: (
    <Button
      type={'normal'}
      onClick={() => {
        FileSystemSelectorDialog.show({
          targetType: 'folder',
          startPath: 'D:\\FE Test',
        });
      }}
    >Folder Selector</Button>
  ),
  SimpleLabel: (
    ['dark', 'light'].map(t => {
      return (
        <div
          className={`iw-theme-${t}`}
          style={{
          background: 'var(--theme-body-background)',
          padding: 10,
        }}
        >
          {['default', 'primary', 'success', 'warning', 'info', 'danger'].map(s => {
            return (
              <SimpleLabel status={s}>
                {s}
              </SimpleLabel>
            );
          })}
        </div>
      );
    })
  ),
  MediaPreviewer: (<MediaPreviewer />),
};


// Render the form with all the properties we just defined passed
// as props
export default () => {
  const { t } = useTranslation();

  const { createPortal } = useBakabaseContext();

  useEffect(() => {
  }, []);

  return (
    <div className={'flex items-start gap-2 max-h-full h-full'}>
      <div className={'border-small px-1 py-2 rounded-small border-default-200 dark:border-default-100'}>
        <Listbox onAction={k => {
          const tk: keyof typeof components = k as any;
          document.getElementById(tk)?.scrollIntoView();
        }}
        >
          {Object.keys(components).map(c => {
            return (
              <ListboxItem key={c}>{c}</ListboxItem>
            );
          })}
        </Listbox>
      </div>
      <div className={'flex flex-col gap-2 grow max-h-full h-full overflow-auto'}>
        {Object.keys(components).map(c => {
          return (
            <>
              <div id={c} className={''}>
                {components[c]}
              </div>
              <Divider />
            </>
          );
        })}
      </div>
    </div>
  );
};
