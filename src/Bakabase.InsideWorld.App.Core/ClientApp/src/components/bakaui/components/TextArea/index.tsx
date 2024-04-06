import { Textarea as NextUiTextArea } from '@nextui-org/react';
import type * as react from 'react';
import { forwardRef } from 'react';
import type { Color } from '../../types';

interface TextAreaProps {
  startContent?: React.ReactNode;
  endContent?: React.ReactNode;
  placeholder?: string;
  className?: any;
  label?: react.ReactNode;
  size?: 'sm' | 'md' | 'lg';
  value?: string;
  defaultValue?: string;
  color?: Color;
  onValueChange?: (value: string) => any;
}

const TextArea = forwardRef<HTMLTextAreaElement, TextAreaProps>((props: TextAreaProps, ref) => {
  return (
    <NextUiTextArea
      ref={ref}
      {...props}
    />
  );
});


export default TextArea;
