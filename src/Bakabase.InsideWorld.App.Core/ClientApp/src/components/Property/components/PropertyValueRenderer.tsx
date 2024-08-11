import { useTranslation } from 'react-i18next';
import React from 'react';
import type { Dayjs } from 'dayjs';
import type { Duration } from 'dayjs/plugin/duration';
import type { ChoicePropertyOptions, IProperty } from '@/components/Property/models';
import { CustomPropertyType, ResourceProperty, StandardValueType } from '@/sdk/constants';
import {
  AttachmentValueRenderer,
  BooleanValueRenderer,
  ChoiceValueRenderer,
  DateTimeValueRenderer,
  FormulaValueRenderer,
  LinkValueRenderer,
  MultilevelValueRenderer,
  MultilineStringValueRenderer,
  NumberValueRenderer,
  RatingValueRenderer,
  StringValueRenderer,
  TagsValueRenderer,
  TimeValueRenderer,
} from '@/components/StandardValue';
import type { MultilevelData } from '@/components/StandardValue/models';
import { deserializeStandardValue, serializeStandardValue } from '@/components/StandardValue/helpers';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';
import property from '@/components/Property';


export type DataPool = {};

export type Props = {
  property: IProperty;
  /**
   * Serialized
   */
  onValueChange?: (dbValue?: string, bizValue?: string) => any;
  /**
   * Serialized
   */
  bizValue?: string;
  /**
   * Serialized
   */
  dbValue?: string;
  variant?: 'default' | 'light';
  dataPool?: {
    categoryMap: Record<number, { id: number; name: string }>;
    mediaLibraryMap: Record<number, { id: number; name: string; categoryId: number; resourceCount: number }>;
  };
};

const log = buildLogger('PropertyValueRenderer');

