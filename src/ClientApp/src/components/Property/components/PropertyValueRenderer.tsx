import { useTranslation } from 'react-i18next';
import React from 'react';
import type { IProperty } from '@/components/Property/models';
import { PropertyType, ResourceProperty, PropertyPool, StandardValueType } from '@/sdk/constants';
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
import type { MultilevelData } from '@/components/StandardValue/models';
import { deserializeStandardValue, serializeStandardValue } from '@/components/StandardValue/helpers';
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
  // log(props);

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

  switch (property.type!) {
    case PropertyType.SingleLineText: {
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

      console.log(editor, property);

      return (
        <ChoiceValueRenderer
          value={bv == undefined ? undefined : [bv]}
          variant={variant}
          editor={editor}
          getDataSource={async () => {
            return property.options?.choices ?? [];
          }}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.MultipleChoice: {
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
        />
      );
    }
    case PropertyType.Number: {
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
      return (
        <MultilevelValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          getDataSource={async () => {
            return property?.options?.data || [];
          }}
          multiple={property?.options?.hasSingleValue ?? true}
          defaultEditing={defaultEditing}
        />
      );
    }
    case PropertyType.Tags: {
      return (
        <TagsValueRenderer
          value={bv}
          variant={variant}
          editor={simpleEditor}
          getDataSource={async () => {
            return property?.options?.tags || [];
          }}
          defaultEditing={defaultEditing}
        />
      );
    }
  }
};
