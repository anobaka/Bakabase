import React, { useEffect, useRef, useState } from 'react';
import './index.scss';

import type { FieldProps, RegistryFieldsType, RJSFSchema, UiSchema } from '@rjsf/utils';
import { Nav } from '@alifd/next';
import { useTour } from '@reactour/tour';
import { useTranslation } from 'react-i18next';
import { CloseCircleFilled, DoubleRightOutlined } from '@ant-design/icons';
import { createPortal } from 'react-dom';
import { Tooltip, Button, Chip, Modal } from '@/components/bakaui';
import MediaPreviewer from '@/components/MediaPreviewer';
import SimpleLabel from '@/components/SimpleLabel';
import FileSystemSelector from '@/components/FileSystemSelector';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';
import Sortable from '@/pages/Test/components/Sortable';
import PropertySelector from '@/components/PropertySelector';
import AntdMenu from '@/layouts/BasicLayout/components/PageNav/components/AntdMenu';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import type { IFilter } from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';
import OrderSelector from '@/pages/Resource/components/FilterPanel/OrderSelector';
import { generateTrees } from '@/pages/Test/data/tree';
import ResourceDetailDialog from '@/components/Resource/components/DetailDialog';

const schema: RJSFSchema = {
  type: 'object',
  required: ['lat', 'lon'],
  properties: {
    lat: { type: 'number' },
    lon: { type: 'number' },
  },
};

function MyComponent() {
  const { isOpen, currentStep, steps, setIsOpen, setCurrentStep, setSteps } = useTour();

  console.log('current steps', steps);

  return (
    <>
      <h1>{isOpen ? 'Welcome to the tour!' : 'Thank you for participate!'}</h1>
      <p className={'new-place-1'}>
        Now you are visiting the place {currentStep + 1} of {steps.length}
      </p>
      <nav>
        <button onClick={() => setIsOpen(o => !o)}>Toggle Tour</button>
        <button onClick={() => setCurrentStep(3)}>
          Take a fast way to 4th place
        </button>
        <button
          onClick={() => {
              setSteps!([
                { selector: '.new-place-1', content: 'New place 1' },
                { selector: '.new-place-2', content: 'New place 2' },
              ]);
              setCurrentStep(1);
            }}
        >
          Switch to a new set of places, starting from the last one!
        </button>
      </nav>
    </>
  );
}

// Define a custom component for handling the root position object
class GeoPosition extends React.Component<FieldProps> {
  constructor(props: FieldProps) {
    super(props);
    this.state = { ...props.formData };
  }

  onChange(name) {
    return (event) => {
      this.setState(
        {
          [name]: parseFloat(event.target.value),
        },
        () => {
          console.log(this.state);
          this.props.onChange(this.state);
        },
      );
    };
  }

  render() {
    const { lat, lon } = this.state;
    return (
      <div>
        <input type="number" value={lat} onChange={this.onChange('lat')} />
        <input type="number" value={lon} onChange={this.onChange('lon')} />
      </div>
    );
  }
}

// Define the custom field component to use for the root object
const uiSchema: UiSchema = { 'ui:field': 'geo' };