export default (props: Props) => {
  const {
    property,
    dataPool,
    variant = 'default',
    onValueChange,
    dbValue,
    bizValue,
  } = props;
  const { t } = useTranslation();
  log(props);

  const bv = deserializeStandardValue(bizValue ?? null, property.bizValueType);
  const dv = deserializeStandardValue(dbValue ?? null, property.dbValueType);

  const simpleOnValueChange: ((dbValue?: any, bizValue?: any) => any) | undefined = onValueChange
    ? (dv, bv) => {
      const sdv = serializeStandardValue(dv ?? null, property.dbValueType);
      const sbv = serializeStandardValue(bv ?? null, property.bizValueType);
      log('OnValueChange:Serialization:dv', dv, sdv);
      log('OnValueChange:Serialization:bv', bv, sbv);
      return onValueChange(sdv, sbv);
    }
    : undefined;

  const simpleEditor = simpleOnValueChange ? {
    value: dv,
    onValueChange: simpleOnValueChange,
  } : undefined;

  if (!property.isCustom) {
    switch (property.id as ResourceProperty) {
      // case ResourceProperty.RootPath:
      //   break;
      // case ResourceProperty.ParentResource:
      //   break;
      // case ResourceProperty.Resource:
      //   break;
      case ResourceProperty.FileName:
      case ResourceProperty.DirectoryPath: {
        return (
          <StringValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case ResourceProperty.Introduction: {
        return (
          <StringValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
            multiline
          />
        );
      }
      case ResourceProperty.Rating: {
        return (
          <RatingValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      // case ResourceProperty.CustomProperty:
      //   break;
      case ResourceProperty.CreatedAt:
      case ResourceProperty.FileCreatedAt:
      case ResourceProperty.FileModifiedAt: {
        return (
          <DateTimeValueRenderer
            value={bv}
            as={'datetime'}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case ResourceProperty.MediaLibrary: {
        // const v = dbValue == undefined ? undefined : JSON.parse(dbValue) as number[];
        const categoryMap = dataPool?.categoryMap || {};
        const mediaLibraryMap = dataPool?.mediaLibraryMap || {};
        //
        // const mls = v?.map(id => mediaLibraryMap[id]).filter(x => x);
        // const bv: string[][] | undefined = mls?.map(ml => {
        //   const c = categoryMap[ml.categoryId];
        //   return [c?.name ?? t?.('Unknown category') ?? 'Unknown category', ml.name];
        // });

        return (
          <MultilevelValueRenderer
            variant={variant}
            value={bv}
            multiple
            getDataSource={async () => {
              const multilevelData: MultilevelData<string>[] = [];
              Object.values(categoryMap).forEach((c) => {
                const md: MultilevelData<string> = {
                  value: `c-${c.id}`,
                  label: c.name,
                  children: Object.values(mediaLibraryMap)
                    .filter(l => l.categoryId == c.id)
                    .map<MultilevelData<string>>(x => ({
                      value: x.id.toString(),
                      label: x.name,
                    })),
                };

                multilevelData.push(md);
              });
              return multilevelData.filter(d => d.children && d.children.length > 0);
            }}
            editor={simpleEditor}
          />
        );
      }
      default:
        return (
          <span>{t?.('Unsupported property')}</span>
        );
    }
  } else {
    switch (property.type!) {
      case CustomPropertyType.SingleLineText: {
        return (
          <StringValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.MultilineText: {
        return (
          <StringValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
            multiline
          />
        );
      }
      case CustomPropertyType.SingleChoice: {
        const oc = onValueChange == undefined ? undefined : (dbValue?: string[], bizValue?: string[]) => {
          onValueChange(
            (dbValue && dbValue.length > 0) ? JSON.stringify(dbValue[0]) : undefined,
            (bizValue && bizValue.length > 0) ? JSON.stringify(bizValue[0]) : undefined,
          );
        };

        const editor = oc ? {
          value: dv == undefined ? undefined : [dv],
          onValueChange: oc,
        } : undefined;

        return (
          <ChoiceValueRenderer
            value={bv == undefined ? undefined : [bv]}
            variant={variant}
            editor={editor}
            getDataSource={async () => {
              return property.options?.choices ?? [];
            }}
          />
        );
      }
      case CustomPropertyType.MultipleChoice: {
        return (
          <ChoiceValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
            multiple
            getDataSource={async () => {
              return property.options?.choices ?? [];
            }}
          />
        );
      }
      case CustomPropertyType.Number: {
        return (
          <NumberValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Percentage: {
        return (
          <NumberValueRenderer
            value={bv}
            variant={variant}
            suffix={'%'}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Rating: {
        return (
          <RatingValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Boolean: {
        return (
          <BooleanValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Link: {
        return (
          <LinkValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Attachment: {
        return (
          <AttachmentValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Date:
      case CustomPropertyType.DateTime: {
        // const oc = onValueChange == undefined ? undefined : (dbValue?: Dayjs, bizValue?: Dayjs) => {
        //   onValueChange(
        //     serializeStandardValue(dbValue, StandardValueType.DateTime),
        //     serializeStandardValue(bizValue, StandardValueType.DateTime),
        //   );
        // };
        //
        // const editor = oc ? {
        //   value: dv,
        //   onValueChange: oc,
        // } : undefined;

        return (
          <DateTimeValueRenderer
            value={bv}
            as={property.type == CustomPropertyType.DateTime ? 'datetime' : 'date'}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Time: {
        // const oc = onValueChange == undefined ? undefined : (dbValue?: Duration, bizValue?: Duration) => {
        //   onValueChange(
        //     serializeStandardValue(dbValue, StandardValueType.Time),
        //     serializeStandardValue(bizValue, StandardValueType.Time),
        //   );
        // };
        //
        // const editor = oc ? {
        //   value: dv,
        //   onValueChange: oc,
        // } : undefined;

        return (
          <TimeValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Formula: {
        return (
          <FormulaValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Multilevel: {
        return (
          <MultilevelValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
      case CustomPropertyType.Tags: {
        return (
          <TagsValueRenderer
            value={bv}
            variant={variant}
            editor={simpleEditor}
          />
        );
      }
    }
  }
};
