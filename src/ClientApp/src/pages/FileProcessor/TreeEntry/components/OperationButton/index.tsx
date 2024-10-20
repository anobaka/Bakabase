import type { ButtonProps } from '@/components/bakaui';
import { Button } from '@/components/bakaui';

type Props = {} & Omit<ButtonProps, 'ref'>;

export default ({
                  onClick,
                  children,
                  size = 'sm',
                  variant = 'light',
                  className,
                  ...props
                }: Props) => {
  return (
    <Button
      size={size}
      variant={variant}
      className={`w-auto h-auto p-1 min-w-fit opacity-70 hover:opacity-100 ${className}`}
      onClick={(e) => {
        e.stopPropagation();
        onClick?.(e);
      }}
      {...props}
    >
      {children}
    </Button>
  );
};
