import { useTranslation } from 'react-i18next';
import React from 'react';
import _ from 'lodash';
import type { IProperty } from '@/components/Property/models';
import { PropertyType, StandardValueType } from '@/sdk/constants';
import {
  AttachmentValueRenderer,
  BooleanValueRenderer,
  ChoiceValueRenderer,
  DateTimeValueRenderer,
  FormulaValueRenderer,
  LinkValueRenderer,
  MultilevelValueRenderer,
  NumberValueRenderer,
  RatingValueRenderer,
  StringValueRenderer,
  TagsValueRenderer,
  TimeValueRenderer,
} from '@/components/StandardValue';
import {
  deserializeStandardValue,
  findNodeChainInMultilevelData,
  serializeStandardValue,
} from '@/components/StandardValue/helpers';
import { buildLogger } from '@/components/utils';


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
  defaultEditing?: boolean;
};

const log = buildLogger('PropertyValueRenderer');

export default (props: Props) => {
  const {
    property,
    variant = 'default',
    onValueChange,
    dbValue,
    bizValue,
    defaultEditing,
  } = props;
  const { t } = useTranslation();

  let bv = deserializeStandardValue(bizValue ?? null, property.bizValueType);
  const dv = deserializeStandardValue(dbValue ?? null, property.dbValueType);

  log(props, bv, dv);

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

  switch (property.type!) {
    case PropertyType.SingleLineText: {
      bv ??= dv;
      return (
        <StringValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.MultilineText: {
      bv ??= dv;
      return (
        <StringValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          multiline
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.SingleChoice: {
      const oc = onValueChange == undefined ? undefined : (dbValue?: string[], bizValue?: string[]) => {
        onValueChange(
          (dbValue && dbValue.length > 0) ? serializeStandardValue(dbValue[0], StandardValueType.String) : undefined,
          (bizValue && bizValue.length > 0) ? serializeStandardValue(bizValue[0], StandardValueType.String) : undefined,
        );
      };

      const editor = oc ? {
        value: dv == undefined ? undefined : [dv],
        onValueChange: oc,
      } : undefined;

      // console.log(editor, property);

      bv ??= (property.options?.choices ?? []).find(x => x.value == dv)?.label;
      const vas = _.sortBy(property.options?.choices?.filter(o => dv?.includes(o.value)) ?? [],
        x => dv?.findIndex(d => d.value == x.value));
      return (
        <ChoiceValueRenderer
          value={bv == undefined ? undefined : [bv]}
          variant={variant}
          editor={editor}
          getDataSource={async () => {
            return property.options?.choices ?? [];
          }}
          defaultEditing={defaultEditing}
          valueAttributes={vas}
        />
      );
    }
    case PropertyType.MultipleChoice: {
      bv ??= (property.options?.choices ?? []).filter(x => dv?.includes(x.value)).map(x => x.label);
      const vas = _.sortBy(property.options?.choices?.filter(o => dv?.includes(o.value)) ?? [],
        x => dv?.findIndex(d => d.value == x.value));
      return (
        <ChoiceValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          multiple
          getDataSource={async () => {
            return property.options?.choices ?? [];
          }}
          defaultEditing={defaultEditing}
          valueAttributes={vas}
        />
      );
    }
    case PropertyType.Number: {
      bv ??= dv;

      return (
        <NumberValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
          as={'number'}
        />
      );
    }
    case PropertyType.Percentage: {
      bv ??= dv;

      return (
        <NumberValueRenderer
          value={bv}
          variant={variant}
          suffix={'%'}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
          as={'progress'}
        />
      );
    }
    case PropertyType.Rating: {
      bv ??= dv;

      return (
        <RatingValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.Boolean: {
      bv ??= dv;

      return (
        <BooleanValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.Link: {
      bv ??= dv;

      return (
        <LinkValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.Attachment: {
      bv ??= dv;

      return (
        <AttachmentValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.Date:
    case PropertyType.DateTime: {
      bv ??= dv;

      return (
        <DateTimeValueRenderer
          value={bv}
          as={property.type == PropertyType.DateTime ? 'datetime' : 'date'}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.Time: {
      bv ??= dv;

      return (
        <TimeValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.Formula: {
      bv ??= dv;

      return (
        <FormulaValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.Multilevel: {
      const tbv = dv?.map(v => findNodeChainInMultilevelData(property?.options?.data || [], v)).filter(x => x != undefined);
      bv ??= tbv?.map(x => x.map(y => y.label));
      const vas = tbv?.map(v => v.map(x => ({ color: x.color })));

      // log(tbv, bv, vas);

      return (
        <MultilevelValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          getDataSource={async () => {
            return property?.options?.data || [];
          }}
          multiple={property?.options?.valueIsSingleton ?? true}
          defaultEditing={defaultEditing}
          valueAttributes={vas}
        />
      );
    }
    case PropertyType.Tags: {
      bv ??= (property.options?.tags || []).filter(x => dv?.includes(x.value)).map(x => ({
        group: x.group,
        name: x.name,
      }));
      const vas = _.sortBy(property.options?.tags?.filter(o => dv?.includes(o.value)) ?? [],
        x => dv?.findIndex(d => d.value == x.value));
      return (
        <TagsValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          getDataSource={async () => {
            return property?.options?.tags || [];
          }}
          defaultEditing={defaultEditing}
          valueAttributes={vas}
        />
      );
    }
  }
};
