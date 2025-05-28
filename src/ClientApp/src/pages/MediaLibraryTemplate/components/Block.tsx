import { AiOutlineBlock } from 'react-icons/ai';
import { Button, Divider } from '@/components/bakaui';

type Props = {
  title: any;
  description?: any;
  descriptionPlacement?: 'right' | 'bottom';
  leftIcon?: any;
  rightIcon?: any;
  onRightIconPress?: any;
  children: any;
};

export default ({ leftIcon, title, description, descriptionPlacement = 'right', rightIcon, onRightIconPress, children }: Props) => {
  return (
    <div className={'flex flex-col gap-1'}>
      <div className={'flex flex-col gap-1'}>
        <div className={'flex items-center gap-1'}>
          {leftIcon ?? <AiOutlineBlock className={'text-lg'} />}
          <div className={'text-medium'}>{title}</div>
          {rightIcon && onRightIconPress ? (
            <Button
              isIconOnly
              variant={'light'}
              color={'secondary'}
              size={'sm'}
              onPress={onRightIconPress}
            >
              {rightIcon}
            </Button>
          ) : rightIcon}
          {descriptionPlacement == 'right' && (
            <div className={'opacity-60'}>{description}</div>
          )}
        </div>
        {descriptionPlacement == 'bottom' && (
          <div className={'opacity-60'}>{description}</div>
        )}
      </div>
      <div className={'flex gap-1'}>
        <Divider className={'h-auto'} orientation={'vertical'} />
        <div className={'grow'}>
          {children}
        </div>
      </div>
    </div>
  );
};
