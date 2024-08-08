import { DeleteOutlined } from '@ant-design/icons';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { EnhancerTargetFullOptions } from '../../models';
import { defaultCategoryEnhancerTargetOptions } from '../../models';
import type { EnhancerDescriptor, EnhancerTargetDescriptor } from '../../../../models';
import PropertyTip from './PropertyTip';
import TargetOptions from './TargetOptions';
import type { TableRowProps } from '@/components/bakaui';
import { Button, Input, TableCell, TableRow } from '@/components/bakaui';
import PropertySelector from '@/components/PropertySelector';
import BApi from '@/sdk/BApi';
import { PropertyLabel } from '@/components/Property';
import type { IProperty } from '@/components/Property/models';
import { EnhancerTargetType, SpecialTextType, StandardValueType } from '@/sdk/constants';
import { IntegrateWithSpecialTextLabel } from '@/components/SpecialText';

interface Props extends Omit<TableRowProps, 'target' | 'property' | 'children'>{
  target: number;
  dynamicTarget?: string;
  descriptor: EnhancerTargetDescriptor;
  options?: Partial<EnhancerTargetFullOptions>;
  otherDynamicTargetsInGroup?: string[];
  onDeleted?: () => any;
  property?: IProperty;
  category: {name: string; id: number; customPropertyIds?: number[]};
  onPropertyChanged?: () => any;
  onCategoryChanged?: () => any;
  enhancer: EnhancerDescriptor;
}

const StdValueSpecialTextIntegrationMap: { [key in StandardValueType]?: SpecialTextType } = {
  [StandardValueType.DateTime]: SpecialTextType.DateTime,
};

export default (props: Props) => {
  const { t } = useTranslation();
  const {
    target,
    dynamicTarget,
    options: propsOptions,
    onDeleted,
    otherDynamicTargetsInGroup,
    property,
    descriptor,
    category,
    onPropertyChanged,
    onCategoryChanged,
    enhancer,
    ...tableRowProps
  } = props;

  const [options, setOptions] = useState<Partial<EnhancerTargetFullOptions>>(propsOptions ?? defaultCategoryEnhancerTargetOptions(target, descriptor.optionsItems));
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

  const patchTargetOptions = (patches: Partial<EnhancerTargetFullOptions>) => {
    setOptions({
      ...options,
      ...patches,
    });
    return BApi.category.patchCategoryEnhancerTargetOptions(category.id, enhancer.id, {
      target: target,
      dynamicTarget: dynamicTarget,
    }, patches);
  };

  const targetLabel = descriptor.isDynamic ? dynamicTarget ?? t('Default') : descriptor.name;
  const isDefaultTargetOfDynamic = descriptor.isDynamic && dynamicTarget == undefined;
  const integratedSpecialTextType = StdValueSpecialTextIntegrationMap[descriptor.id];

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
                // color={(dynamicTarget != undefined && dynamicTarget.length > 0) ? 'warning' : 'default'}
                onClick={() => {
                  dynamicTargetInputValueRef.current = dynamicTarget;
                  setEditingDynamicTarget(true);
                }}
              >{targetLabel ?? t('Click to specify target')}</Button>
            ) : (
              <>
                {targetLabel}
                {integratedSpecialTextType && (
                  <IntegrateWithSpecialTextLabel type={integratedSpecialTextType} />
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
        {(isDefaultTargetOfDynamic || descriptor.type != EnhancerTargetType.Data) ? (
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
                  pool: 'custom',
                  multiple: false,
                  onSubmit: async properties => {
                    patchTargetOptions({
                      propertyId: properties[0].id,
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
              <PropertyTip
                property={property}
                category={category}
                onAllowAddingNewDataDynamicallyEnabled={onPropertyChanged}
                onPropertyBoundToCategory={onCategoryChanged}
              />
            )}
          </div>
        )}
      </div>
      <div className={'w-1/4'}>
        <div className={'flex flex-col gap-1'}>
          <TargetOptions
            options={options}
            optionsItems={descriptor.optionsItems}
            onChange={patchTargetOptions}
          />
        </div>
      </div>
      <div className={'w-1/12'}>
        <div className={'flex flex-col gap-1'}>
          {!descriptor.isDynamic && (
            <Button
              isIconOnly
              size={'sm'}
              color={'danger'}
              variant={'light'}
              onClick={() => {
                BApi.category.deleteCategoryEnhancerTargetOptions(category.id, enhancer.id, {
                  target,
                  dynamicTarget,
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
