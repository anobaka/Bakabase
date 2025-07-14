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
import PresetMediaLibraryTemplateBuilderTest from './cases/PresetMediaLibraryTemplateBuilderTest';
import LongTabs from './cases/LongTabs';
import ReactPlayer from './cases/ReactPlayer';
import HlsPlayer from './cases/HlsPlayer';
import { Button, Listbox } from '@/components/bakaui';
import SimpleLabel from '@/components/SimpleLabel';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import AntdMenu from '@/layouts/BasicLayout/components/PageNav/components/AntdMenu';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import OrderSelector from '@/pages/Resource/components/FilterPanel/OrderSelector';
import VirtualList from '@/pages/Test/cases/VirtualList';
import ResourceTransfer from '@/pages/Test/cases/ResourceTransfer';
import { ProcessValueEditor } from '@/pages/BulkModification2/components/BulkModification/ProcessValue';
import { PropertyType, StandardValueType } from '@/sdk/constants';
import PropertyMatcher from '@/components/PropertyMatcher';
import FileNameModifierTest from './cases/FileNameModifierTest';
import BetaChip from '@/components/Chips/BetaChip';
import DeprecatedChip from '@/components/Chips/DeprecatedChip';


const components = {
  FileNameModifierTest: <FileNameModifierTest />,
  PropertyMatcher: <PropertyMatcher type={PropertyType.Attachment} name={'封面3'} onValueChanged={console.log} />,
  BetaChip: (
    <div className={'flex flex-wrap gap-2 items-center'}>
      <BetaChip />
      <BetaChip size="md" color="primary" />
      <BetaChip size="lg" color="success" variant="solid" />
      <BetaChip color="danger" variant="bordered" tooltipContent="Custom tooltip content" />
      <BetaChip showTooltip={false} />
    </div>
  ),
  DeprecatedChip: (
    <div className={'flex flex-wrap gap-2 items-center'}>
      <DeprecatedChip />
      <DeprecatedChip size="md" color="warning" />
      <DeprecatedChip size="lg" color="danger" variant="solid" />
      <DeprecatedChip color="secondary" variant="bordered" tooltipContent="Custom deprecated message" />
      <DeprecatedChip showTooltip={false} />
    </div>
  ),
  PresetMediaLibraryTemplateBuilder: <PresetMediaLibraryTemplateBuilderTest />,
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
  LongTabs: <LongTabs />,
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
  HlsPlayer: <HlsPlayer src={'http://localhost:5000/file/play?fullname=Z%3A%5CAnime%5CAdded%20recently%5CArcane%20S01%5CS01E01%20-%20Welcome%20to%20the%20Playground.mkv'} />,
  ReactPlayer: <ReactPlayer />,
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
    <div>
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
    </div>
  );
};
