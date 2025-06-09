import {
  DeleteOutlined,
  DisconnectOutlined,
  EditOutlined,
  InfoCircleOutlined,
  WarningOutlined,
} from '@ant-design/icons';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import type { EnhancerTargetFullOptions } from '../../models';
import { defaultCategoryEnhancerTargetOptions } from '../../models';
import type { EnhancerDescriptor, EnhancerTargetDescriptor } from '../../../../models';
import PropertyTip from './PropertyTip';
import TargetOptions from './TargetOptions';
import { Button, Chip, Input, Tooltip } from '@/components/bakaui';
import PropertySelector from '@/components/PropertySelector';
import BApi from '@/sdk/BApi';
import { PropertyLabel } from '@/components/Property';
import type { IProperty } from '@/components/Property/models';
import { PropertyPool, SpecialTextType, StandardValueType } from '@/sdk/constants';
import { IntegrateWithSpecialTextLabel } from '@/components/SpecialText';
import { buildLogger } from '@/components/utils';

interface Props {
  target: number;
  dynamicTarget?: string;
  descriptor: EnhancerTargetDescriptor;
  options?: Partial<EnhancerTargetFullOptions>;
  otherDynamicTargetsInGroup?: string[];
  onDeleted?: () => any;
  propertyMap?: { [key in PropertyPool]?: Record<number, IProperty> };
  category: { name: string; id: number; customPropertyIds?: number[] };
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

  useUpdateEffect(() => {
    setOptions(propsOptions ?? defaultCategoryEnhancerTargetOptions(descriptor));
  }, [propsOptions]);

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
  if (options.propertyPool != undefined && options.propertyId != undefined) {
    property = propertyMap?.[options.propertyPool]?.[options.propertyId];
  }

  const noPropertyBound = !options.autoBindProperty && (!options.propertyId || !options.propertyPool);

  log(props, options, propsOptions, property);

  return (
    <div className={`flex items-center gap-1 ${noPropertyBound ? 'opacity-60' : ''}`}>
      <div className={'w-5/12'}>
        <div className={'flex flex-col gap-2'}>
          <div className={'flex items-center gap-1'}>
            {noPropertyBound && (
              <Tooltip content={t('Target will not be applied because there is no property to store it.')}>
                <DisconnectOutlined className={'text-base text-warning'} />
              </Tooltip>
            )}
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
                // size={'sm'}
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
                <Chip
                  // size={'sm'}
                  variant={'light'}
                  // color={'success'}
                >
                  {targetLabel}
                </Chip>
                {/* {targetLabel} */}
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
          <Chip variant={'light'}>
            /
          </Chip>
        ) : (
          <div className={'flex items-center gap-1'}>
            <Button
              // size={'sm'}
              isDisabled={options?.autoBindProperty}
              variant={'light'}
              color={property ? 'success' : 'primary'}
              onClick={() => {
                PropertySelector.show({
                  addable: true,
                  editable: true,
                  pool: PropertyPool.Custom | PropertyPool.Reserved,
                  multiple: false,
                  selection: property ? [property] : [],
                  onSubmit: async properties => {
                    patchTargetOptions({
                      propertyId: properties[0].id,
                      propertyPool: properties[0].pool,
                    });
                    onPropertyChanged?.();
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
                  // onAllowAddingNewDataDynamicallyEnabled={onPropertyChanged}
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
