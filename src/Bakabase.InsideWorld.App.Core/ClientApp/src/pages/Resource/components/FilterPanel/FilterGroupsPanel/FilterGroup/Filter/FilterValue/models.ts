import type React from 'react';

export type ValueRendererPortalProps = {
  onClick?: () => any;
};

export type ValueEditorPortalProps = {
  onChange: (value?: string) => any;
};

export interface FilterValueContext {
  ValueRenderer: React.ComponentType<ValueRendererPortalProps>;
  ValueEditor: React.ComponentType<ValueEditorPortalProps>;
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
