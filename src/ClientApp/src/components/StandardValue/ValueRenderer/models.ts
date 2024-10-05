import type { ValueEditorProps } from '../ValueEditor/models';

export type ValueRendererProps<TBizValue, TDbValue = TBizValue> = {
  value?: TBizValue;
  // onClick?: () => any;
  variant?: 'default' | 'light';
  defaultEditing?: boolean;

  editor?: ValueEditorProps<TDbValue, TBizValue>;
};
