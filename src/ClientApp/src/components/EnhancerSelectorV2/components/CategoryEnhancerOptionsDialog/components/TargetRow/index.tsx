import { DeleteOutlined, DisconnectOutlined, EditOutlined, InfoCircleOutlined } from '@ant-design/icons';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { EnhancerTargetFullOptions } from '../../models';
import { defaultCategoryEnhancerTargetOptions } from '../../models';
import type { EnhancerDescriptor, EnhancerTargetDescriptor } from '../../../../models';
import PropertyTip from './PropertyTip';
import TargetOptions from './TargetOptions';
import { Button, Input, Tooltip } from '@/components/bakaui';
import PropertySelector from '@/components/PropertySelector';
import BApi from '@/sdk/BApi';
import { PropertyLabel } from '@/components/Property';
import type { IProperty } from '@/components/Property/models';
import {
  reservedResourceProperties,
  ResourceProperty,
  ResourcePropertyType,
  SpecialTextType,
  StandardValueType,
} from '@/sdk/constants';
import { IntegrateWithSpecialTextLabel } from '@/components/SpecialText';
import { buildLogger } from '@/components/utils';
import store from '@/store';

interface Props {
  target: number;
  dynamicTarget?: string;
  descriptor: EnhancerTargetDescriptor;
  options?: Partial<EnhancerTargetFullOptions>;
  otherDynamicTargetsInGroup?: string[];
  onDeleted?: () => any;
  propertyMap?: Record<number, IProperty>;
  category: {name: string; id: number; customPropertyIds?: number[]};
  onPropertyChanged?: () => any;
  onCategoryChanged?: () => any;
  enhancer: EnhancerDescriptor;
  onChange?: (options: Partial<EnhancerTargetFullOptions>) => any;
}

const StdValueSpecialTextIntegrationMap: { [key in StandardValueType]?: SpecialTextType } = {
  [StandardValueType.DateTime]: SpecialTextType.DateTime,
};

const log = buildLogger('TargetRow');