// Render the form with all the properties we just defined passed
// as props
export default () => {
  const { t } = useTranslation();
  const hoverTimerRef = useRef<any>();
  const [previewerVisible, setPreviewerVisible] = useState(false);

  const [filter, setFilter] = useState<IFilter>({});
  const { createPortal } = useBakabaseContext();

  useEffect(() => {
  }, []);

  return (
    <div className={'test-page'}>
      <Tooltip content="I am a tooltip">
        <Chip>123456</Chip>
      </Tooltip>

      <Button
        size={'sm'}
        color={'primary'}
        onClick={() => {
          const trees = generateTrees();
          createPortal(Modal, {
            defaultVisible: true,
            // children: renderTreeNodes(trees),
            size: 'xl',
          });
              }}
      >
        Open multilevel value editor
      </Button>

      <OrderSelector />

      <AntdMenu />
      <Nav style={{ width: '200px' }} type={'normal'} defaultOpenAll>
        <Nav.Item icon="account">Navigation One</Nav.Item>
        <Nav.Item icon="account">Navigation Two</Nav.Item>
        <Nav.Item disabled icon="account">
          Navigation Three
        </Nav.Item>
        <Nav.Item icon="account">Navigation Four</Nav.Item>
        <Nav.Item icon="account">Navigation Five</Nav.Item>
        <Nav.SubNav disabled icon="account" label="Sub Nav">
          <Nav.Item icon="account">Nav.Item 1</Nav.Item>
          <Nav.Item icon="account">Nav.Item 2</Nav.Item>
          <Nav.Item icon="account">Nav.Item 3</Nav.Item>
          <Nav.Item icon="account">Nav.Item 4</Nav.Item>
        </Nav.SubNav>
      </Nav>
      <Button
        type={'primary'}
        text
        onClick={() => {
          PropertySelector.show({
            selection: { [filter.isReservedProperty ? 'reservedPropertyIds' : 'customPropertyIds']: filter.propertyId == undefined ? undefined : [filter.propertyId] },
            onSubmit: async (selectedProperties) => {
              const property = (selectedProperties.reservedProperties?.[0] ?? selectedProperties.customProperties?.[0])!;
              const cp = property as ICustomProperty;
              setFilter({
                ...filter,
                propertyId: property.id,
                propertyName: property.name,
                isReservedProperty: cp == undefined,
              });
            },
            multiple: false,
            pool: 'all',
          });
        }}
        size={'small'}
      >
        {filter.propertyId ? filter.propertyName : t('Property')}
      </Button>
      <Sortable />
      <MyComponent />
      <Button
        type={'normal'}
        onClick={() => {
                FileSystemSelectorDialog.show({ targetType: 'file', startPath: 'D:\\FE Test' });
              }}
      >File Selector</Button>

      <Button
        type={'normal'}
        onClick={() => {
          FileSystemSelectorDialog.show({ targetType: 'folder', startPath: 'D:\\FE Test' });
        }}
      >Folder Selector</Button>

      {['dark', 'light'].map(t => {
        return (
          <div className={`iw-theme-${t}`} style={{ background: 'var(--theme-body-background)', padding: 10 }}>
            {['default', 'primary', 'success', 'warning', 'info', 'danger'].map(s => {
              return (
                <SimpleLabel status={s}>
                  {s}
                </SimpleLabel>
              );
            })}
          </div>
        );
      })}
      <div
        className={'media-previewer-container'}
        onMouseOver={() => {
          if (!hoverTimerRef.current) {
            hoverTimerRef.current = setTimeout(() => {
              setPreviewerVisible(true);
            }, 1000);
          }
        }}
        onMouseLeave={() => {
          clearTimeout(hoverTimerRef.current);
          hoverTimerRef.current = undefined;
          if (previewerVisible) {
            setPreviewerVisible(false);
          }
        }}
      >
        {previewerVisible && (
          <MediaPreviewer resourceId={2501} />
        )}
      </div>
    </div>
  );
};

// export default () => {
//   const [samplePath, setSamplePath] = useState('D:\\test\\new-media-library-path-configuration\\a\\bc\\New Text Document.txt');
//   const [value, setValue] = useState({});
//   const segmentsRef = useRef(samplePath.split('\\'));
//
//   const simpleMatchers = {
//     [ResourceProperty.Resource]: false,
//     [ResourceProperty.RootPath]: false,
//     [ResourceProperty.ParentResource]: false,
//     [ResourceProperty.ReleaseDt]: false,
//     [ResourceProperty.Publisher]: false,
//     [ResourceProperty.Name]: false,
//     [ResourceProperty.Language]: false,
//     [ResourceProperty.Volume]: false,
//     [ResourceProperty.Original]: false,
//     [ResourceProperty.Series]: false,
//     [ResourceProperty.Tag]: false,
//     // [MatcherType.Introduction]: false,
//     [ResourceProperty.Rate]: false,
//     [ResourceProperty.CustomProperty]: false,
//   };
//   const matchers = Object.keys(simpleMatchers)
//     .reduce<PathSegmentConfigurationPropsMatcherOptions[]>((ts, t) => {
//       ts.push(new PathSegmentConfigurationPropsMatcherOptions({
//         type: parseInt(t),
//         readonly: simpleMatchers[t],
//       }));
//       return ts;
//     }, []);
//
//   const matchersRef = useRef(matchers);
//   const onChangeCallback = useCallback(v => {
//     setValue(v);
//   }, []);
//   const valueRef = useRef({ 1: ['D:/test'], 2: [{ isReverse: true, layer: 3 }], 3: ['^[^\\/]+\\/[^\\/]+\\/[^\\/]+\\/[^\\/]+$'] });
//
//   return (
//     <div className={'test-page'}>
//       <div className="psc">
//         <PathSegmentsConfiguration
//           isDirectory={false}
//           segments={segmentsRef.current}
//           matchers={matchersRef.current}
//           defaultValue={valueRef.current}
//           onChange={onChangeCallback}
//         />
//         <div className="values">
//           <div className="matcher">
//             <div className="label">Raw</div>
//             <div className="value">
//               {JSON.stringify(value)}
//             </div>
//           </div>
//           {Object.keys(value).map(m => {
//             const t = parseInt(m);
//             let v;
//             if (t == ResourceProperty.RootPath || t == ResourceProperty.Resource) {
//               v = value[t][0];
//             } else {
//               v = JSON.stringify(value[m]);
//             }
//             return (
//               <div className={'matcher'}>
//                 <div className="label">{ResourceProperty[m]}</div>
//                 <div className="value">{v}</div>
//               </div>
//             );
//           })}
//         </div>
//       </div>
//     </div>
//   );
// };
