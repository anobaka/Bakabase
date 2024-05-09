import type React from 'react';
import type { Key } from '@react-types/shared';

export type ValueRendererPortalProps = {
  key?: Key;
  onClick?: () => any;
};

export type ValueEditorPortalProps = {
  key?: Key;
  onChange: (value?: string) => any;
};

export interface FilterValueContext {
  renderValueRenderer: (props: ValueRendererPortalProps) => React.ReactNode;
  renderValueEditor: (props: ValueEditorPortalProps) => React.ReactNode;
}

export enum RenderType {
  StringValue = 1,
  MediaLibrary,
  ChoiceValue,
  MultipleChoiceValue,
  MultilevelValue,
  NumberValue,
  BooleanValue,
  DateValue,
  DateTimeValue,
  TimeValue,
}
