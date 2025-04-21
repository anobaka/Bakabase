import React, { useEffect } from 'react';
import './index.scss';
import { useTranslation } from 'react-i18next';
import { ListboxItem } from '@heroui/react';
import { useCookie } from 'react-use';
import Psc from './cases/Psc';
import Tour from './cases/Tour';
import Sortable from './cases/Sortable';
import MediaPreviewer from './cases/MediaPreviewer';
import CategoryEnhancerOptionsDialog from './cases/CategoryEnhancerOptionsDialog';
import ResourceFilter from './cases/ResourceFilter';
import Properties from './cases/Properties';
import { Button, Listbox } from '@/components/bakaui';
import SimpleLabel from '@/components/SimpleLabel';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import AntdMenu from '@/layouts/BasicLayout/components/PageNav/components/AntdMenu';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import OrderSelector from '@/pages/Resource/components/FilterPanel/OrderSelector';
import VirtualList from '@/pages/Test/cases/VirtualList';
import ResourceTransfer from '@/pages/Test/cases/ResourceTransfer';
import { ProcessValueEditor } from '@/pages/BulkModification2/components/BulkModification/ProcessValue';
import { StandardValueType } from '@/sdk/constants';


const components = {
  Properties: <Properties />,
  BulkModification: <ProcessValueEditor valueType={StandardValueType.Boolean} />,
  ResourceTransfer: <ResourceTransfer />,
  Filter: <ResourceFilter />,
  VirtualList: <VirtualList />,
  CategoryEnhancerOptions: <CategoryEnhancerOptionsDialog />,
  Psc: <Psc />,
  Tour: <Tour />,
  ResourceOrderSelector: <OrderSelector />,
  Menu: <AntdMenu />,
  Sortable: <Sortable />,
  FileSelector: (
    <Button
      onClick={() => {
        FileSystemSelectorDialog.show({
          targetType: 'file',
          startPath: 'I:\\Test\\updater\\AppData\\configs\\updater.json',
          defaultSelectedPath: 'I:\\Test\\updater\\AppData\\configs\\updater.json',
        });
      }}
    >File Selector</Button>
  ),
  FolderSelector: (
    <Button
      onClick={() => {
        FileSystemSelectorDialog.show({
          targetType: 'folder',
          startPath: 'I:\\Test',
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
  const [testingKey, setTestingKey] = useCookie('test-component-key');

  useEffect(() => {
    if (testingKey == null) {
      setTestingKey(Object.keys(components)[0]);
    }
  }, []);

  return (
    <div className={'flex items-start gap-2 max-h-full h-full'}>
      <div className={'border-small px-1 py-2 rounded-small border-default-200 dark:border-default-100'}>
        <Listbox
          onAction={k => {
          // const tk: keyof typeof components = k as any;
          // document.getElementById(tk)?.scrollIntoView();
            setTestingKey(k as string);
        }}
          selectedKeys={testingKey ? [testingKey] : undefined}
        >
          {Object.keys(components).map(c => {
            return (
              <ListboxItem key={c}>{c}</ListboxItem>
            );
          })}
        </Listbox>
      </div>
      <div className={'flex flex-col gap-2 grow max-h-full h-full overflow-auto'}>
        {/* {Object.keys(components).map(c => { */}
        {/*   return ( */}
        {/*     <> */}
        {/*       <div id={c} className={''}> */}
        {/*         {components[c]} */}
        {/*       </div> */}
        {/*       <Divider /> */}
        {/*     </> */}
        {/*   ); */}
        {/* })} */}
        <div id={testingKey} className={''}>
          {components[testingKey]}
        </div>
      </div>
    </div>
  );
};