export default (props: Props) => {
  const { t } = useTranslation();
  const {
    target,
    dynamicTarget,
    options: propsOptions,
    onDeleted,
    otherDynamicTargetsInGroup,
    propertyMap,
    descriptor,
    category,
    onPropertyChanged,
    onCategoryChanged,
    enhancer,
    onChange,
  } = props;
  const internalOptions = store.useModelState('internalOptions');

  const [options, setOptions] = useState<Partial<EnhancerTargetFullOptions>>(propsOptions ?? defaultCategoryEnhancerTargetOptions(descriptor));
  const [dynamicTargetError, setDynamicTargetError] = useState<string>();
  const dynamicTargetInputValueRef = useRef<string>();
  const [editingDynamicTarget, setEditingDynamicTarget] = useState(false);

  const validateDynamicTarget = (newTarget: string) => {
    let error;
    if (otherDynamicTargetsInGroup?.includes(newTarget)) {
      error = t('Duplicate dynamic target is found');
    }
    if (newTarget.length == 0) {
      error = t('This field is required');
    }
    if (dynamicTargetError != error) {
      setDynamicTargetError(error);
    }
    return error == undefined;
  };

  const patchTargetOptions = async (patches: Partial<EnhancerTargetFullOptions>) => {
    const newOptions = {
      ...options,
      ...patches,
    };
    setOptions(newOptions);

    // log('Patch target options', {
    //   ...options,
    //   ...patches,
    // });

    await BApi.category.patchCategoryEnhancerTargetOptions(category.id, enhancer.id, {
      target: target,
      dynamicTarget: dynamicTarget,
    }, patches);

    onChange?.(newOptions);
  };

  const unbindProperty = async () => {
    await BApi.category.unbindCategoryEnhancerTargetProperty(category.id, enhancer.id, {
      target: target,
      dynamicTarget: dynamicTarget,
    });
    options.propertyId = undefined;
    setOptions({ ...options });
    onChange?.(options);
  };

  const dt = options.dynamicTarget ?? dynamicTarget;

  const targetLabel = descriptor.isDynamic ? dt ?? t('Default') : descriptor.name;
  const isDefaultTargetOfDynamic = descriptor.isDynamic && dt == undefined;
  const integratedSpecialTextType = StdValueSpecialTextIntegrationMap[descriptor.valueType];

  let property: IProperty | undefined;
  if (options.propertyType != undefined && options.propertyId != undefined) {
    switch (options.propertyType!) {
      case ResourcePropertyType.Reserved: {
        const p = internalOptions.resource.reservedResourcePropertyDescriptorMap?.[options.propertyId];
        if (p) {
          property = {
            ...p,
            name: t(`ResourceProperty.${ResourceProperty[options.propertyId]}`),
          };
        }
        break;
      }

      case ResourcePropertyType.Custom:
        property = propertyMap?.[options.propertyId];
        break;
      case ResourcePropertyType.Internal:
        break;
      case ResourcePropertyType.All:
        break;
    }
  }

  return (
    <div className={'flex items-center gap-1'}>
      <div className={'w-5/12'}>
        <div className={'flex flex-col gap-2'}>
          <div className={'flex items-center gap-1'}>
            {(descriptor.isDynamic && !isDefaultTargetOfDynamic) ? editingDynamicTarget ? (
              <Input
                size={'sm'}
                defaultValue={dynamicTargetInputValueRef.current}
                isInvalid={dynamicTargetError != undefined}
                onValueChange={v => {
                  if (validateDynamicTarget(v)) {
                    dynamicTargetInputValueRef.current = v;
                  }
                }}
                errorMessage={dynamicTargetError}
                onBlur={() => {
                  if (dynamicTargetInputValueRef.current != undefined && dynamicTargetInputValueRef.current.length > 0) {
                    patchTargetOptions({ dynamicTarget: dynamicTargetInputValueRef.current });
                  }
                  dynamicTargetInputValueRef.current = undefined;
                  setEditingDynamicTarget(false);
                }}
              />
            ) : (
              <Button
                size={'sm'}
                variant={'light'}
                // color={'success'}
                onClick={() => {
                  dynamicTargetInputValueRef.current = dt;
                  setEditingDynamicTarget(true);
                }}
              >
                {targetLabel ?? t('Click to specify target')}
                <EditOutlined className={'text-base'} />
              </Button>
            ) : (
              <>
                {targetLabel}
                {integratedSpecialTextType && (
                  <IntegrateWithSpecialTextLabel type={integratedSpecialTextType} />
                )}
                {descriptor.description && (
                  <Tooltip
                    content={descriptor.description}
                    placement={'right'}
                  >
                    <InfoCircleOutlined className={'text-base'} />
                  </Tooltip>
                )}
              </>
            )}
          </div>
          {/* <div className={'flex items-center gap-1 opacity-60'}> */}
          {/*   <StandardValueIcon valueType={target.valueType} className={'text-small'} /> */}
          {/*   {t(`StandardValueType.${StandardValueType[target.valueType]}`)} */}
          {/* </div> */}
        </div>
      </div>
      <div className={'w-1/4'}>
        {(isDefaultTargetOfDynamic) ? (
          '/'
        ) : (
          <div className={'flex items-center gap-1'}>
            <Button
              // size={'sm'}
              disabled={options?.autoGenerateProperties}
              variant={'light'}
              color={property ? 'success' : 'primary'}
              onClick={() => {
                PropertySelector.show({
                  addable: true,
                  editable: true,
                  pool: ResourcePropertyType.Custom | ResourcePropertyType.Reserved,
                  multiple: false,
                  selection: property ? [property] : [],
                  onSubmit: async properties => {
                    patchTargetOptions({
                      propertyId: properties[0].id,
                      propertyType: properties[0].type,
                    });
                  },
                });
              }}
            >
              {property ? (
                <PropertyLabel property={property} />
              ) : t('Select a property')}
            </Button>
            {property && (
              <>
                <PropertyTip
                  property={property}
                  category={category}
                  onAllowAddingNewDataDynamicallyEnabled={onPropertyChanged}
                  onPropertyBoundToCategory={onCategoryChanged}
                />
                <Tooltip
                  content={t('Unbind')}
                >
                  <Button
                    isIconOnly
                    color={'danger'}
                    variant={'light'}
                    onClick={() => {
                      unbindProperty();
                    }}
                  >
                    <DisconnectOutlined className={'text-base'} />
                  </Button>
                </Tooltip>
              </>
            )}
          </div>
        )}
      </div>
      <div className={'w-1/4'}>
        <div className={'flex flex-col gap-2'}>
          <TargetOptions
            options={options}
            optionsItems={descriptor.optionsItems}
            onChange={patchTargetOptions}
          />
        </div>
      </div>
      <div className={'w-1/12'}>
        <div className={'flex flex-col gap-1'}>
          {(descriptor.isDynamic && !isDefaultTargetOfDynamic) && (
            <Button
              isIconOnly
              size={'sm'}
              color={'danger'}
              variant={'light'}
              onClick={() => {
                BApi.category.deleteCategoryEnhancerTargetOptions(category.id, enhancer.id, {
                  target,
                  dynamicTarget: dt,
                });
                onDeleted?.();
              }}
            >
              <DeleteOutlined className={'text-base'} />
            </Button>
          )}
        </div>
      </div>
    </div>
  );
};
