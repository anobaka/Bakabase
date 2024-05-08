import type React from 'react';
import type { ValueRendererProps } from './components/ValueRenderer';
import type { ValueEditorProps } from './components/ValueEditor/models';

export interface FilterValueProps<V> {
  editing: boolean;
  value?: V;
  onChange?: (value: V) => any;
}

export interface FilterValueContext {
  value?: any;
  displayValue?: ValueRendererProps['value'];
  ValueComponent: React.ComponentType<ValueRendererProps>;
  EditorComponent: React.ComponentType<ValueEditorProps>;
}
