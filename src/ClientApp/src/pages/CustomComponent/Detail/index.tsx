import './index.scss';
import React, { useEffect, useRef, useState } from 'react';
import { Dialog, Input, Message, NumberPicker, Select } from '@alifd/next';
import i18n from 'i18next';
import ReactDOM from 'react-dom/client';
import type { DialogProps } from '@alifd/next/types/dialog';
import { ComponentDescriptorAdditionalItem, ComponentDescriptorType, ComponentType } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type {
  BakabaseInsideWorldBusinessComponentsResourceComponentsComponentDescriptor,
} from '@/sdk/Api';
import ComponentDescriptorCard from '@/pages/CustomComponent/components/ComponentCard';
import ComponentOptionsRender from '@/pages/CustomComponent/Detail/ComponentOptions/ComponentOptionsRender';
import { uuidv4 } from '@/components/utils';
import store from '@/store';


export interface CustomComponentDetailDialogProps extends DialogProps{
  componentType?: ComponentType;
  componentKey?: string | number;
  onClosed?: (hasChanges) => void;
}

function ComponentDetail(props: CustomComponentDetailDialogProps) {
  const {
    componentKey,
    componentType,
    onClosed = () => {
    },
    afterClose,
    ...otherProps
  } = props;

  const [descriptor, setDescriptor] = useState<BakabaseInsideWorldBusinessComponentsResourceComponentsComponentDescriptor | undefined>();
  const [baseDescriptors, setBaseDescriptors] = useState<BakabaseInsideWorldBusinessComponentsResourceComponentsComponentDescriptor[]>([]);
  const [baseDescriptor, setBaseDescriptor] = useState<BakabaseInsideWorldBusinessComponentsResourceComponentsComponentDescriptor>(undefined);
  const [visible, setVisible] = useState(true);
  const optionsFormRef = useRef();

  const init = async () => {
    let d;
    if (componentKey !== undefined) {
      const rsp = await BApi.component.getComponentDescriptorByKey({ key: componentKey.toString(), additionalItems: ComponentDescriptorAdditionalItem.AssociatedCategories });
      d = rsp.data;
    } else {
      d = {
        componentType,
      };
    }
    setDescriptor(d);

    const dsRsp = await BApi.component.getComponentDescriptors({ type: d.componentType });
    const baseDs = dsRsp.data?.filter(a => a.type == ComponentDescriptorType.Configurable) ?? [];
    setBaseDescriptors(baseDs);

    if (baseDs.length > 0) {
      const bd = baseDs.find(a => a.id == d.componentKey) ?? baseDs.find(a => a.componentType == d.componentType);
      setBaseDescriptor(bd);
    }
  };

  useEffect(() => {
    init();
    console.log('[CustomComponentDetail]Initialized', props, otherProps);
  }, []);

  const optionsJsonSchema = baseDescriptor?.optionsJsonSchema && JSON.parse(baseDescriptor.optionsJsonSchema);

  console.log(descriptor, baseDescriptor);

  const closeWithChangesRef = useRef(false);

  const close = (closeWithChanges) => {
    console.log('close?');
    closeWithChangesRef.current = closeWithChanges;
    setVisible(false);
  };

  return (
    <Dialog
      width={'auto'}
      centered
      className={'custom-component-detail-dialog'}
      title={i18n.t(ComponentType[componentType])}
      v2
      visible={visible}
      closeMode={['close', 'esc', 'mask']}
      onOk={() => new Promise((resolve, reject) => {
        if (optionsFormRef.current.validateForm()) {
          if (!(descriptor.name?.length > 0)) {
            return Message.error(i18n.t('{{key}} is not set', { key: i18n.t('Name') }));
          }

          const data = {
            name: descriptor.name,
            componentAssemblyQualifiedTypeName: baseDescriptor.assemblyQualifiedTypeName,
            description: descriptor?.description,
            json: descriptor?.optionsJson,
          };

          const invoke = descriptor?.optionsId > 0 ? BApi.componentOptions.putComponentOptions : BApi.componentOptions.addComponentOptions;
          const args = descriptor?.optionsId > 0 ? [descriptor?.optionsId, data] : [data];
          console.log(args);

          invoke(...args)
            .then(a => {
              if (a.code) {
                reject();
              } else {
                resolve(a);
                close(true);
              }
            })
            .catch(e => {
              reject(e);
            });
        } else {
          reject();
        }
      })}
      onClose={() => close(false)}
      onCancel={() => close(false)}
      afterClose={() => {
        console.log('closed');
        if (onClosed) {
          onClosed(closeWithChangesRef.current);
        }
        if (afterClose) {
          afterClose();
        }
      }}
      {...(otherProps || {})}
    >
      <div className="label">{i18n.t('Type')}</div>
      <div className="value ">
        <div className="base-components">
          {baseDescriptors.map(c => {
            return (
              <ComponentDescriptorCard
                descriptor={c}
                selected={c == baseDescriptor}
                onClick={() => {
                  setBaseDescriptor(c);
                }}
              />
            );
          })}
        </div>
      </div>
      <div className="label">{i18n.t('Name')}</div>
      <div className="value">
        <Input
          onChange={v => {
            setDescriptor({
              ...descriptor,
              name: v,
            });
          }}
          value={descriptor?.name}
        />
      </div>
      <div className="label">{i18n.t('Description')}</div>
      <div className="value">
        <Input.TextArea
          value={descriptor?.description}
          placeholder={baseDescriptor?.description ? i18n.t(baseDescriptor?.description) : undefined}
          onChange={v => {
            setDescriptor({
              ...(descriptor || {}),
              description: v,
            });
          }}
        />
      </div>
      {optionsJsonSchema && (
        <>
          <div className="label">{i18n.t('Configuration')}</div>
          <div className="value">
            <ComponentOptionsRender
              defaultValue={descriptor?.optionsJson ? JSON.parse(descriptor?.optionsJson) : undefined}
              onChange={v => {
                console.log('Options changed', v);
                setDescriptor({
                  ...(descriptor),
                  optionsJson: JSON.stringify(v),
                });
              }}
              schema={optionsJsonSchema}
              ref={optionsFormRef}
            />
          </div>
        </>
      )}
    </Dialog>
  );
}

ComponentDetail.show = (props) => {
  const { key = `component-detail-${uuidv4()}` } = props;
  const node = document.createElement('div');
  document.body.appendChild(node);

  const root = ReactDOM.createRoot(node);

  const unmount = () => {
    console.log(key);
    root.unmount();
    node.parentElement.removeChild(node);
  };

  root.render(
    // <React.StrictMode>
    <store.Provider>
      <ComponentDetail
        {...props}
        afterClose={unmount}
      />
    </store.Provider>,
    // </React.StrictMode>,
  );

  return {
    key,
    close: unmount,
  };
};

export default ComponentDetail;
